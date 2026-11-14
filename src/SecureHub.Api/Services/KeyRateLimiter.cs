using System.Collections.Concurrent;
using SecureHub.Core.Security;

namespace SecureHub.Api.Services;

public sealed record RateLease(bool Allowed, string? ReasonCode, Guid KeyId, KeyRateLimiter Owner) : IDisposable
{
    public void Dispose() => Owner.Release(KeyId);
}

/// <summary>进程内每密钥限流（单机版）；集群部署时替换为分布式实现。</summary>
public sealed class KeyRateLimiter
{
    private sealed class Window
    {
        public long Minute;
        public int Requests;
        public long Tokens;
        public int Concurrent;
    }

    private readonly ConcurrentDictionary<Guid, Window> _windows = new();

    public RateLease Acquire(Guid keyId, KeyConstraints constraints)
    {
        var window = _windows.GetOrAdd(keyId, _ => new Window());
        var minute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;

        lock (window)
        {
            if (window.Minute != minute)
            {
                window.Minute = minute;
                window.Requests = 0;
                window.Tokens = 0;
            }

            if (constraints.MaxConcurrency is { } maxConcurrency && window.Concurrent >= maxConcurrency)
            {
                return new RateLease(false, "concurrency_exceeded", keyId, this);
            }

            if (constraints.RequestsPerMinute is { } rpm && window.Requests >= rpm)
            {
                return new RateLease(false, "rate_limit_exceeded", keyId, this);
            }

            if (constraints.TokensPerMinute is { } tpm && window.Tokens >= tpm)
            {
                return new RateLease(false, "token_rate_exceeded", keyId, this);
            }

            window.Requests++;
            window.Concurrent++;
            return new RateLease(true, null, keyId, this);
        }
    }

    public void AddTokens(Guid keyId, long tokens)
    {
        if (tokens <= 0) return;
        if (!_windows.TryGetValue(keyId, out var window)) return;

        lock (window)
        {
            window.Tokens += tokens;
        }
    }

    public void Release(Guid keyId)
    {
        if (!_windows.TryGetValue(keyId, out var window)) return;

        lock (window)
        {
            if (window.Concurrent > 0) window.Concurrent--;
        }
    }
}
