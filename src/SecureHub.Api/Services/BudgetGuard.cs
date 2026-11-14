using Microsoft.EntityFrameworkCore;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Data;

namespace SecureHub.Api.Services;

public sealed record BudgetCheck(
    bool Allowed,
    string? ExceededPeriod,
    long? TokenLimit,
    long TokensUsed,
    decimal? CostLimit,
    decimal CostUsed);

/// <summary>预算检查与累计（PRD §8.2）。周期到期自动归零。</summary>
public sealed class BudgetGuard(SecureHubDbContext db, IClock clock)
{
    public async Task<BudgetCheck> CheckAsync(
        Guid? teamId,
        CancellationToken ct = default)
    {
        var budgets = await ApplicableAsync(teamId, ct);
        if (budgets.Count == 0) return new BudgetCheck(true, null, null, 0, null, 0);

        long tokensUsed = 0;
        decimal costUsed = 0;
        long? tokenLimit = null;
        decimal? costLimit = null;

        foreach (var budget in budgets)
        {
            tokensUsed += budget.TokensUsed;
            costUsed += budget.CostUsed;

            if (budget.TokenLimit is { } tl)
            {
                tokenLimit = tokenLimit is null ? tl : Math.Min(tokenLimit.Value, tl);
            }

            if (budget.CostLimit is { } cl)
            {
                costLimit = costLimit is null ? cl : Math.Min(costLimit.Value, cl);
            }

            if (budget.TokenLimit is { } tokenCap && budget.TokensUsed >= tokenCap)
            {
                return new BudgetCheck(false, budget.Period, tokenCap, budget.TokensUsed, budget.CostLimit, budget.CostUsed);
            }

            if (budget.CostLimit is { } costCap && budget.CostUsed >= costCap)
            {
                return new BudgetCheck(false, budget.Period, budget.TokenLimit, budget.TokensUsed, costCap, budget.CostUsed);
            }
        }

        return new BudgetCheck(true, null, tokenLimit, tokensUsed, costLimit, costUsed);
    }

    public async Task AccumulateAsync(
        Guid? teamId,
        long tokens,
        decimal cost,
        CancellationToken ct = default)
    {
        if (tokens <= 0 && cost <= 0) return;

        var budgets = await ApplicableAsync(teamId, ct);
        if (budgets.Count == 0) return;

        foreach (var budget in budgets)
        {
            budget.TokensUsed += Math.Max(0, tokens);
            budget.CostUsed += Math.Max(0, cost);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<List<Budget>> ApplicableAsync(Guid? teamId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var all = await db.Budgets.Where(b => b.Enabled).ToListAsync(ct);

        var matched = all
            .Where(b => b.Subject switch
            {
                BudgetSubject.All => true,
                BudgetSubject.Team => teamId is not null && b.SubjectId == teamId,
                _ => false
            })
            .ToList();

        var changed = false;
        foreach (var budget in matched)
        {
            if (RollOver(budget, now)) changed = true;
        }

        if (changed) await db.SaveChangesAsync(ct);
        return matched;
    }

    /// <summary>周期切换时归零；PeriodStart 置为当前周期起点。</summary>
    private static bool RollOver(Budget budget, DateTimeOffset now)
    {
        var start = budget.Period.ToLowerInvariant() switch
        {
            "day" => new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero),
            "week" => StartOfWeek(now),
            _ => new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero)
        };

        if (start <= budget.PeriodStart) return false;

        budget.PeriodStart = start;
        budget.TokensUsed = 0;
        budget.CostUsed = 0;
        return true;
    }

    private static DateTimeOffset StartOfWeek(DateTimeOffset now)
    {
        var date = now.UtcDateTime.Date;
        var delta = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return new DateTimeOffset(date.AddDays(-delta), TimeSpan.Zero);
    }
}
