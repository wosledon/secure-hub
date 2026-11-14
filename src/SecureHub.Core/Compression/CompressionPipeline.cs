using System.Text;
using System.Text.RegularExpressions;

namespace SecureHub.Core.Compression;

public sealed record StageSaving(string StageId, int SavedChars);

public sealed record CompressionResult(
    string Text,
    int OriginalLength,
    int CompressedLength,
    IReadOnlyList<StageSaving> Stages,
    bool RolledBack,
    string? RollbackReason)
{
    public double Ratio => OriginalLength == 0 ? 1 : CompressedLength / (double)OriginalLength;
    public int SavedChars => Math.Max(0, OriginalLength - CompressedLength);
}

public sealed record CompressionSettings(
    bool Enabled = true,
    bool StripBoilerplate = true,
    bool CollapseRepetition = true,
    int MinRepeats = 3,
    double MinSavingsRatio = 0.02);

/// <summary>
/// 文本级去冗余（PRD §6.5）。字节级压缩（gzip）对 token 无效，故不在此实现。
/// </summary>
public static partial class CompressionPipeline
{
    [GeneratedRegex(@"\r\n|\r", RegexOptions.Compiled)]
    private static partial Regex CrlfRegex();

    [GeneratedRegex(@"[ \t]+$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex TrailingSpaceRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.Compiled)]
    private static partial Regex BlankLinesRegex();

    [GeneratedRegex(@"[\uFEFF\u200B-\u200D\u2060]", RegexOptions.Compiled)]
    private static partial Regex InvisibleCharsRegex();

    [GeneratedRegex(@"\d+", RegexOptions.Compiled)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"https?://\S+", RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    public static CompressionResult Compress(string input, CompressionSettings settings)
    {
        if (!settings.Enabled || string.IsNullOrEmpty(input))
        {
            return new CompressionResult(input, input.Length, input.Length, [], false, null);
        }

        var stages = new List<StageSaving>();
        var current = input;

        current = Apply("normalize.whitespace", current, NormalizeWhitespace, stages);
        current = Apply("normalize.unicode", current, NormalizeUnicode, stages);

        if (settings.StripBoilerplate)
        {
            current = Apply("strip.boilerplate", current, StripBoilerplate, stages);
        }

        if (settings.CollapseRepetition)
        {
            current = Apply("collapse.repetition", current,
                text => CollapseRepetition(text, settings.MinRepeats), stages);
        }

        var saved = input.Length - current.Length;
        var ratio = input.Length == 0 ? 1 : current.Length / (double)input.Length;

        if (ratio > 1 - settings.MinSavingsRatio)
        {
            return new CompressionResult(input, input.Length, input.Length, stages, false, "below-min-savings");
        }

        var violation = IntegrityGuard.Check(input, current);
        if (violation is not null)
        {
            return new CompressionResult(input, input.Length, input.Length, stages, true, violation);
        }

        return new CompressionResult(current, input.Length, current.Length, stages, false, null);
    }

    private static string Apply(
        string stageId,
        string input,
        Func<string, string> stage,
        List<StageSaving> stages)
    {
        var output = stage(input);
        var saved = input.Length - output.Length;
        if (saved == 0) return input;

        stages.Add(new StageSaving(stageId, saved));
        return output;
    }

    public static string NormalizeWhitespace(string input)
    {
        var text = InvisibleCharsRegex().Replace(input, string.Empty);
        text = CrlfRegex().Replace(text, "\n");
        text = trailingOrBlankGuard(text);
        return text;
    }

    private static string trailingOrBlankGuard(string text)
    {
        text = TrailingSpaceRegex().Replace(text, string.Empty);
        text = BlankLinesRegex().Replace(text, "\n\n");
        return text;
    }

    public static string NormalizeUnicode(string input) => input.Normalize(NormalizationForm.FormC);

    /// <summary>去掉引用前缀、签名分隔线与分隔线块。</summary>
    public static string StripBoilerplate(string input)
    {
        var lines = input.Split('\n');
        var output = new List<string>(lines.Length);
        var inSignature = false;

        foreach (var raw in lines)
        {
            var line = raw;

            if (line.TrimEnd() == "-- " || line.TrimEnd() == "--")
            {
                inSignature = true;
                continue;
            }

            if (inSignature) continue;

            // 纯分隔线（---、===、***）连续出现时只保留一条
            var trimmed = line.Trim();
            if (trimmed.Length >= 3 &&
                (trimmed.All(c => c == '-') || trimmed.All(c => c == '=') || trimmed.All(c => c == '*')))
            {
                if (output.Count > 0 && output[^1] == "---") continue;
                output.Add("---");
                continue;
            }

            // 引用前缀 "> " 去重为单层
            if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                while (line.StartsWith("> ", StringComparison.Ordinal))
                {
                    line = line[2..];
                }

                output.Add(line);
                continue;
            }

            output.Add(line);
        }

        return string.Join('\n', output);
    }

    /// <summary>连续相同行折叠为 <c>[line ×N]</c>；含数字/URL 的重复块不折叠，避免信息丢失。</summary>
    public static string CollapseRepetition(string input, int minRepeats)
    {
        if (minRepeats < 2) minRepeats = 2;

        var lines = input.Split('\n');
        var output = new List<string>(lines.Length);
        var i = 0;

        while (i < lines.Length)
        {
            var j = i + 1;
            while (j < lines.Length && string.Equals(lines[j], lines[i], StringComparison.Ordinal))
            {
                j++;
            }

            var count = j - i;
            var line = lines[i];

            if (count >= minRepeats && line.Length > 0 && !ContainsVerifiableToken(line))
            {
                output.Add($"{line} [×{count}]");
            }
            else
            {
                for (var k = 0; k < count; k++) output.Add(line);
            }

            i = j;
        }

        return string.Join('\n', output);
    }

    private static bool ContainsVerifiableToken(string line) =>
        NumberRegex().IsMatch(line) || UrlRegex().IsMatch(line);
}

/// <summary>完整性校验：关键实体不得减少、括号必须配对（PRD §6.5.6）。</summary>
public static class IntegrityGuard
{
    private static readonly Regex NumberRegex = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"https?://\S+", RegexOptions.Compiled);

    public static string? Check(string before, string after)
    {
        if (Count(NumberRegex, after) < Count(NumberRegex, before)) return "numbers-lost";
        if (Count(UrlRegex, after) < Count(UrlRegex, before)) return "urls-lost";
        if (!BracketsBalanced(after)) return "unbalanced-brackets";
        return null;
    }

    private static int Count(Regex regex, string text) => regex.Matches(text).Count;

    public static bool BracketsBalanced(string text)
    {
        var stack = new Stack<char>();
        foreach (var c in text)
        {
            switch (c)
            {
                case '(' or '[' or '{':
                    stack.Push(c);
                    break;
                case ')' or ']' or '}':
                    if (stack.Count == 0) return false;
                    var open = stack.Pop();
                    var matches = (open, c) switch
                    {
                        ('(', ')') => true,
                        ('[', ']') => true,
                        ('{', '}') => true,
                        _ => false
                    };
                    if (!matches) return false;
                    break;
            }
        }

        return stack.Count == 0;
    }
}
