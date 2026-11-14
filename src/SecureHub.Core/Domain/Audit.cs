namespace SecureHub.Core.Domain;

public sealed class AuditEvent
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; }
    public long Sequence { get; set; }
    public string EventType { get; set; } = string.Empty;
    public AuditSeverity Severity { get; set; } = AuditSeverity.Info;
    public string ActorType { get; set; } = "system";
    public string ActorId { get; set; } = string.Empty;
    public string ActorLabel { get; set; } = string.Empty;
    public Guid? AppId { get; set; }
    public Guid? TeamId { get; set; }
    public string? RequestId { get; set; }
    public string? ModelAlias { get; set; }
    public Guid? ProviderId { get; set; }
    public string? UpstreamModel { get; set; }
    public Guid? ModelGroupId { get; set; }
    public Guid? ModelMemberId { get; set; }
    public GuardrailAction Action { get; set; } = GuardrailAction.Allow;
    public string? PolicyId { get; set; }
    public int? PolicyVersion { get; set; }
    public string? RuleId { get; set; }
    public string ReasonCode { get; set; } = string.Empty;

    /// <summary>JSON 扩展信息；禁止包含正文。</summary>
    public string? Detail { get; set; }

    public int? InputTokens { get; set; }
    public int? InputTokensOriginal { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? CostUsd { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>正文 SHA-256（hex），用于与内容表比对。</summary>
    public string? ContentHash { get; set; }

    public bool ContentMissing { get; set; }
    public BillingOwner BillingOwner { get; set; }
    public string PrevHash { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
}

public sealed class AuditAnchor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Sequence { get; set; }
    public string Hash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AuditEventDispute
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public string DisputeType { get; set; } = "false_positive";
    public string? Note { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
