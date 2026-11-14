namespace SecureHub.Core.Domain;

public sealed class Team
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class App
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid? TeamId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string? TotpSecret { get; set; }
    public Guid? TeamId { get; set; }
    public string RolesJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public IReadOnlyList<UserRole> Roles => JsonRoleParser.Parse(RolesJson);
}

public static class JsonRoleParser
{
    public static IReadOnlyList<UserRole> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return [];
            var list = new List<UserRole>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.String &&
                    Enum.TryParse<UserRole>(item.GetString(), ignoreCase: true, out var role))
                {
                    list.Add(role);
                }
                else if (item.ValueKind == System.Text.Json.JsonValueKind.Number &&
                         item.TryGetInt32(out var value) &&
                         Enum.IsDefined(typeof(UserRole), value))
                {
                    list.Add((UserRole)value);
                }
            }
            return list;
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}

public sealed class VirtualKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string KeyId { get; set; } = string.Empty;

    /// <summary>SHA-256(secret)，32 字节。</summary>
    public byte[] SecretHash { get; set; } = [];

    /// <summary>前 12 字符，用于列表展示。</summary>
    public string Prefix { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public KeyOwnerType OwnerType { get; set; }
    public Guid OwnerId { get; set; }
    public string ScopesJson { get; set; } = "[]";

    /// <summary>JSON：allowedModels / allowedProviders / allowedIpCidrs / rpm / tpm / maxConcurrency。</summary>
    public string? ConstraintsJson { get; set; }

    public bool IsRevoked { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid? RotatedFromId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive(DateTimeOffset now) =>
        !IsRevoked && (ExpiresAt is null || ExpiresAt > now);
}
