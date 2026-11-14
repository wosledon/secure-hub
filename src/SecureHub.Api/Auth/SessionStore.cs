using System.Collections.Concurrent;
using System.Security.Cryptography;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using Microsoft.Extensions.Options;

namespace SecureHub.Api.Auth;

public sealed record SessionEntry(
    Guid UserId,
    string UserName,
    Guid? TeamId,
    IReadOnlyList<UserRole> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt)
{
    public bool Has(UserRole role) => Roles.Contains(role);
    public bool IsEndUserOnly => Roles.Count > 0 && Roles.All(r => r == UserRole.EndUser);
}

/// <summary>内存会话（单机版）。集群化时替换为分布式缓存。</summary>
public sealed class SessionStore(IOptions<SecureHubOptions> options)
{
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.Ordinal);

    public string Create(User user, IReadOnlyList<UserRole> roles)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var now = DateTimeOffset.UtcNow;
        _sessions[token] = new SessionEntry(
            user.Id,
            user.UserName,
            user.TeamId,
            roles,
            now,
            now.AddHours(options.Value.Security.SessionAbsoluteHours));

        return token;
    }

    public SessionEntry? Get(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        if (!_sessions.TryGetValue(token, out var entry)) return null;

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return null;
        }

        return entry;
    }

    public void Remove(string? token)
    {
        if (!string.IsNullOrEmpty(token)) _sessions.TryRemove(token, out _);
    }

    public void RemoveAllForUser(Guid userId)
    {
        foreach (var pair in _sessions)
        {
            if (pair.Value.UserId == userId) _sessions.TryRemove(pair.Key, out _);
        }
    }

    /// <summary>密码变更后撤销该用户的其它会话，保留当前会话。</summary>
    public void RemoveAllForUserExcept(Guid userId, string? keepToken)
    {
        foreach (var pair in _sessions)
        {
            if (pair.Value.UserId == userId &&
                !string.Equals(pair.Key, keepToken, StringComparison.Ordinal))
            {
                _sessions.TryRemove(pair.Key, out _);
            }
        }
    }
}

public static class SessionCookie
{
    public const string Name = "sh_session";

    public static void Write(HttpContext http, string token, int hours)
    {
        http.Response.Cookies.Append(Name, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromHours(hours)
        });
    }

    public static void Clear(HttpContext http) =>
        http.Response.Cookies.Delete(Name);

    public static string? Read(HttpContext http) => http.Request.Cookies[Name];
}
