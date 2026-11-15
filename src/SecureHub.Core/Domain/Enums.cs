namespace SecureHub.Core.Domain;

/// <summary>虚拟密钥属主类型。</summary>
public enum KeyOwnerType
{
    Organization = 0,
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

/// <summary>
/// 脱敏方式（PRD §7.4）：
/// <list type="bullet">
/// <item><c>Mask</c>：部分遮蔽（138****1234），不可逆，人可读。</item>
/// <item><c>Placeholder</c>：替换为 <c>&lt;PII.CN_MOBILE_1&gt;</c>，不可逆，人不可读。</item>
/// <item><c>Tokenize</c>：替换为 <c>⟦sh:token⟧</c>，密文存库，响应回来时还原 —— 上游"看不见"但下游"用得着"。</item>
/// </list>
/// </summary>
public enum RedactionMode
{
    Mask = 0,
    Placeholder = 1,
    Tokenize = 2
}

/// <summary>助手会话中的消息角色。</summary>
public enum AssistantMessageRole
{
    User = 0,
    Assistant = 1
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
    public const string PolicyDeleted = "policy_deleted";
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
    public const string AssistantSessionStarted = "assistant_session_started";
    public const string AssistantAnswerReturned = "assistant_answer_returned";
    public const string ContentRestored = "content_restored";
    public const string SecurityBaselineFailed = "security_baseline_failed";
    public const string UserCreated = "user_created";
    public const string UserUpdated = "user_updated";
    public const string UserDeactivated = "user_deactivated";
    public const string UserPasswordReset = "user_password_reset";
    public const string UserPasswordChanged = "user_password_changed";
    public const string TeamCreated = "team_created";
    public const string TeamUpdated = "team_updated";
    public const string AppCreated = "app_created";
    public const string AppUpdated = "app_updated";
    public const string ProviderUpdated = "provider_updated";
    public const string ProviderDisabled = "provider_disabled";
    public const string ProviderTested = "provider_tested";
    public const string ProviderCredentialRotated = "provider_credential_rotated";
    public const string EntitlementUpdated = "entitlement_updated";
    public const string EntitlementRevoked = "entitlement_revoked";
    public const string KeyConstraintsUpdated = "key_constraints_updated";
    public const string RateLimited = "rate_limited";
    public const string SystemSettingsUpdated = "system_settings_updated";
    public const string ContentPurged = "content_purged";
    public const string CustomRuleCreated = "custom_rule_created";
    public const string CustomRuleUpdated = "custom_rule_updated";
    public const string CustomRuleDeleted = "custom_rule_deleted";
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
    public const string ContentNotFound = "content_not_found";
    public const string ContentExpired = "content_expired";
    public const string ContentReadDisabled = "content_read_disabled";
    public const string SessionNotFound = "session_not_found";
}
