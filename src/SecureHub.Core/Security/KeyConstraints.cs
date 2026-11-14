using System.Net;
using System.Text.Json;

namespace SecureHub.Core.Security;

/// <summary>虚拟密钥的访问约束（ConstraintsJson）。空集合表示不限制。</summary>
public sealed record KeyConstraints(
    IReadOnlyList<string> AllowedModels,
    IReadOnlyList<string> AllowedProviders,
    IReadOnlyList<string> AllowedIpCidrs,
    int? RequestsPerMinute,
    int? TokensPerMinute,
    int? MaxConcurrency)
{
    public static readonly KeyConstraints None = new([], [], [], null, null, null);

    public static KeyConstraints Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return None;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return None;
            var root = document.RootElement;

            return new KeyConstraints(
                ReadStrings(root, "allowedModels"),
                ReadStrings(root, "allowedProviders"),
                ReadStrings(root, "allowedIpCidrs"),
                ReadPositiveInt(root, "rpm"),
                ReadPositiveInt(root, "tpm"),
                ReadPositiveInt(root, "maxConcurrency"));
        }
        catch (JsonException)
        {
            return None;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(new
    {
        allowedModels = AllowedModels,
        allowedProviders = AllowedProviders,
        allowedIpCidrs = AllowedIpCidrs,
        rpm = RequestsPerMinute,
        tpm = TokensPerMinute,
        maxConcurrency = MaxConcurrency
    });

    public bool AllowsProvider(string providerAlias) =>
        AllowedProviders.Count == 0 ||
        AllowedProviders.Any(value => GlobMatch(value, providerAlias));

    public bool AllowsModel(string upstreamModel, string? providerAlias) =>
        (AllowedProviders.Count == 0 || providerAlias is null || AllowsProvider(providerAlias)) &&
        (AllowedModels.Count == 0 ||
         AllowedModels.Any(value =>
             GlobMatch(value, upstreamModel) ||
             (providerAlias is not null && GlobMatch(value, $"{providerAlias}/{upstreamModel}"))));

    public bool AllowsIp(string? remoteIp) =>
        AllowedIpCidrs.Count == 0 || (remoteIp is not null && AllowedIpCidrs.Any(cidr => CidrMatch(cidr, remoteIp)));

    private static IReadOnlyList<string> ReadStrings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .ToArray();
    }

    private static int? ReadPositiveInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt32(out var number) && number > 0 ? number : null;
    }

    private static bool GlobMatch(string pattern, string value)
    {
        if (pattern == "*") return true;
        if (pattern.EndsWith('*'))
        {
            return value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CidrMatch(string cidr, string remoteIp)
    {
        if (!IPAddress.TryParse(remoteIp, out var address)) return false;

        var parts = cidr.Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out var network)) return false;

        var prefix = network.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out prefix) || prefix < 0 || prefix > (network.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128)))
        {
            return false;
        }

        if (address.AddressFamily != network.AddressFamily) return false;

        var addressBytes = address.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();
        var fullBytes = prefix / 8;
        var remainder = prefix % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (addressBytes[i] != networkBytes[i]) return false;
        }

        if (remainder == 0) return true;

        var mask = (byte)(0xFF << (8 - remainder));
        return (addressBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
    }
}
