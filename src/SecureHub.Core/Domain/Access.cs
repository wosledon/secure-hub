namespace SecureHub.Core.Domain;

/// <summary>企业向团队/个人分配供应商与模型。</summary>
public sealed class Entitlement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public SubjectType SubjectType { get; set; }
    public Guid? SubjectId { get; set; }
    public Guid ProviderId { get; set; }

    /// <summary>JSON 数组；空表示该供应商全部模型。</summary>
    public string? ModelsJson { get; set; }

    public long? TokenQuota { get; set; }
    public decimal? CostQuota { get; set; }
    public long? TokensUsed { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>负载均衡组。</summary>
public sealed class ModelGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool IsAuto { get; set; }
    public bool Enabled { get; set; } = true;
    public bool AllowCrossScope { get; set; }
    public string Strategy { get; set; } = "priority-weighted";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ModelGroupMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public ModelScope Scope { get; set; }
    public Guid ProviderId { get; set; }
    public string UpstreamModel { get; set; } = string.Empty;
    public int Priority { get; set; }
    public int Weight { get; set; } = 100;
    public bool Enabled { get; set; } = true;
    public MemberState State { get; set; } = MemberState.Healthy;
}
