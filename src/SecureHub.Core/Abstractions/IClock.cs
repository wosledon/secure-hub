namespace SecureHub.Core.Abstractions;

/// <summary>可控时钟，便于测试与审计时间一致性。</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
