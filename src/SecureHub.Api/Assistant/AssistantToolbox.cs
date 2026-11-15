using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using SecureHub.Api.Services;
using SecureHub.Core.Abstractions;
using SecureHub.Core.Domain;
using SecureHub.Core.Options;
using SecureHub.Api.Endpoints;
using SecureHub.Core.Security;
using SecureHub.Core.Security.Guardrails;
using SecureHub.Data;

namespace SecureHub.Api.Assistant;

/// <summary>一次助手问答内的工具调用状态：计数、证据、工具名轨迹；角色决定可见工具集。</summary>
public sealed class AssistantToolSession(Guid userId, string model, IReadOnlyList<UserRole>? roles = null)
{
    private readonly object _gate = new();
    private readonly List<string> _evidence = [];
    private readonly List<string> _invoked = [];
    private int _calls;

    public Guid UserId { get; } = userId;
    public string Model { get; } = model;
    public IReadOnlyList<UserRole> Roles { get; } = roles ?? [];

    public int CallCount => Volatile.Read(ref _calls);

    public IReadOnlyList<string> Evidence
    {
        get { lock (_gate) return _evidence.Distinct(StringComparer.Ordinal).Take(50).ToArray(); }
    }

    public string? ToolNames
    {
        get
        {
            lock (_gate)
            {
                return _invoked.Count == 0
                    ? null
                    : string.Join(',', _invoked.Distinct(StringComparer.Ordinal).Take(20));
            }
        }
    }

    public void Record(string toolName, IReadOnlyList<string> evidence)
    {
        Interlocked.Increment(ref _calls);

        lock (_gate)
        {
            _invoked.Add(toolName);
            foreach (var id in evidence)
            {
                _evidence.Add(id);
            }
        }
    }
}

