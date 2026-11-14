namespace SecureHub.Core.Domain;

/// <summary>助手会话（PRD §11.3）。消息正文只对本人生效，管理员不可读他人会话。</summary>
public sealed class AssistantSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ModelAlias { get; set; }
    public int MessageCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>助手会话中的一条消息。</summary>
public sealed class AssistantMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public AssistantMessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;

    /// <summary>该轮调用的工具名（逗号分隔），仅 assistant 消息有值。</summary>
    public string? ToolNames { get; set; }

    /// <summary>证据 ID JSON 数组（requestId / eventId），用于前端展示。</summary>
    public string? EvidenceJson { get; set; }

    /// <summary>实际使用的模型（上游模型名），仅 assistant 消息有值。</summary>
    public string? Model { get; set; }

    public int ToolCallCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
