using SecureHub.Core.Domain;
using SecureHub.Core.Localization;
using SecureHub.Core.Security;

namespace SecureHub.Core.Tests;

public class VirtualKeyFormatTests
{
    [Fact]
    public void Generated_key_round_trips()
    {
        var (full, parsed) = VirtualKeyFormat.Generate("live");

        Assert.StartsWith("sk-sh-live-", full);
        Assert.True(VirtualKeyFormat.TryParse(full, out var reparsed));
        Assert.Equal(parsed.KeyId, reparsed.KeyId);
        Assert.Equal(parsed.Secret, reparsed.Secret);
        Assert.Equal("live", reparsed.Environment);
    }

    [Fact]
    public void Key_id_is_eight_hex_chars()
    {
        var (_, parsed) = VirtualKeyFormat.Generate();
        Assert.Equal(8, parsed.KeyId.Length);
        Assert.All(parsed.KeyId, c => Assert.True(Uri.IsHexDigit(c)));
    }

    [Fact]
    public void Two_generated_keys_differ()
    {
        var (a, _) = VirtualKeyFormat.Generate();
        var (b, _) = VirtualKeyFormat.Generate();
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("")]
    [InlineData("gpt-4o")]
    [InlineData("sk-live-abc-def")]
    [InlineData("sk-sh-live-zzzzzzzz-secret")]
    [InlineData("sk-sh-live-a1b2c3d4-")]
    public void Rejects_malformed_keys(string raw)
    {
        Assert.False(VirtualKeyFormat.TryParse(raw, out _));
    }

    [Fact]
    public void Hash_is_stable_and_not_plaintext()
    {
        var hash1 = VirtualKeyFormat.HashSecret("s3cr3t-value-1234567890");
        var hash2 = VirtualKeyFormat.HashSecret("s3cr3t-value-1234567890");

        Assert.Equal(hash1, hash2);
        Assert.Equal(32, hash1.Length);
    }
}

public class RbacTests
{
    [Theory]
    [InlineData(UserRole.PlatformAdmin, Permission.SecurityWrite, true)]
    [InlineData(UserRole.SecurityAdmin, Permission.SecurityWrite, true)]
    [InlineData(UserRole.TeamOwner, Permission.SecurityWrite, false)]
    [InlineData(UserRole.Developer, Permission.AuditRead, false)]
    [InlineData(UserRole.Auditor, Permission.AuditRead, true)]
    [InlineData(UserRole.Auditor, Permission.SecurityWrite, false)]
    [InlineData(UserRole.EndUser, Permission.AssistantUse, true)]
    [InlineData(UserRole.EndUser, Permission.ProviderWrite, false)]
    [InlineData(UserRole.SecurityAdmin, Permission.ContentRead, true)]
    [InlineData(UserRole.Auditor, Permission.ContentRead, false)]
    [InlineData(UserRole.TeamOwner, Permission.ContentRead, false)]
    public void Matrix_matches_spec(UserRole role, Permission permission, bool expected)
    {
        Assert.Equal(expected, Rbac.Allows(role, permission));
    }

    [Fact]
    public void Personal_scope_detects_end_user_only()
    {
        Assert.True(Rbac.IsPersonalScope([UserRole.EndUser]));
        Assert.False(Rbac.IsPersonalScope([UserRole.EndUser, UserRole.Auditor]));
        Assert.False(Rbac.IsPersonalScope([]));
    }
}

public class LocalizationTests
{
    [Theory]
    [InlineData("zh-CN,zh;q=0.9,en;q=0.8", "zh-CN")]
    [InlineData("en-US,en;q=0.9", "en")]
    [InlineData("fr-FR", "zh-CN")]
    [InlineData(null, "zh-CN")]
    public void Negotiates_accept_language(string? header, string expected)
    {
        Assert.Equal(expected, DictionaryLocalizer.Negotiate(header));
    }

    [Fact]
    public void Missing_key_falls_back_to_key_not_exception()
    {
        var localizer = new DictionaryLocalizer("en");
        Assert.Equal("no.such.key", localizer.Get("no.such.key"));
    }

    [Fact]
    public void Same_key_differs_between_cultures()
    {
        var zh = new DictionaryLocalizer("zh-CN").Get("error.model_not_found");
        var en = new DictionaryLocalizer("en").Get("error.model_not_found");
        Assert.NotEqual(zh, en);
    }
}
