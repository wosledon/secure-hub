namespace SecureHub.Core.Domain;

/// <summary>虚拟密钥属主类型。</summary>
public enum KeyOwnerType
{
    App = 0,
    User = 1
}

/// <summary>计费归属。</summary>
public enum BillingOwner
{
    Org = 0,
    User = 1
}

/// <summary>供应商可见范围。</summary>
public enum ProviderScope
{
    Org = 0,
    User = 1
}

/// <summary>企业授权对象。</summary>
public enum SubjectType
{
    All = 0,
    Team = 1,
    User = 2
}

public enum GuardrailDirection
{
    Inbound = 0,
    Outbound = 1
}

/// <summary>动作合成优先级：Block &gt; Redact &gt; Alert &gt; Allow。</summary>
public enum GuardrailAction
{
    Allow = 0,
    Alert = 1,
    Redact = 2,
    Block = 3
}

public enum GuardrailSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public enum AuditSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public enum ContentDirection
{
    Request = 0,
    Response = 1
}

/// <summary>内容变体：原文 / 脱敏后 / 实际外发。</summary>
public enum ContentVariant
{
    Raw = 0,
    Redacted = 1,
    Compressed = 2
}

public enum MemberState
{
    Healthy = 0,
    Degraded = 1,
    Disabled = 2
}

/// <summary>模型命名空间。</summary>
public enum ModelScope
{
    Org = 0,
    User = 1
}

/// <summary>RBAC 角色。</summary>
public enum UserRole
{
    PlatformAdmin = 0,
    SecurityAdmin = 1,
    TeamOwner = 2,
    Developer = 3,
    Auditor = 4,
    EndUser = 5
}

/// <summary>审计事件类型（机器可读，永不本地化）。</summary>
public static class AuditEventTypes
{
    public const string AuthSucceeded = "auth_succeeded";
    public const string AuthFailed = "auth_failed";
    public const string KeyCreated = "key_created";
    public const string KeyRotated = "key_rotated";
    public const string KeyRevoked = "key_revoked";
    public const string AccessDenied = "access_denied";
    public const string GuardrailBlocked = "guardrail_blocked";
    public const string GuardrailRedacted = "guardrail_redacted";
    public const string GuardrailAlert = "guardrail_alert";
    public const string GuardrailError = "guardrail_error";
    public const string PolicyCreated = "policy_created";
    public const string PolicyUpdated = "policy_updated";
    public const string PolicyActivated = "policy_activated";
    public const string RouteFailover = "route_failover";
    public const string RouteSelected = "route_selected";
    public const string RouteDenied = "route_denied";
    public const string QuotaExceeded = "quota_exceeded";
    public const string CompressionApplied = "compression_applied";
    public const string CompressionRolledBack = "compression_rolled_back";
    public const string ContentViewed = "content_viewed";
    public const string ContentWriteFailed = "content_write_failed";
    public const string DataExported = "data_exported";
    public const string ProviderCreated = "provider_created";
    public const string ProviderCredentialUpdated = "provider_credential_updated";
    public const string CatalogSynced = "catalog_synced";
    public const string ByokEnabled = "byok_enabled";
    public const string AssistantToolInvoked = "assistant_tool_invoked";
    public const string SecurityBaselineFailed = "security_baseline_failed";
}

/// <summary>审计/错误原因码（机器可读，永不本地化）。</summary>
public static class ReasonCodes
{
    public const string InvalidApiKey = "invalid_api_key";
    public const string InsufficientScope = "insufficient_scope";
    public const string RequestBlocked = "request_blocked";
    public const string ContextLengthExceeded = "context_length_exceeded";
    public const string UpstreamTimeout = "upstream_timeout";
    public const string UpstreamRateLimited = "upstream_rate_limited";
    public const string CapabilityUnsupported = "capability_unsupported";
    public const string PiiDetected = "pii_detected";
    public const string SecretDetected = "secret_detected";
    public const string PromptInjection = "prompt_injection";
    public const string QuotaExceeded = "quota_exceeded";
    public const string BudgetExhausted = "budget_exhausted";
    public const string ModelNotEntitled = "model_not_entitled";
    public const string ModelNotFound = "model_not_found";
    public const string ModelAmbiguous = "model_ambiguous";
    public const string KeyOwnerMismatch = "key_owner_mismatch";
    public const string ByokDisabled = "byok_disabled";
    public const string ByokNotConfigured = "byok_not_configured";
    public const string ProviderNotAllowed = "provider_not_allowed";
    public const string DetectorTimeout = "detector_timeout";
    public const string CompressionRolledBack = "compression_rolled_back";
    public const string AllCandidatesFailed = "all_candidates_failed";
}
