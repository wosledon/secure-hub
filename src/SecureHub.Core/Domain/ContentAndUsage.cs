namespace SecureHub.Core.Domain;

/// <summary>加密后的请求/响应内容。</summary>
public sealed class ContentBlob
{
    public Guid ContentId { get; set; } = Guid.NewGuid();
    public string RequestId { get; set; } = string.Empty;
    public ContentDirection Direction { get; set; }
    public ContentVariant Variant { get; set; }
    public Guid? AppId { get; set; }
    public byte[] CipherText { get; set; } = [];
    public byte[] Nonce { get; set; } = [];
    public byte[] Tag { get; set; } = [];
    public string DekId { get; set; } = string.Empty;

    /// <summary>明文 SHA-256（hex）。</summary>
    public string ContentHash { get; set; } = string.Empty;

    public int SizeBytes { get; set; }
    public bool Truncated { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class ContentDek
{
    public string DekId { get; set; } = string.Empty;

    /// <summary>KEK 包裹后的 DEK。</summary>
    public byte[] WrappedDek { get; set; } = [];

    public string Algorithm { get; set; } = "AES-256-GCM";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
}

/// <summary>
/// tokenize 脱敏令牌：把一个敏感值换成 <c>⟦sh:{TokenId}⟧</c> 外发，
/// 密文留在这里，响应回来时按 TokenId 还原（PRD §7.4）。
/// 令牌与请求绑定：AAD 含 requestId，跨请求复用会被 GCM 拒绝。
/// </summary>
public sealed class RedactionToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TokenId { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string DetectorId { get; set; } = string.Empty;
    public byte[] CipherText { get; set; } = [];
    public byte[] Nonce { get; set; } = [];
    public byte[] Tag { get; set; } = [];
    public string DekId { get; set; } = string.Empty;
    public int PlainLength { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>用量事件（按真实命中成员记账）。</summary>
public sealed class UsageEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? AppId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? VirtualKeyId { get; set; }
    public Guid? ProviderId { get; set; }
    public Guid? ModelGroupId { get; set; }
    public Guid? ModelMemberId { get; set; }
    public ModelScope ModelScope { get; set; }
    public BillingOwner BillingOwner { get; set; }
    public string Model { get; set; } = string.Empty;
    public string? UpstreamModel { get; set; }
    public int InputTokens { get; set; }
    public int InputTokensOriginal { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public bool Estimated { get; set; }
    public int DurationMs { get; set; }
    public string? ErrorCode { get; set; }
    public string? RequestId { get; set; }
}
