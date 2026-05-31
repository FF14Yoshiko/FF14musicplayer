using System;
using System.Text.RegularExpressions;

namespace AllTimeSoundTrigger.EventSources;

public static class KillChatParser
{
    private static readonly Regex YouKillRegex = new(
        @"^(?:你|您)(?:击倒了|擊倒了|击败了|擊敗了|击杀了|擊殺了|杀死了|殺死了|打倒了|讨伐了|討伐了|消灭了|消滅了|战胜了|戰勝了)\s*(?<target>.+?)[。.!！]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex YouKillEnglishRegex = new(
        @"^You\s+(?:defeat|defeated|knock out|knocked out|slay|slew|kill|killed)\s+(?<target>.+?)[。.!！]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SenderKillRegex = new(
        @"^(?:击倒了|擊倒了|击败了|擊敗了|击杀了|擊殺了|杀死了|殺死了|打倒了|讨伐了|討伐了|消灭了|消滅了|战胜了|戰勝了|defeated|defeats|knocked out|kills)\s*(?<target>.+?)[。.!！]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex TargetDefeatedByActorRegex = new(
        @"^(?<target>.+?)(?:被|遭到)\s*(?<actor>.+?)(?:击倒|擊倒|击败|擊敗|击杀|擊殺|杀死|殺死|打倒|讨伐|討伐|消灭|消滅|战胜|戰勝)[了]?[。.!！]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex[] KillRegexes =
    [
        new(@"^(?<actor>.+?)(?:击倒了|擊倒了|击败了|擊敗了|击杀了|擊殺了|杀死了|殺死了|打倒了|讨伐了|討伐了|消灭了|消滅了|战胜了|戰勝了)\s*(?<target>.+?)[。.!！]?$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
        new(@"^(?<target>.+?)(?:被|遭到)\s*(?<actor>.+?)(?:击倒|擊倒|击败|擊敗|击杀|擊殺|杀死|殺死|打倒|讨伐|討伐|消灭|消滅|战胜|戰勝)[了]?[。.!！]?$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
        new(@"^(?<actor>.+?)\s+(?:defeated|defeats|knocked out|knocks out|slew|slays|killed|kills)\s+(?<target>.+?)[。.!！]?$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
        new(@"^(?<target>.+?)\s+was\s+(?:defeated|knocked out|slain|killed)\s+by\s+(?<actor>.+?)[。.!！]?$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
    ];

    public static bool TryParse(string rawText, string sender, out KillParseResult result)
    {
        var text = CleanText(rawText);
        sender = CleanName(sender);
        if (!ContainsKillKeyword(text))
        {
            result = default;
            return false;
        }

        if (TryMatch(text, YouKillRegex, out var youKillMatch))
        {
            result = new KillParseResult("你", CleanName(youKillMatch.Groups["target"].Value));
            return result.TargetName.Length > 0;
        }

        if (TryMatch(text, YouKillEnglishRegex, out var youKillEnglishMatch))
        {
            result = new KillParseResult("You", CleanName(youKillEnglishMatch.Groups["target"].Value));
            return result.TargetName.Length > 0;
        }

        if (sender.Length > 0 && TryMatch(text, SenderKillRegex, out var senderKillMatch))
        {
            result = new KillParseResult(sender, CleanName(senderKillMatch.Groups["target"].Value));
            return result.TargetName.Length > 0;
        }

        if (TryMatch(text, TargetDefeatedByActorRegex, out var targetDefeatedMatch))
        {
            result = new KillParseResult(
                CleanName(targetDefeatedMatch.Groups["actor"].Value),
                CleanName(targetDefeatedMatch.Groups["target"].Value));
            return result.ActorName.Length > 0 && result.TargetName.Length > 0;
        }

        foreach (var regex in KillRegexes)
        {
            var match = regex.Match(text);
            if (!match.Success)
                continue;

            var actor = CleanName(match.Groups["actor"].Value);
            var target = CleanName(match.Groups["target"].Value);
            result = new KillParseResult(actor, target);
            return actor.Length > 0 && target.Length > 0 && !actor.Equals(target, StringComparison.OrdinalIgnoreCase);
        }

        result = default;
        return false;
    }

    private static bool TryMatch(string text, Regex regex, out Match match)
    {
        match = regex.Match(text);
        return match.Success;
    }

    private static bool ContainsKillKeyword(string text)
        => text.Contains("击倒", StringComparison.Ordinal)
            || text.Contains("擊倒", StringComparison.Ordinal)
            || text.Contains("击败", StringComparison.Ordinal)
            || text.Contains("擊敗", StringComparison.Ordinal)
            || text.Contains("击杀", StringComparison.Ordinal)
            || text.Contains("擊殺", StringComparison.Ordinal)
            || text.Contains("杀死", StringComparison.Ordinal)
            || text.Contains("殺死", StringComparison.Ordinal)
            || text.Contains("打倒", StringComparison.Ordinal)
            || text.Contains("讨伐", StringComparison.Ordinal)
            || text.Contains("討伐", StringComparison.Ordinal)
            || text.Contains("消灭", StringComparison.Ordinal)
            || text.Contains("消滅", StringComparison.Ordinal)
            || text.Contains("战胜", StringComparison.Ordinal)
            || text.Contains("戰勝", StringComparison.Ordinal)
            || text.Contains("defeat", StringComparison.OrdinalIgnoreCase)
            || text.Contains("knock", StringComparison.OrdinalIgnoreCase)
            || text.Contains("slain", StringComparison.OrdinalIgnoreCase)
            || text.Contains("kill", StringComparison.OrdinalIgnoreCase);

    private static string CleanText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var text = Regex.Replace(raw, @"\s+", " ").Trim();
        return text.Trim('\u0000', '\u001f', '\ufffd', ' ');
    }

    private static string CleanName(string? raw)
        => CleanText(raw)
            .Trim(' ', '\u3000', '「', '」', '『', '』', '“', '”', '"', '。', '.', '!', '！');
}
