using System.Diagnostics.CodeAnalysis;
using SecureHub.Core.Domain;

namespace SecureHub.Core.Models;

/// <summary>
/// 三级模型 ID：<c>[scope/]provider/modelId</c>。
/// <para>
/// 解析规则（PRD §10.3）：从左消费已知前缀，剩余全部视为 modelId，
/// 因此 modelId 本身允许包含 '/'（如 <c>openai/gpt-4o</c>）。
/// </para>
/// </summary>
public readonly record struct ModelId
{
    public const string UserScopePrefix = "me";
    public const string OrgScopePrefix = "org";

    public static readonly string[] ReservedProviderAliases =
        ["org", "me", "system", "admin", "internal"];

    public ModelId(ModelScope scope, string? providerAlias, string model)
    {
        Scope = scope;
        ProviderAlias = providerAlias;
        Model = model;
    }

    public ModelScope Scope { get; init; }

    /// <summary>为 null 时表示"未指定供应商"，需唯一命中或交给负载均衡。</summary>
    public string? ProviderAlias { get; init; }

    public string Model { get; init; }

    public bool IsFullyQualified => ProviderAlias is not null;

    public static bool TryParse(
        string? raw,
        IReadOnlySet<string> knownProviderAliases,
        out ModelId id,
        out string? errorCode)
    {
        id = default;
        errorCode = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            errorCode = ReasonCodes.ModelNotFound;
            return false;
        }

        var segments = raw.Trim().Split('/');
        if (segments.Any(string.IsNullOrWhiteSpace))
        {
            errorCode = ReasonCodes.ModelNotFound;
            return false;
        }

        var scope = ModelScope.Org;
        var index = 0;

        if (string.Equals(segments[0], UserScopePrefix, StringComparison.OrdinalIgnoreCase))
        {
            scope = ModelScope.User;
            index = 1;
        }
        else if (string.Equals(segments[0], OrgScopePrefix, StringComparison.OrdinalIgnoreCase))
        {
            scope = ModelScope.Org;
            index = 1;
        }

        if (index >= segments.Length)
        {
            // 只给了 "me" / "org"
            errorCode = ReasonCodes.ModelNotFound;
            return false;
        }

        string? providerAlias = null;
        if (knownProviderAliases.Contains(segments[index]))
        {
            providerAlias = segments[index];
            index++;
        }

        if (index >= segments.Length)
        {
            errorCode = ReasonCodes.ModelNotFound;
            return false;
        }

        var model = string.Join('/', segments[index..]);
        id = new ModelId(scope, providerAlias, model);
        return true;
    }

    public static ModelId Parse(string raw, IReadOnlySet<string> knownProviderAliases)
    {
        if (!TryParse(raw, knownProviderAliases, out var id, out _))
        {
            throw new FormatException($"Invalid model id: {raw}");
        }

        return id;
    }

    /// <summary>全限定形式；provider 为空时退化为 <c>scope/model</c>。</summary>
    public override string ToString() =>
        ProviderAlias is null
            ? $"{ScopePrefix(Scope)}/{Model}"
            : $"{ScopePrefix(Scope)}/{ProviderAlias}/{Model}";

    public ModelId WithProvider(string alias) => this with { ProviderAlias = alias };

    public static string ScopePrefix(ModelScope scope) =>
        scope == ModelScope.User ? UserScopePrefix : OrgScopePrefix;
}
