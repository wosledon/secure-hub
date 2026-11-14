using SecureHub.Core.Domain;
using SecureHub.Core.Security.Policies;

namespace SecureHub.Core.Tests;

public class PolicyEvaluatorTests
{
    private static CompiledPolicy Policy(
        string key = "p1",
        int priority = 100,
        GuardrailAction action = GuardrailAction.Block,
        string[]? detectors = null,
        string[]? apps = null,
        int rollout = 100,
        string[]? allDetectors = null) =>
        new(
            key,
            Version: 1,
            priority,
            Enabled: true,
            new PolicyScope { Apps = apps ?? [], Teams = [], Models = [] },
            new PolicyCondition
            {
                Any = (detectors ?? []).Select(d => new DetectorRequirement { Detector = d, MinMatches = 1 }).ToArray(),
                All = (allDetectors ?? []).Select(d => new DetectorRequirement { Detector = d, MinMatches = 1 }).ToArray()
            },
            action,
            Reason: "test",
            RuleId: detectors?.FirstOrDefault(),
            RolloutPercent: rollout,
            OnError: "block");

    private static PolicyEvalContext Context(
        string[] detectors,
        Guid? appId = null,
        int bucket = 0,
        string? model = "gpt-4o",
        string? appName = null,
        IReadOnlyDictionary<string, int>? counts = null) =>
        new(appId, appName, null, null, model, detectors, bucket, new TimeOnly(10, 0), counts);

    [Theory]
    [InlineData("app-*", "app-1", true)]
    [InlineData("*", "anything", true)]
    [InlineData("*-prod", "svc-prod", true)]
    [InlineData("app-*", "svc-1", false)]
    [InlineData("gpt-4o", "gpt-4o", true)]
    [InlineData("gpt-4o", "gpt-4o-mini", false)]
    public void Wildcard_matching(string pattern, string value, bool expected)
    {
        Assert.Equal(expected, PolicyEvaluator.WildcardMatch(pattern, value));
    }

    [Fact]
    public void No_policies_means_allow()
    {
        var decision = PolicyEvaluator.Evaluate([], Context([]));
        Assert.Equal(GuardrailAction.Allow, decision.Action);
        Assert.Empty(decision.Hits);
    }

    [Fact]
    public void Matches_detector_and_blocks()
    {
        var decision = PolicyEvaluator.Evaluate(
            [Policy(detectors: ["pii.cn_idcard"])],
            Context(["pii.cn_idcard"]));

        Assert.Equal(GuardrailAction.Block, decision.Action);
        Assert.Single(decision.Hits);
    }

    [Fact]
    public void Detector_not_present_does_not_match()
    {
        var decision = PolicyEvaluator.Evaluate(
            [Policy(detectors: ["pii.cn_idcard"])],
            Context(["pii.cn_mobile"]));

        Assert.Equal(GuardrailAction.Allow, decision.Action);
    }

    [Fact]
    public void All_condition_requires_every_detector()
    {
        var policy = Policy(allDetectors: ["pii.cn_idcard", "secret.generic"]);

        Assert.Equal(GuardrailAction.Allow,
            PolicyEvaluator.Evaluate([policy], Context(["pii.cn_idcard"])).Action);

        Assert.Equal(GuardrailAction.Block,
            PolicyEvaluator.Evaluate([policy], Context(["pii.cn_idcard", "secret.generic"])).Action);
    }

    [Fact]
    public void App_scope_matches_by_name()
    {
        var policy = Policy(apps: ["demo-app"]);

        Assert.Equal(GuardrailAction.Allow,
            PolicyEvaluator.Evaluate([policy], Context(["pii.cn_idcard"], appName: "other-app")).Action);

        Assert.Equal(GuardrailAction.Block,
            PolicyEvaluator.Evaluate([policy], Context(["pii.cn_idcard"], appName: "demo-app")).Action);
    }

    [Fact]
    public void App_scope_falls_back_to_id_when_name_missing()
    {
        var appId = Guid.NewGuid();
        var policy = Policy(apps: [$"{appId}"]);

        Assert.Equal(GuardrailAction.Allow,
            PolicyEvaluator.Evaluate([policy], Context(["pii.cn_idcard"], appId: Guid.NewGuid())).Action);

        Assert.Equal(GuardrailAction.Block,
            PolicyEvaluator.Evaluate([policy], Context(["pii.cn_idcard"], appId: appId)).Action);
    }

    [Fact]
    public void Rollout_skips_buckets_beyond_percent()
    {
        var policy = Policy(rollout: 10);

        Assert.Equal(GuardrailAction.Block,
            PolicyEvaluator.Evaluate([policy], Context(["pii.cn_idcard"], bucket: 9)).Action);

        Assert.Equal(GuardrailAction.Allow,
            PolicyEvaluator.Evaluate([policy], Context(["pii.cn_idcard"], bucket: 10)).Action);
    }

    [Fact]
    public void Strictest_action_wins()
    {
        var alert = Policy("alert", priority: 200, action: GuardrailAction.Alert, detectors: ["pii.email"]);
        var block = Policy("block", priority: 100, action: GuardrailAction.Block, detectors: ["pii.email"]);

        var decision = PolicyEvaluator.Evaluate([alert, block], Context(["pii.email"]));

        Assert.Equal(GuardrailAction.Block, decision.Action);
        Assert.Equal(2, decision.Hits.Count);
    }

    [Fact]
    public void Higher_priority_is_evaluated_first()
    {
        var high = Policy("high", priority: 200, action: GuardrailAction.Alert, detectors: ["pii.email"]);
        var low = Policy("low", priority: 10, action: GuardrailAction.Redact, detectors: ["pii.email"]);

        var decision = PolicyEvaluator.Evaluate([low, high], Context(["pii.email"]));
        Assert.Equal("high", decision.Hits[0].PolicyKey);
    }

    [Fact]
    public void Match_threshold_requires_enough_occurrences()
    {
        var policy = new CompiledPolicy(
            "threshold", Version: 1, Priority: 100, Enabled: true,
            new PolicyScope(),
            new PolicyCondition
            {
                Any = [new DetectorRequirement { Detector = "pii.email", MinMatches = 2 }]
            },
            GuardrailAction.Alert, Reason: "test", RuleId: "pii.email",
            RolloutPercent: 100, OnError: "block");

        Assert.Equal(GuardrailAction.Allow, PolicyEvaluator.Evaluate(
            [policy],
            Context(["pii.email"], counts: new Dictionary<string, int> { ["pii.email"] = 1 })).Action);

        Assert.Equal(GuardrailAction.Alert, PolicyEvaluator.Evaluate(
            [policy],
            Context(["pii.email"], counts: new Dictionary<string, int> { ["pii.email"] = 2 })).Action);
    }
}
