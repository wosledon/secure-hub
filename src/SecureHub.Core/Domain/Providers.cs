namespace SecureHub.Core.Domain;

public sealed class Provider
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>全局唯一别名，用于三级模型 ID 的 provider 段。</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>models.dev provider id，如 openai / azure / deepseek。</summary>
    public string ProviderKey { get; set; } = string.Empty;

    public ProviderScope Scope { get; set; } = ProviderScope.Org;
    public Guid? OwnerUserId { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string HealthStatus { get; set; } = "unknown";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? LastTestedAt { get; set; }
    public string? LastTestError { get; set; }
}

public sealed class ProviderCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    public Guid? OwnerUserId { get; set; }

    /// <summary>DataProtection 加密后的密文。</summary>
    public byte[] CipherText { get; set; } = [];

    /// <summary>末 4 位，仅供用户辨认。</summary>
    public string KeyHint { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
}

/// <summary>models.dev 目录中的供应商元数据快照（用于接入向导，不包含任何凭据）。</summary>
public sealed class CatalogProvider
{
    /// <summary>models.dev provider id，如 openai / deepseek。</summary>
    public string ProviderKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>models.dev 提供的 OpenAI 兼容端点；部分供应商缺失，需要用户补充。</summary>
    public string? Api { get; set; }

    public string? Doc { get; set; }
    public string? Npm { get; set; }

    /// <summary>JSON 数组，形如 ["OPENAI_API_KEY"]。</summary>
    public string? EnvJson { get; set; }

    public int ModelCount { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}

/// <summary>供应商下的具体模型（provider × model 的 offering）。</summary>
public sealed class ProviderOffering
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }

    /// <summary>上游真实 model id，可含 '/'。</summary>
    public string UpstreamModel { get; set; } = string.Empty;

    /// <summary>provider-agnostic 规范模型 id（来自 models.dev）。</summary>
    public string? CanonicalModelId { get; set; }

    /// <summary>模型组聚合键，默认等于 UpstreamModel。</summary>
    public string GroupKey { get; set; } = string.Empty;

    public bool SupportsTools { get; set; }
    public bool SupportsReasoning { get; set; }
    public bool SupportsStructuredOutput { get; set; }
    public int ContextWindow { get; set; }
    public int MaxOutputTokens { get; set; }
    public decimal InputPricePerMillion { get; set; }
    public decimal OutputPricePerMillion { get; set; }
    public bool Enabled { get; set; } = true;
}
