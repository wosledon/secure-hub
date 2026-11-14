using SecureHub.Core.Domain;
using SecureHub.Core.Models;

namespace SecureHub.Core.Tests;

public class ModelIdTests
{
    private static readonly HashSet<string> Aliases =
        new(StringComparer.Ordinal) { "openai", "azure", "deepseek", "openrouter" };

    [Theory]
    [InlineData("org/azure/gpt-4o", ModelScope.Org, "azure", "gpt-4o")]
    [InlineData("me/openai/gpt-4o", ModelScope.User, "openai", "gpt-4o")]
    [InlineData("azure/gpt-4o", ModelScope.Org, "azure", "gpt-4o")]
    [InlineData("gpt-4o", ModelScope.Org, null, "gpt-4o")]
    [InlineData("me/gpt-4o", ModelScope.User, null, "gpt-4o")]
    [InlineData("org/gpt-4o", ModelScope.Org, null, "gpt-4o")]
    public void Parses_three_level_forms(string raw, ModelScope scope, string? alias, string model)
    {
        Assert.True(ModelId.TryParse(raw, Aliases, out var id, out var error));
        Assert.Null(error);
        Assert.Equal(scope, id.Scope);
        Assert.Equal(alias, id.ProviderAlias);
        Assert.Equal(model, id.Model);
    }

    [Fact]
    public void Model_id_may_contain_slashes()
    {
        Assert.True(ModelId.TryParse("org/openrouter/openai/gpt-4o", Aliases, out var id, out _));
        Assert.Equal("openrouter", id.ProviderAlias);
        Assert.Equal("openai/gpt-4o", id.Model);
    }

    [Fact]
    public void Personal_scope_with_nested_model_id()
    {
        Assert.True(ModelId.TryParse("me/deepseek/deepseek-ai/DeepSeek-V3", Aliases, out var id, out _));
        Assert.Equal(ModelScope.User, id.Scope);
        Assert.Equal("deepseek", id.ProviderAlias);
        Assert.Equal("deepseek-ai/DeepSeek-V3", id.Model);
    }

    [Fact]
    public void Unknown_first_segment_is_treated_as_model_namespace()
    {
        // 'nvidia' 不是已知 alias → 整体作为 modelId
        Assert.True(ModelId.TryParse("nvidia/llama-3.1-70b", Aliases, out var id, out _));
        Assert.Null(id.ProviderAlias);
        Assert.Equal("nvidia/llama-3.1-70b", id.Model);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("me")]
    [InlineData("org")]
    [InlineData("me/")]
    [InlineData("/gpt-4o")]
    [InlineData("org//gpt-4o")]
    public void Rejects_invalid_forms(string raw)
    {
        Assert.False(ModelId.TryParse(raw, Aliases, out _, out var error));
        Assert.Equal(ReasonCodes.ModelNotFound, error);
    }

    [Fact]
    public void ToString_round_trips_fully_qualified_id()
    {
        var id = new ModelId(ModelScope.User, "openai", "openai/gpt-4o");
        Assert.Equal("me/openai/openai/gpt-4o", id.ToString());

        Assert.True(ModelId.TryParse(id.ToString(), Aliases, out var parsed, out _));
        Assert.Equal(id, parsed);
    }

    [Fact]
    public void Shorthand_is_not_fully_qualified()
    {
        Assert.True(ModelId.TryParse("gpt-4o", Aliases, out var id, out _));
        Assert.False(id.IsFullyQualified);
        Assert.Equal("org/gpt-4o", id.ToString());
    }
}
