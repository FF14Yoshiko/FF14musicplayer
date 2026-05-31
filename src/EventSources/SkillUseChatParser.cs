using System;
using System.Text.RegularExpressions;

namespace AllTimeSoundTrigger.EventSources;

public static class SkillUseChatParser
{
    // 国服简体、繁体客户端都会把动作日志写成 "角色 + 动作动词 + 技能名"。
    // 真实样本：
    // - "鳳凰發動了“刃舞”。"
    // - "伊弗利特正在發動“蓄力衝擊”。"
    // - "伊弗利特詠唱了“大宇宙”。"
    // 简体样本：
    // - "你使用了 强力射击 技能"
    // - "伊弗利特正在发动“蓄力冲击”。"
    private static readonly Regex QuotedSkillUseRegex = new(
        @"^(?<actor>.+?)\s*(?<verb>正在發動|正在发动|發動了|发动了|施放了|詠唱了|咏唱了|使用了|uses|used|casts|cast|begins casting|begin casting)\s*[「『“""](?<skill>.+?)[」』”""][。.!！]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // 兜底给没有引号的战斗日志或用户给出的描述句：
    // "你使用了 XX 技能"、"You use Heavy Swing."
    private static readonly Regex PlainSkillUseRegex = new(
        @"^(?<actor>你|您|You)\s*(?<verb>使用了|發動了|发动了|施放了|詠唱了|咏唱了|use|uses|used|cast|casts)\s*(?<skill>.+?)(?:\s*技能)?[。.!！]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool TryParse(string rawText, out SkillUseParseResult result)
    {
        var text = CleanText(rawText);
        var match = QuotedSkillUseRegex.Match(text);
        if (!match.Success)
            match = PlainSkillUseRegex.Match(text);

        if (!match.Success)
        {
            result = new SkillUseParseResult();
            return false;
        }

        var actorName = CleanActorName(match.Groups["actor"].Value);
        var skillName = CleanSkillName(match.Groups["skill"].Value);
        var verb = CleanText(match.Groups["verb"].Value);
        if (actorName.Length == 0 || skillName.Length == 0)
        {
            result = new SkillUseParseResult();
            return false;
        }

        result = new SkillUseParseResult
        {
            ActorName = actorName,
            SkillName = skillName,
            Verb = verb,
            IsCastStart = verb.Contains("正在", StringComparison.Ordinal)
                || verb.Contains("begin", StringComparison.OrdinalIgnoreCase)
        };
        return true;
    }

    public static bool IsLocalActor(string actorName, string localPlayerName)
    {
        if (actorName is "你" or "您")
            return true;

        if (actorName.Equals("you", StringComparison.OrdinalIgnoreCase))
            return true;

        return localPlayerName.Length > 0
            && actorName.Equals(localPlayerName, StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var text = Regex.Replace(raw, @"\s+", " ").Trim();
        return text.Trim('\u0000', '\u001f', '\ufffd', ' ');
    }

    private static string CleanActorName(string raw)
        => CleanText(raw)
            .Trim(' ', '\u3000', '「', '」', '『', '』', '“', '”', '"');

    private static string CleanSkillName(string raw)
    {
        var cleaned = CleanText(raw)
            .Trim(' ', '\u3000', '「', '」', '『', '』', '“', '”', '"')
            .TrimEnd('。', '.', '!', '！');

        if (cleaned.EndsWith("技能", StringComparison.Ordinal) && cleaned.Length > 2)
            cleaned = cleaned[..^2].Trim();

        return cleaned;
    }
}
