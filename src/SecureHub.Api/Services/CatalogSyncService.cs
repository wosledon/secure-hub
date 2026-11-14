using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SecureHub.Core.Domain;
using SecureHub.Data;

namespace SecureHub.Api.Services;

public sealed record CatalogSyncResult(
    bool Success,
    int Providers,
    int Models,
    string? Error,
    DateTimeOffset? SyncedAt)
{
    public static CatalogSyncResult Fail(string error) => new(false, 0, 0, error, null);
}

/// <summary>
/// models.dev 目录同步（PRD §5.1 / T9）：每天一次，失败保留上次快照。
/// 只更新"模型能力与价格"，不自动创建供应商或凭据。
/// </summary>
public sealed class CatalogSyncService(
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory,
    AuditWriter audit,
    ILogger<CatalogSyncService> logger) : BackgroundService
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public DateTimeOffset? LastSyncedAt { get; private set; }
    public string? LastError { get; private set; }
    public int LastModelCount { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 延迟首同步：不与启动争抢资源，也避免离线环境拖慢开发调试。
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停机
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            var result = await SyncAsync(ct);
            if (!result.Success)
            {
                // 失败不回退：保留上一份快照，仅记录
                LastError = result.Error;
                logger.LogWarning("Catalog sync failed: {Error}", result.Error);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            logger.LogError(ex, "Catalog sync crashed");
        }
    }

    public async Task<CatalogSyncResult> SyncAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var client = httpClientFactory.CreateClient("catalog");
            using var response = await client.GetAsync("https://models.dev/api.json", ct);
            if (!response.IsSuccessStatusCode)
            {
                return CatalogSyncResult.Fail($"HTTP {(int)response.StatusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, JsonOptions, ct);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return CatalogSyncResult.Fail("unexpected payload: root is not an object");
            }

            var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var parsed = new List<CatalogModel>();
            var providerCount = 0;

            foreach (var provider in document.RootElement.EnumerateObject())
            {
                providerCount++;

                if (provider.Value.ValueKind != JsonValueKind.Object) continue;
                if (!provider.Value.TryGetProperty("models", out var models) ||
                    models.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var model in models.EnumerateObject())
                {
                    if (model.Value.ValueKind != JsonValueKind.Object) continue;

                    parsed.Add(new CatalogModel
                    {
                        Id = $"{provider.Name}/{model.Name}",
                        ProviderKey = provider.Name,
                        ModelId = model.Name,
                        Family = ReadString(model.Value, "family"),
                        SupportsTools = ReadBool(model.Value, "tool_call"),
                        SupportsReasoning = ReadBool(model.Value, "reasoning"),
                        SupportsStructuredOutput = ReadBool(model.Value, "structured_output"),
                        SupportsAttachment = ReadBool(model.Value, "attachment"),
                        ContextWindow = ReadInt(model.Value, "limit", "context"),
                        MaxOutputTokens = ReadInt(model.Value, "limit", "output"),
                        InputPricePerMillion = ReadDecimal(model.Value, "cost", "input"),
                        OutputPricePerMillion = ReadDecimal(model.Value, "cost", "output"),
                        CanonicalModelId = ReadString(model.Value, "canonical_model_id"),
                        SyncedAt = now
                    });
                }
            }

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureHubDbContext>();

            // 全量替换：目录是只读快照，删除旧条目比逐条 diff 简单且不会残留下架模型。
            var existing = await db.CatalogModels.ToListAsync(ct);
            db.CatalogModels.RemoveRange(existing);
            db.CatalogModels.AddRange(parsed);
            await db.SaveChangesAsync(ct);

            LastSyncedAt = now;
            LastError = null;
            LastModelCount = parsed.Count;

            await audit.AppendAsync(
                AuditEventTypes.CatalogSynced, "system", "catalog", "models.dev", "ok",
                configure: e => e.Detail = JsonSerializer.Serialize(new
                {
                    providers = providerCount,
                    models = parsed.Count
                }), ct: ct);

            logger.LogInformation(
                "Catalog synced: {Models} models from {Providers} providers",
                parsed.Count, providerCount);

            return new CatalogSyncResult(true, providerCount, parsed.Count, null, now);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return CatalogSyncResult.Fail(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();

    private static int ReadInt(JsonElement element, string parent, string name)
    {
        if (!element.TryGetProperty(parent, out var nested) ||
            nested.ValueKind != JsonValueKind.Object ||
            !nested.TryGetProperty(name, out var value))
        {
            return 0;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
    }

    private static decimal ReadDecimal(JsonElement element, string parent, string name)
    {
        if (!element.TryGetProperty(parent, out var nested) ||
            nested.ValueKind != JsonValueKind.Object ||
            !nested.TryGetProperty(name, out var value))
        {
            return 0;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : 0;
    }
}
