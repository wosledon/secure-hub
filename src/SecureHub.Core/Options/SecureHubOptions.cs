namespace SecureHub.Core.Options;

public sealed class SecureHubOptions
{
    public const string SectionName = "SecureHub";

    public DatabaseOptions Database { get; set; } = new();
    public SecurityOptions Security { get; set; } = new();
    public ContentOptions Content { get; set; } = new();
    public GuardrailOptions Guardrails { get; set; } = new();
    public CompressionOptions Compression { get; set; } = new();
    public LoadBalancingOptions LoadBalancing { get; set; } = new();
    public LocalizationOptions Localization { get; set; } = new();
    public PersonalProviderOptions PersonalProviders { get; set; } = new();
}

public sealed class DatabaseOptions
{
    /// <summary>Sqlite | Postgres。</summary>
    public string Provider { get; set; } = "Sqlite";
    public string ConnectionString { get; set; } = "Data Source=./data/securehub.db";
    public bool AutoMigrate { get; set; } = true;
}

public sealed class SecurityOptions
{
    public bool RequireHttps { get; set; }
    public bool AllowInsecureTransport { get; set; } = true;
    public string AdminBindAddress { get; set; } = "127.0.0.1";
    public int SessionMinutes { get; set; } = 30;
    public int SessionAbsoluteHours { get; set; } = 12;
    public int LoginLockoutFailures { get; set; } = 5;
    public int LoginLockoutMinutes { get; set; } = 5;
    public bool TotpRequired { get; set; }
    public string DataDirectory { get; set; } = "./data";
    public int RequestBodyLimitBytes { get; set; } = 1_048_576;
}

public sealed class ContentOptions
{
    public int RawRetentionDays { get; set; } = 30;
    public int RedactedRetentionDays { get; set; } = 365;
    public int CompressedRetentionDays { get; set; } = 365;
    public int MaxContentBytes { get; set; } = 262_144;
    public int DiskWarnPercent { get; set; } = 20;
    public int DiskStopPercent { get; set; } = 10;
    public bool PersistContent { get; set; } = true;
}

public sealed class GuardrailOptions
{
    public int TotalBudgetMs { get; set; } = 8;
    public int PerDetectorBudgetMs { get; set; } = 2;
    public int ExternalTimeoutMs { get; set; } = 300;
    /// <summary>block | allow</summary>
    public string OnError { get; set; } = "block";
    public int ObservationModeDays { get; set; } = 7;
}

public sealed class CompressionOptions
{
    public bool Enabled { get; set; } = true;
    public int Version { get; set; } = 1;
    public double MinSavingsRatio { get; set; } = 0.02;
    /// <summary>passthrough | fail</summary>
    public string OnError { get; set; } = "passthrough";
    public bool DetectBoilerplate { get; set; } = true;
    public bool CollapseRepetition { get; set; } = true;
    public int MinRepeats { get; set; } = 3;
}

public sealed class LoadBalancingOptions
{
    public bool Enabled { get; set; }
    public bool AutoGroupSameModel { get; set; } = true;
    public bool AllowVariantMixing { get; set; }
    public bool AllowCrossScope { get; set; }
    /// <summary>priority-weighted | cost | latency</summary>
    public string Strategy { get; set; } = "priority-weighted";
    public double HealthPenalty { get; set; } = 0.5;
}

public sealed class LocalizationOptions
{
    public string Default { get; set; } = "zh-CN";
    public string[] Supported { get; set; } = ["zh-CN", "en"];
}

public sealed class PersonalProviderOptions
{
    public bool Enabled { get; set; } = true;
    public string[] AllowedProviders { get; set; } = [];
    /// <summary>full | metadata | off</summary>
    public string AuditLevel { get; set; } = "full";
    public Guid[] BlockedUsers { get; set; } = [];
}
