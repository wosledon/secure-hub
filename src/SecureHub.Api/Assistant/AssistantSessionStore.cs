using Microsoft.EntityFrameworkCore;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Data;

namespace SecureHub.Api.Assistant;

/// <summary>
/// 助手会话持久化（PRD §11.3）。会话只属于创建者：所有读写都强制带 <c>UserId</c> 过滤，
/// 不依赖前端传参，也不给管理员开后门读他人对话。
/// </summary>
public sealed class AssistantSessionStore(SecureHubDbContext db, IClock clock)
{
    public async Task<AssistantSession> CreateAsync(
        Guid userId,
        string? title,
        string? modelAlias,
        CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var session = new AssistantSession
        {
            UserId = userId,
            Title = NormalizeTitle(title),
            ModelAlias = modelAlias,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.AssistantSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public Task<AssistantSession?> GetAsync(Guid sessionId, Guid userId, CancellationToken ct = default) =>
        db.AssistantSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);

    public Task<List<AssistantSession>> ListAsync(Guid userId, int limit, CancellationToken ct = default) =>
        db.AssistantSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .Take(Math.Clamp(limit <= 0 ? 30 : limit, 1, 100))
            .ToListAsync(ct);

    public Task<List<AssistantMessage>> LoadMessagesAsync(
        Guid sessionId, Guid userId, CancellationToken ct = default) =>
        db.AssistantMessages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId &&
                        db.AssistantSessions.Any(s => s.Id == sessionId && s.UserId == userId))
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

    /// <summary>取最近 N 条消息用于模型上下文回放（时间正序返回）。</summary>
    public async Task<List<(AssistantMessageRole Role, string Content)>> LoadHistoryAsync(
        Guid sessionId,
        Guid userId,
        int limit,
        CancellationToken ct = default)
    {
        var rows = await db.AssistantMessages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId &&
                        db.AssistantSessions.Any(s => s.Id == sessionId && s.UserId == userId))
            .OrderByDescending(m => m.CreatedAt)
            .Take(Math.Clamp(limit <= 0 ? 12 : limit, 1, 40))
            .ToListAsync(ct);

        rows.Reverse();
        return rows.Select(m => (m.Role, m.Content)).ToList();
    }

    public async Task<bool> AppendTurnAsync(
        Guid sessionId,
        Guid userId,
        string question,
        AssistantAnswer answer,
        CancellationToken ct = default)
    {
        var session = await db.AssistantSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);

        if (session is null) return false;

        var now = clock.UtcNow;

        db.AssistantMessages.Add(new AssistantMessage
        {
            SessionId = sessionId,
            Role = AssistantMessageRole.User,
            Content = question,
            CreatedAt = now
        });

        db.AssistantMessages.Add(new AssistantMessage
        {
            SessionId = sessionId,
            Role = AssistantMessageRole.Assistant,
            Content = answer.Answer,
            ToolNames = answer.ToolNames,
            EvidenceJson = System.Text.Json.JsonSerializer.Serialize(answer.Evidence),
            Model = answer.Model,
            ToolCallCount = answer.ToolCalls,
            CreatedAt = now.AddMilliseconds(1)
        });

        session.MessageCount += 2;
        session.UpdatedAt = now;

        // 首轮问答顺手把标题补成问题摘要，省掉一次额外请求。
        if (session.MessageCount == 2 && session.Title == DefaultTitle)
        {
            session.Title = NormalizeTitle(question);
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid sessionId, Guid userId, CancellationToken ct = default)
    {
        var session = await db.AssistantSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);

        if (session is null) return false;

        db.AssistantMessages.RemoveRange(
            await db.AssistantMessages.Where(m => m.SessionId == sessionId).ToListAsync(ct));
        db.AssistantSessions.Remove(session);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public const string DefaultTitle = "新会话";

    private static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DefaultTitle;

        var trimmed = value.Trim().ReplaceLineEndings(" ");
        return trimmed.Length > 60 ? trimmed[..60] : trimmed;
    }
}
