namespace SecureHub.Core.Localization;

public interface ILocalizer
{
    string Culture { get; }
    string Get(string key);
    string Format(string key, params object?[] args);
}

/// <summary>
/// 轻量本地化：只本地化面向人的文案；错误 code、审计 eventType 永不翻译（PRD §12.1）。
/// 生产环境可替换为 resx + IStringLocalizer，接口保持一致。
/// </summary>
public sealed class DictionaryLocalizer : ILocalizer
{
    public const string ZhCn = "zh-CN";
    public const string En = "en";

    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = new(StringComparer.OrdinalIgnoreCase)
    {
        [ZhCn] = new(StringComparer.Ordinal)
        {
            ["error.invalid_api_key"] = "密钥无效、已过期或已吊销",
            ["error.insufficient_scope"] = "该密钥缺少所需的访问范围",
            ["error.request_blocked"] = "请求被安全策略拦截",
            ["error.context_length_exceeded"] = "请求超出模型的上下文长度上限",
            ["error.upstream_timeout"] = "上游供应商超时",
            ["error.upstream_rate_limited"] = "上游供应商限流",
            ["error.capability_unsupported"] = "模型不支持请求所需的能力",
            ["error.key_owner_mismatch"] = "密钥属主与调用方不一致",
            ["error.byok_disabled"] = "个人供应商接入已被禁用",
            ["error.byok_not_configured"] = "尚未配置个人供应商，无法使用 me/ 命名空间",
            ["error.model_not_entitled"] = "当前账号未被授权使用该模型",
            ["error.model_not_found"] = "模型不存在或不可用",
            ["error.model_ambiguous"] = "模型名有歧义，请使用完整的三级 ID",
            ["error.provider_not_allowed"] = "该供应商不在允许范围内",
            ["error.quota_exceeded"] = "额度已用尽",
            ["error.all_candidates_failed"] = "所有候选供应商均调用失败",
            ["error.invalid_model_id"] = "模型 ID 格式不正确",
            ["validation.required"] = "该字段为必填项",
            ["validation.invalid"] = "字段格式不正确",
            ["validation.catalog_provider_not_found"] = "模型目录中找不到该供应商，请先同步 models.dev 目录",
            ["validation.catalog_model_not_found"] = "所选模型不在该供应商的目录中",
            ["validation.provider_endpoint_required"] = "目录未提供接口地址，请填写有效的 http(s) 端点",
            ["validation.password_too_weak"] = "密码至少需要 12 个字符",
            ["validation.email"] = "邮箱地址格式不正确",
            ["validation.password_same"] = "新密码不能与当前密码相同",
            ["validation.duplicate"] = "名称或用户名已存在",
            ["validation.last_admin"] = "必须至少保留一名启用的平台管理员",
            ["validation.self_lockout"] = "不能停用当前登录账号或移除其平台管理员角色",
            ["validation.policy_key"] = "策略标识只能包含小写字母、数字和连字符，长度 2–64",
            ["validation.policy_condition_empty"] = "请至少选择一个检测器或限定作用范围，否则策略会命中全部请求",
            ["validation.detector_unknown"] = "检测器不存在，请从列表中选择",
            ["validation.policy_action"] = "处置动作只能是阻断、脱敏或告警",
            ["validation.policy_priority"] = "优先级必须在 0–1000 之间",
            ["auth.login_failed"] = "用户名或密码错误",
            ["auth.registration_disabled"] = "管理员未开放自助注册，请联系平台管理员",
            ["auth.current_password_invalid"] = "当前密码不正确",
            ["auth.locked"] = "登录失败次数过多，请稍后再试",
            ["auth.session_expired"] = "会话已过期，请重新登录",
            ["common.confirm"] = "确认",
            ["common.cancel"] = "取消"
        },
        [En] = new(StringComparer.Ordinal)
        {
            ["error.invalid_api_key"] = "The API key is invalid, expired or revoked",
            ["error.insufficient_scope"] = "The key lacks the required scope",
            ["error.request_blocked"] = "The request was blocked by a security policy",
            ["error.context_length_exceeded"] = "The request exceeds the model context window",
            ["error.upstream_timeout"] = "The upstream provider timed out",
            ["error.upstream_rate_limited"] = "The upstream provider rate limited the request",
            ["error.capability_unsupported"] = "The model does not support a requested capability",
            ["error.key_owner_mismatch"] = "The key owner does not match the caller",
            ["error.byok_disabled"] = "Bring-your-own-key is disabled",
            ["error.byok_not_configured"] = "No personal provider is configured for the me/ namespace",
            ["error.model_not_entitled"] = "This account is not entitled to the model",
            ["error.model_not_found"] = "The model does not exist or is unavailable",
            ["error.model_ambiguous"] = "The model name is ambiguous; use the fully qualified three-level id",
            ["error.provider_not_allowed"] = "The provider is not allowed",
            ["error.quota_exceeded"] = "Quota exhausted",
            ["error.all_candidates_failed"] = "All candidate providers failed",
            ["error.invalid_model_id"] = "Malformed model id",
            ["validation.required"] = "This field is required",
            ["validation.invalid"] = "This field is invalid",
            ["validation.catalog_provider_not_found"] = "Provider is not present in the model catalog; sync models.dev first",
            ["validation.catalog_model_not_found"] = "One or more models are not in the provider's catalog",
            ["validation.provider_endpoint_required"] = "The catalog has no endpoint for this provider; enter a valid http(s) endpoint",
            ["validation.password_too_weak"] = "Password must be at least 12 characters",
            ["validation.email"] = "Invalid email address",
            ["validation.password_same"] = "The new password must differ from the current one",
            ["validation.duplicate"] = "Name or user name already exists",
            ["validation.last_admin"] = "At least one active platform administrator is required",
            ["validation.self_lockout"] = "You cannot deactivate your own account or remove your platform admin role",
            ["validation.policy_key"] = "Policy key must use 2–64 lowercase letters, digits, or hyphens",
            ["validation.policy_condition_empty"] = "Select at least one detector or narrow the scope; otherwise the policy matches every request",
            ["validation.detector_unknown"] = "Unknown detector; choose one from the list",
            ["validation.policy_action"] = "Action must be block, redact, or alert",
            ["validation.policy_priority"] = "Priority must be between 0 and 1000",
            ["auth.login_failed"] = "Invalid user name or password",
            ["auth.registration_disabled"] = "Self-registration is disabled; contact your platform administrator",
            ["auth.current_password_invalid"] = "The current password is incorrect",
            ["auth.locked"] = "Too many failed attempts, try again later",
            ["auth.session_expired"] = "Session expired, please sign in again",
            ["common.confirm"] = "Confirm",
            ["common.cancel"] = "Cancel"
        }
    };

    public DictionaryLocalizer(string culture)
    {
        Culture = Catalogs.ContainsKey(culture) ? Normalize(culture) : ZhCn;
    }

    public string Culture { get; }

    public string Get(string key)
    {
        if (Catalogs[Culture].TryGetValue(key, out var value)) return value;
        // 回落英文，再回落 key 本身，绝不抛异常
        if (Catalogs[En].TryGetValue(key, out var fallback)) return fallback;
        return key;
    }

    public string Format(string key, params object?[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public static string Normalize(string culture) =>
        culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? ZhCn : En;

    /// <summary>按 Accept-Language 协商，支持 q 值之外的简单前缀匹配。</summary>
    public static string Negotiate(string? acceptLanguage, string defaultCulture = ZhCn)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage)) return defaultCulture;

        foreach (var part in acceptLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var tag = part.Split(';')[0].Trim();
            if (tag.Length == 0) continue;

            if (tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return ZhCn;
            if (tag.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return En;
        }

        return defaultCulture;
    }
}
