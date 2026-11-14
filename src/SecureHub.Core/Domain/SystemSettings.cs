namespace SecureHub.Core.Domain;

/// <summary>可热更新的运行期配置（覆盖 appsettings 中的安全相关默认值）。</summary>
public sealed class SystemSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
