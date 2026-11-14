namespace SecureHub.Core.Domain;

/// <summary>安全策略主表；实际规则在版本表中。</summary>
public sealed class SecurityPolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PolicyKey { get; set; } = string.Empty;
    public int? ActiveVersion { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>策略版本：一旦发布只读，可回滚到旧版本。</summary>
public sealed class SecurityPolicyVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PolicyId { get; set; }
    public int Version { get; set; }
    public int Priority { get; set; }
    public string ScopeJson { get; set; } = "{}";
    public string WhenJson { get; set; } = "{}";
    public string ThenJson { get; set; } = "{}";
    public string OnError { get; set; } = "block";
    public string? RolloutJson { get; set; }
    public string? ChangeReason { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>预算/配额（周期累计）。</summary>
public sealed class Budget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public BudgetSubject Subject { get; set; }
    public Guid? SubjectId { get; set; }

    /// <summary>day | week | month</summary>
    public string Period { get; set; } = "month";

    public long? TokenLimit { get; set; }
    public decimal? CostLimit { get; set; }
    public long TokensUsed { get; set; }
    public decimal CostUsed { get; set; }
    public DateTimeOffset PeriodStart { get; set; }
    public bool Enabled { get; set; } = true;
}

public enum BudgetSubject
{
    All = 0,
    Team = 1,
    App = 2
}

/// <summary>模型能力目录条目（models.dev 同步）。</summary>
public sealed class CatalogModel
{
    public string Id { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? Family { get; set; }
    public bool SupportsTools { get; set; }
    public bool SupportsReasoning { get; set; }
    public bool SupportsStructuredOutput { get; set; }
    public bool SupportsAttachment { get; set; }
    public int ContextWindow { get; set; }
    public int MaxOutputTokens { get; set; }
    public decimal InputPricePerMillion { get; set; }
    public decimal OutputPricePerMillion { get; set; }
    public string? CanonicalModelId { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}
