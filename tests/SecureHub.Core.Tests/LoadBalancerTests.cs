using SecureHub.Core.Domain;
using SecureHub.Core.Models;

namespace SecureHub.Core.Tests;

public class LoadBalancerTests
{
    private static RouteCandidate Candidate(
        string alias,
        int priority = 100,
        int weight = 100,
        MemberState state = MemberState.Healthy,
        ModelScope scope = ModelScope.Org) =>
        new(Guid.NewGuid(), scope, Guid.NewGuid(), alias, "gpt-4o", priority, weight, state);

    [Fact]
    public void Returns_null_when_no_candidates()
    {
        Assert.Null(LoadBalancer.Select([]));
    }

    [Fact]
    public void Returns_null_when_all_disabled()
    {
        var candidates = new[]
        {
            Candidate("a", state: MemberState.Disabled),
            Candidate("b", state: MemberState.Disabled)
        };

        Assert.Null(LoadBalancer.Select(candidates));
    }

    [Fact]
    public void Single_candidate_is_returned()
    {
        var only = Candidate("a");
        Assert.Equal(only, LoadBalancer.Select([only]));
    }

    [Fact]
    public void Highest_priority_tier_wins_deterministically()
    {
        var high = Candidate("high", priority: 200);
        var low = Candidate("low", priority: 100, weight: 1000);
        var candidates = new[] { high, low };

        var rng = new Random(42);
        for (var i = 0; i < 200; i++)
        {
            Assert.Equal(high, LoadBalancer.Select(candidates, random: rng));
        }
    }

    [Fact]
    public void Weighted_distribution_is_close_to_configured_ratio()
    {
        var heavy = Candidate("heavy", weight: 80);
        var light = Candidate("light", weight: 20);
        var candidates = new[] { heavy, light };

        var rng = new Random(7);
        var heavyHits = 0;
        const int iterations = 5000;

        for (var i = 0; i < iterations; i++)
        {
            if (LoadBalancer.Select(candidates, random: rng)!.MemberId == heavy.MemberId) heavyHits++;
        }

        var ratio = heavyHits / (double)iterations;
        Assert.InRange(ratio, 0.74, 0.86);
    }

    [Fact]
    public void Degraded_member_is_penalized()
    {
        var healthy = Candidate("healthy", weight: 100);
        var degraded = Candidate("degraded", weight: 100, state: MemberState.Degraded);
        var candidates = new[] { degraded, healthy };

        var rng = new Random(11);
        var degradedHits = 0;
        const int iterations = 5000;

        for (var i = 0; i < iterations; i++)
        {
            if (LoadBalancer.Select(candidates, healthPenalty: 0.5, random: rng)!.MemberId == degraded.MemberId)
            {
                degradedHits++;
            }
        }

        // 权重 100 vs 50 → 约 33%
        Assert.InRange(degradedHits / (double)iterations, 0.27, 0.40);
    }

    [Fact]
    public void SelectNext_skips_failed_members()
    {
        var first = Candidate("first", priority: 200);
        var second = Candidate("second", priority: 100);
        var candidates = new[] { first, second };

        var failed = new HashSet<Guid> { first.MemberId };
        var selected = LoadBalancer.SelectNext(candidates, failed);

        Assert.Equal(second, selected);
    }

    [Fact]
    public void SelectNext_returns_null_when_everything_failed()
    {
        var only = Candidate("only");
        var failed = new HashSet<Guid> { only.MemberId };

        Assert.Null(LoadBalancer.SelectNext([only], failed));
    }
}
