using SecureHub.Core.Domain;

namespace SecureHub.Core.Security.Guardrails;

public static class Redactor
{
    /// <summary>部分遮蔽：手机 138****1234，证件 ****，邮箱 a***@domain。</summary>
    public static string Mask(string value)
    {
        if (value.Length <= 4) return new string('*', value.Length);

        if (value.Contains('@'))
        {
            var at = value.IndexOf('@');
            var name = value[..at];
            var domain = value[at..];
            var keep = Math.Min(1, name.Length);
            return name[..keep] + "***" + domain;
        }

        var prefix = value.Length >= 11 ? 3 : 2;
        var suffix = 4;
        return value[..prefix] + new string('*', Math.Max(0, value.Length - prefix - suffix)) + value[^suffix..];
    }

    /// <summary>按检测结果替换：把命中的敏感值替换为占位符，返回改写文本与替换数。</summary>
    public static (string Text, int Replaced) Replace(
        string text,
        IEnumerable<Detection> detections,
        bool mask)
    {
        var result = text;
        var replaced = 0;
        var index = 0;

        foreach (var detection in detections)
        {
            foreach (var value in detection.MatchedValues)
            {
                if (value.StartsWith('<') || value.Length == 0) continue;

                var placeholder = mask
                    ? Mask(value)
                    : $"<{detection.DetectorId.ToUpperInvariant()}_{++index}>";

                if (result.Contains(value, StringComparison.Ordinal))
                {
                    result = result.Replace(value, placeholder, StringComparison.Ordinal);
                    replaced++;
                }
            }
        }

        return (result, replaced);
    }
}

/// <summary>护栏管道：按预算执行检测器，超时走 fail-closed（PRD §7.2.5）。</summary>
public sealed class GuardrailPipeline(
    IEnumerable<IGuardrailDetector> detectors,
    GuardrailOptionsLike options)
{
    public async ValueTask<GuardrailVerdict> RunAsync(
        IReadOnlyList<(string FieldPath, string Text)> fields,
        GuardrailDirection direction,
        CancellationToken ct = default)
    {
        var active = detectors.Where(d => d.Direction == direction).ToArray();
        var all = new List<Detection>();

        using var totalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        totalCts.CancelAfter(options.TotalBudgetMs);

        foreach (var detector in active.Where(d => !d.RequiresExternalCall))
        {
            foreach (var (path, text) in fields)
            {
                using var perDetector = CancellationTokenSource.CreateLinkedTokenSource(totalCts.Token);
                perDetector.CancelAfter(options.PerDetectorBudgetMs);

                try
                {
                    var found = await detector.DetectAsync(text, path, perDetector.Token);
                    all.AddRange(found);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    if (string.Equals(options.OnError, "block", StringComparison.OrdinalIgnoreCase))
                    {
                        return new GuardrailVerdict(
                            GuardrailAction.Block, all, true, detector.Id);
                    }
                }
            }
        }

        return GuardrailDecision.Build(all);
    }
}

/// <summary>避免 Core 直接依赖 Options 类型带来的循环，管道只取需要的字段。</summary>
public sealed record GuardrailOptionsLike(int TotalBudgetMs, int PerDetectorBudgetMs, string OnError);