/// <summary>
/// 助手工具集（PRD §11.5）。只读为主，护栏创建两个工具按 RBAC 注入并二次鉴权；每次调用都写 <c>assistant_tool_invoked</c> 审计；
/// 结果一律截断，避免一次追问把大量数据送进第三方模型。
/// </summary>
public sealed class AssistantToolbox(
    SecureHubDbContext db,
    ContentStore content,
    CustomRuleStore customRules,
    PolicyStore policyStore,
    AuditWriter audit,
    IClock clock,
    IOptions<SecureHubOptions> options)
{
    private int MaxChars => Math.Clamp(options.Value.Assistant.MaxToolResultChars, 1_000, 40_000);

    public IReadOnlyList<AITool> Build(AssistantToolSession session, CancellationToken ct)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(
                (int days, string? eventType, int limit) =>
                    RunAsync("search_audit_events", session, ct, () => SearchAuditEventsAsync(days, eventType, limit, ct)),
                "search_audit_events",
                "按时间范围与事件类型检索审计事件，只返回元数据"),

            AIFunctionFactory.Create(
                (int days) =>
                    RunAsync("get_usage_summary", session, ct, () => GetUsageSummaryAsync(days, ct)),
                "get_usage_summary",
                "近 N 天的调用量、token 与成本汇总"),

            AIFunctionFactory.Create(
                (int days, string? bucket) =>
                    RunAsync("get_cost_trend", session, ct, () => GetCostTrendAsync(days, bucket, ct)),
                "get_cost_trend",
                "按时间桶（day/week）返回成本与调用量趋势"),

            AIFunctionFactory.Create(
                (string requestId) =>
                    RunAsync("explain_request", session, ct, () => ExplainRequestAsync(requestId, ct)),
                "explain_request",
                "查看单个 requestId 的完整审计链路"),

            AIFunctionFactory.Create(
                (string? query, string? providerKey, int limit) =>
                    RunAsync("search_model_catalog", session, ct, () => SearchModelCatalogAsync(query, providerKey, limit, ct)),
                "search_model_catalog",
                "在 models.dev 目录里搜索模型能力与价格"),

            AIFunctionFactory.Create(
                () => RunAsync("list_models", session, ct, () => ListModelsAsync(ct)),
                "list_models",
                "列出当前可用的模型与其能力"),

            AIFunctionFactory.Create(
                () => RunAsync("get_gateway_health", session, ct, GetGatewayHealthAsync),
                "get_gateway_health",
                "供应商健康状态与近 24 小时的失败率"),

            AIFunctionFactory.Create(
                (string requestId, string? direction, string? variant) =>
                    RunAsync("read_content", session, ct, () => ReadContentAsync(requestId, direction, variant, ct)),
                "read_content",
                "读取单个请求/响应的正文（默认关闭，需管理员开启）")
        };

        // 护栏工具按 RBAC 注入：拿不到 SecurityRead 就看不到 list，拿不到 SecurityWrite 就没有 create。
        if (Rbac.AllowsAny(session.Roles, Permission.SecurityRead))
        {
            tools.Add(AIFunctionFactory.Create(
                () => RunAsync("list_guardrails", session, ct, () => ListGuardrailsAsync(ct)),
                "list_guardrails",
                "列出安全护栏：自定义敏感词规则与安全策略（含 detector id，供策略条件引用）"));
        }

        if (Rbac.AllowsAny(session.Roles, Permission.SecurityWrite))
        {
            tools.Add(AIFunctionFactory.Create(
                (string? type, string? key, string? pattern, string? matchType, string? severity,
                 string? direction, string? description, string? scopeApps, int? priority,
                 string? action, string? detector, int? minMatches, string? reason) =>
                    RunAsync("create_guardrail", session, ct,
                        () => CreateGuardrailAsync(session, type, key, pattern, matchType, severity,
                            direction, description, scopeApps, priority, action, detector,
                            minMatches, reason, ct)),
                "create_guardrail",
                "创建安全护栏并立即生效，同时写审计。type=rule 建自定义敏感词规则（key、pattern、match_type=keyword|regex、severity=info|warning|critical、direction=inbound|outbound、description）；"
                + "type=policy 建安全策略（key、scope_apps 逗号分隔、priority、action=block|redact|alert、detector、min_matches、reason）。"
                + "policy 至少要有一个 scope 或一个 detector，detector 必须是已存在的 id（内置或 custom.{key}）。"));
        }

        return tools;
    }

    private async Task<string> RunAsync(
        string toolName,
        AssistantToolSession session,
        CancellationToken ct,
        Func<Task<(string Text, IReadOnlyList<string> Evidence)>> work)
    {
        string text;
        IReadOnlyList<string> evidence;

        try
        {
            (text, evidence) = await work();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            text = JsonSerializer.Serialize(new { error = "tool_failed", message = ex.GetType().Name });
            evidence = [];
        }

        session.Record(toolName, evidence);

        await audit.AppendAsync(
            AuditEventTypes.AssistantToolInvoked, "user", session.UserId.ToString(), "assistant", "ok",
            billingOwner: BillingOwner.User,
            configure: e =>
            {
                e.ModelAlias = session.Model;
                e.Detail = JsonSerializer.Serialize(new { tool = toolName, result_chars = text.Length });
            }, ct: ct);

        return Truncate(text);
    }

    private string Truncate(string value) =>
        value.Length > MaxChars ? value[..MaxChars] + "…(truncated)" : value;

    private async Task<(string, IReadOnlyList<string>)> SearchAuditEventsAsync(
        int days, string? eventType, int limit, CancellationToken ct)
    {
        var window = ClampDays(days);
        var take = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);
        var since = clock.UtcNow.AddDays(-window);

        var query = db.AuditEvents.AsNoTracking().Where(e => e.OccurredAt >= since);
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(e => e.EventType == eventType);
        }

        var rows = await query.OrderByDescending(e => e.Sequence).Take(take).ToListAsync(ct);

        var payload = new
        {
            range_days = window,
            count = rows.Count,
            by_type = rows.GroupBy(r => r.EventType).Select(g => new { type = g.Key, count = g.Count() }),
            by_rule = rows.Where(r => r.RuleId != null)
                .GroupBy(r => r.RuleId!)
                .Select(g => new { rule = g.Key, count = g.Count() }),
            events = rows.Select(r => new
            {
                event_id = r.EventId,
                occurred_at = r.OccurredAt,
                type = r.EventType,
                action = r.Action.ToString().ToLowerInvariant(),
                actor = r.ActorLabel,
                app_name = r.AppName ?? (r.ActorType == "app" ? r.ActorLabel : null),
                request_id = r.RequestId,
                rule = r.RuleId,
                policy = r.PolicyId
            })
        };

        return (JsonSerializer.Serialize(payload), rows.Take(20).Select(r => r.EventId.ToString()).ToArray());
    }

    private async Task<(string, IReadOnlyList<string>)> GetUsageSummaryAsync(int days, CancellationToken ct)
    {
        var window = ClampDays(days);
        var since = clock.UtcNow.AddDays(-window);

        var rows = await db.UsageEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= since)
            .ToListAsync(ct);

        var payload = new
        {
            range_days = window,
            total_requests = rows.Count,
            total_input_tokens = rows.Sum(r => r.InputTokens),
            total_input_tokens_before_compression = rows.Sum(r => r.InputTokensOriginal),
            total_output_tokens = rows.Sum(r => r.OutputTokens),
            total_cost_usd = rows.Sum(r => r.CostUsd),
            by_billing = rows.GroupBy(r => r.BillingOwner).Select(g => new
            {
                owner = g.Key.ToString().ToLowerInvariant(),
                requests = g.Count(),
                cost_usd = g.Sum(x => x.CostUsd)
            }),
            by_model = rows.GroupBy(r => r.Model).Select(g => new
            {
                model = g.Key,
                requests = g.Count(),
                input_tokens = g.Sum(x => x.InputTokens),
                cost_usd = g.Sum(x => x.CostUsd)
            }).OrderByDescending(x => x.cost_usd).Take(10)
        };

        return (JsonSerializer.Serialize(payload), []);
    }

    private async Task<(string, IReadOnlyList<string>)> GetCostTrendAsync(
        int days, string? bucket, CancellationToken ct)
    {
        var window = ClampDays(days);
        var sinceDate = clock.UtcNow.AddDays(-window).UtcDateTime.Date;
        var since = new DateTimeOffset(sinceDate, TimeSpan.Zero);
        var weekly = string.Equals(bucket, "week", StringComparison.OrdinalIgnoreCase);

        var rows = await db.UsageEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= since)
            .Select(e => new { e.OccurredAt, e.CostUsd, e.InputTokens, e.OutputTokens })
            .ToListAsync(ct);

        var points = rows
            .GroupBy(r => BucketKey(r.OccurredAt.UtcDateTime.Date, weekly))
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                bucket = g.Key,
                requests = g.Count(),
                input_tokens = g.Sum(x => x.InputTokens),
                output_tokens = g.Sum(x => x.OutputTokens),
                cost_usd = g.Sum(x => x.CostUsd)
            })
            .ToArray();

        // 环比：后半窗 vs 前半窗，给模型一个可直接引用的数字。
        var midpoint = since.AddDays(window / 2.0);
        var firstHalf = rows.Where(r => r.OccurredAt < midpoint).Sum(r => r.CostUsd);
        var secondHalf = rows.Where(r => r.OccurredAt >= midpoint).Sum(r => r.CostUsd);

        var payload = new
        {
            range_days = window,
            bucket = weekly ? "week" : "day",
            points,
            first_half_cost_usd = firstHalf,
            second_half_cost_usd = secondHalf,
            change_ratio = firstHalf == 0 ? (double?)null : (double)((secondHalf - firstHalf) / firstHalf)
        };

        return (JsonSerializer.Serialize(payload), []);
    }

    private async Task<(string, IReadOnlyList<string>)> ExplainRequestAsync(string requestId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return ("缺少 requestId 参数。", []);
        }

        var rows = await db.AuditEvents.AsNoTracking()
            .Where(e => e.RequestId == requestId)
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return ($"未找到 requestId={requestId} 的审计事件。", []);
        }

        var payload = new
        {
            request_id = requestId,
            chain = rows.Select(r => new
            {
                sequence = r.Sequence,
                occurred_at = r.OccurredAt,
                type = r.EventType,
                action = r.Action.ToString().ToLowerInvariant(),
                rule = r.RuleId,
                policy = r.PolicyId,
                policy_version = r.PolicyVersion,
                provider = r.ProviderId,
                model = r.UpstreamModel,
                tokens_in = r.InputTokens,
                tokens_in_before_compression = r.InputTokensOriginal,
                tokens_out = r.OutputTokens,
                content_missing = r.ContentMissing
            })
        };

        return (JsonSerializer.Serialize(payload), rows.Select(r => r.EventId.ToString()).ToArray());
    }

    private async Task<(string, IReadOnlyList<string>)> SearchModelCatalogAsync(
        string? query, string? providerKey, int limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit <= 0 ? 20 : limit, 1, 100);

        var q = db.CatalogModels.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(providerKey))
        {
            q = q.Where(m => m.ProviderKey == providerKey);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";
            q = q.Where(m => EF.Functions.Like(m.ModelId, pattern) || EF.Functions.Like(m.ProviderKey, pattern));
        }

        var rows = await q.OrderBy(m => m.ProviderKey).ThenBy(m => m.ModelId).Take(take).ToListAsync(ct);

        var payload = new
        {
            count = rows.Count,
            models = rows.Select(m => new
            {
                provider = m.ProviderKey,
                model = m.ModelId,
                context_window = m.ContextWindow,
                max_output_tokens = m.MaxOutputTokens,
                tools = m.SupportsTools,
                reasoning = m.SupportsReasoning,
                structured_output = m.SupportsStructuredOutput,
                input_price = m.InputPricePerMillion,
                output_price = m.OutputPricePerMillion
            })
        };

        return (JsonSerializer.Serialize(payload), []);
    }

    private async Task<(string, IReadOnlyList<string>)> ListModelsAsync(CancellationToken ct)
    {
        var providers = await db.Providers.AsNoTracking().Where(p => p.Enabled).ToListAsync(ct);
        var offerings = await db.ProviderOfferings.AsNoTracking().Where(o => o.Enabled).ToListAsync(ct);

        var payload = offerings.Select(o =>
        {
            var provider = providers.FirstOrDefault(p => p.Id == o.ProviderId);
            return new
            {
                id = provider is null
                    ? o.UpstreamModel
                    : $"{(provider.Scope == ProviderScope.User ? "me" : "org")}/{provider.Alias}/{o.UpstreamModel}",
                context_window = o.ContextWindow,
                tools = o.SupportsTools,
                input_price = o.InputPricePerMillion
            };
        }).Take(100);

        return (JsonSerializer.Serialize(payload), []);
    }

    private async Task<(string, IReadOnlyList<string>)> GetGatewayHealthAsync()
    {
        var providers = await db.Providers.AsNoTracking().ToListAsync();
        var since = clock.UtcNow.AddHours(-24);

        var recent = await db.AuditEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= since &&
                        (e.EventType == AuditEventTypes.RouteDenied ||
                         e.EventType == AuditEventTypes.RouteFailover ||
                         e.EventType == AuditEventTypes.RouteSelected))
            .Select(e => new { e.EventType, e.ProviderId })
            .ToListAsync();

        var payload = new
        {
            window_hours = 24,
            providers = providers.Select(p =>
            {
                var mine = recent.Where(r => r.ProviderId == p.Id).ToList();
                var denied = mine.Count(r => r.EventType == AuditEventTypes.RouteDenied);
                var selected = mine.Count(r => r.EventType == AuditEventTypes.RouteSelected);
                var attempts = denied + selected;

                return new
                {
                    alias = p.Alias,
                    scope = p.Scope.ToString().ToLowerInvariant(),
                    enabled = p.Enabled,
                    health = p.HealthStatus,
                    route_denied = denied,
                    route_selected = selected,
                    failure_ratio = attempts == 0 ? (double?)null : (double)denied / attempts
                };
            })
        };

        return (JsonSerializer.Serialize(payload), []);
    }

    private async Task<(string, IReadOnlyList<string>)> ReadContentAsync(
        string requestId, string? direction, string? variant, CancellationToken ct)
    {
        // 正文对助手默认关闭：一次追问就能把大量正文送进第三方模型（PRD §11.5）。
        if (!options.Value.Assistant.AllowContentRead)
        {
            return (JsonSerializer.Serialize(new
            {
                error = ReasonCodes.ContentReadDisabled,
                hint = "管理员需在 SecureHub:Assistant:AllowContentRead 开启后可用。"
            }), []);
        }

        if (string.IsNullOrWhiteSpace(requestId))
        {
            return ("缺少 requestId 参数。", []);
        }

        var dir = ParseEnum<ContentDirection>(direction, ContentDirection.Request);
        var ver = ParseEnum<ContentVariant>(variant, ContentVariant.Redacted);

        var text = await content.ReadAsync(requestId, dir, ver, ct);
        if (text is null)
        {
            return (JsonSerializer.Serialize(new { error = ReasonCodes.ContentNotFound, request_id = requestId }), []);
        }

        await audit.AppendAsync(
            AuditEventTypes.ContentViewed, "assistant", "assistant", "assistant", "ok",
            configure: e =>
            {
                e.RequestId = requestId;
                e.Detail = JsonSerializer.Serialize(new
                {
                    direction = dir.ToString().ToLowerInvariant(),
                    variant = ver.ToString().ToLowerInvariant(),
                    chars = text.Length
                });
            }, ct: ct);

        var limit = Math.Clamp(options.Value.Assistant.MaxContentReadChars, 200, 40_000);
        var body = text.Length > limit ? text[..limit] : text;

        return (JsonSerializer.Serialize(new
        {
            request_id = requestId,
            direction = dir.ToString().ToLowerInvariant(),
            variant = ver.ToString().ToLowerInvariant(),
            truncated = text.Length > limit,
            content = body
        }), [requestId]);
    }

    /// <summary>列出自定义规则与安全策略；证据留空——列表本身不是任何结论的依据。</summary>
    private async Task<(string, IReadOnlyList<string>)> ListGuardrailsAsync(CancellationToken ct)
    {
        var policies = await db.SecurityPolicies.AsNoTracking()
            .OrderBy(p => p.PolicyKey)
            .Select(p => new { p.PolicyKey, p.Enabled, p.ActiveVersion, p.CreatedAt })
            .ToListAsync(ct);

        var payload = new
        {
            rules = customRules.Rules.Select(r => new
            {
                detector_id = r.DetectorId,
                r.Key,
                r.Pattern,
                match_type = r.MatchType,
                severity = r.Severity.ToString().ToLowerInvariant(),
                direction = r.Direction.ToString().ToLowerInvariant(),
                r.Enabled,
                r.Description
            }),
            policies,
            limits = new
            {
                max_rules = CustomRuleValidation.MaxRules,
                max_pattern_length = CustomRuleValidation.MaxPatternLength
            }
        };

        return (JsonSerializer.Serialize(payload), []);
    }

    private async Task<(string, IReadOnlyList<string>)> CreateGuardrailAsync(
        AssistantToolSession session, string? type, string? key, string? pattern, string? matchType,
        string? severity, string? direction, string? description, string? scopeApps, int? priority,
        string? action, string? detector, int? minMatches, string? reason, CancellationToken ct)
    {
        // Build 已按角色注入，这里再挡一次：工具集随会话角色决定，不能只靠「看不见」。
        if (!Rbac.AllowsAny(session.Roles, Permission.SecurityWrite))
        {
            return (JsonSerializer.Serialize(new { error = ReasonCodes.InsufficientScope }), []);
        }

        return type?.Trim().ToLowerInvariant() switch
        {
            "rule" => await CreateGuardRuleAsync(session, key, pattern, matchType, severity, direction, description, ct),
            "policy" => await CreateGuardPolicyAsync(session, key, scopeApps, priority, action, detector, minMatches, reason, ct),
            _ => (JsonSerializer.Serialize(new
            {
                error = "guardrail_type_invalid",
                hint = "type 必须是 rule（自定义敏感词规则）或 policy（安全策略）"
            }), Array.Empty<string>())
        };
    }

    /// <summary>规则路径：与管理端 POST /api/admin/guardrails/rules 同一套校验与归一化。</summary>
    private async Task<(string, IReadOnlyList<string>)> CreateGuardRuleAsync(
        AssistantToolSession session, string? key, string? pattern, string? matchType,
        string? severity, string? direction, string? description, CancellationToken ct)
    {
        if (customRules.Rules.Count >= CustomRuleValidation.MaxRules)
        {
            return (JsonSerializer.Serialize(new { error = "rule_limit_exceeded" }), []);
        }

        var rule = CustomRuleEndpoints.ToRule(new UpsertCustomRuleRequest(
            Key: key,
            Pattern: pattern,
            MatchType: matchType,
            IgnoreCase: null,
            Severity: severity,
            Direction: direction,
            Enabled: true,
            Sample: null,
            Description: description));

        if (CustomRuleValidation.Validate(rule, isDuplicateKey: customRules.Contains(rule.DetectorId)) is { } error)
        {
            return (JsonSerializer.Serialize(new { error }), []);
        }

        await customRules.SaveAsync([.. customRules.Rules, rule], session.UserId, ct);

        await audit.AppendAsync(AuditEventTypes.CustomRuleCreated, "guardrail_rule", rule.Key,
            "assistant", "ok", severity: AuditSeverity.Warning,
            configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                rule.Key,
                rule.Pattern,
                rule.MatchType,
                rule.Severity,
                rule.Direction,
                rule.Enabled,
                by = session.UserId,
                source = "assistant"
            }, CustomRuleJson.Options), ct: ct);

        return (JsonSerializer.Serialize(new
        {
            ok = true,
            detector_id = rule.DetectorId,
            key = rule.Key
        }), [rule.DetectorId]);
    }

    /// <summary>策略路径：与管理端 POST /api/admin/policies 同一套校验、落库与快照刷新。</summary>
    private async Task<(string, IReadOnlyList<string>)> CreateGuardPolicyAsync(
        AssistantToolSession session, string? key, string? scopeApps, int? priority,
        string? action, string? detector, int? minMatches, string? reason, CancellationToken ct)
    {
        var apps = string.IsNullOrWhiteSpace(scopeApps)
            ? null
            : scopeApps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var request = new PublishPolicyRequest(
            key ?? string.Empty,
            priority ?? 100,
            apps is null ? null : new PolicyScopeDto(null, apps, null),
            string.IsNullOrWhiteSpace(detector)
                ? null
                : new PolicyWhenDto([new PolicyDetectorDto(detector.Trim(), Math.Max(1, minMatches ?? 1))], []),
            new PolicyThenDto(string.IsNullOrWhiteSpace(action) ? "block" : action.Trim().ToLowerInvariant(),
                string.Empty, null),
            OnError: null,
            RolloutPercent: null,
            ChangeReason: reason);

        if (PolicyDraft.Validate(request, customRules) is { } error)
        {
            return (JsonSerializer.Serialize(new { error }), []);
        }

        var result = await PolicyDraft.PublishAsync(db, policyStore, request, session.UserId, clock, ct);

        await audit.AppendAsync(
            result.Created ? AuditEventTypes.PolicyCreated : AuditEventTypes.PolicyUpdated,
            "user", session.UserId.ToString(), "assistant", "ok",
            configure: e => e.Detail = JsonSerializer.Serialize(new
            {
                policy_key = result.PolicyKey,
                version = result.Version,
                action = result.Action,
                priority = request.Priority,
                source = "assistant"
            }), ct: ct);

        return (JsonSerializer.Serialize(new
        {
            ok = true,
            policy_key = result.PolicyKey,
            version = result.Version,
            created = result.Created,
            snapshot_reloaded = result.SnapshotReloaded,
            error = result.StoreError
        }), [result.PolicyKey]);
    }

    private int ClampDays(int days) => Math.Clamp(days <= 0 ? 7 : days, 1, 90);

    private static string BucketKey(DateTime day, bool weekly)
    {
        if (!weekly) return day.ToString("yyyy-MM-dd");

        var start = day.AddDays(-(int)day.DayOfWeek);
        return start.ToString("yyyy-MM-dd");
    }

    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : fallback;
}
