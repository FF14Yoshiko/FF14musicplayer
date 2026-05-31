using System;
using System.Text.RegularExpressions;

namespace AllTimeSoundTrigger.EventSources;

public static class ItemAcquiredChatParser
{
    // Item acquisition lines are localized by the game client and can include item links.
    // TextValue strips the link payload, so the parser keeps the patterns intentionally
    // broad and leaves the original chat text in the payload for later sample tuning.
    private static readonly Regex[] ChineseRegexes =
    [
        new(@"^(?<actor>你|您|.+?)\s*(?:获得了|获得|取得了|取得|得到了|拾取了|拾取|捡到了|捡到|拿到了|拿到)\s*(?<item>.+?)(?:\s*[xX×]\s*(?<count>\d+))?[。.!！?？]?$", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"^(?<actor>你|您|.+?)\s*从.+?(?:获得了|获得|取得了|取得|拾取了|拾取)\s*(?<item>.+?)(?:\s*[xX×]\s*(?<count>\d+))?[。.!！?？]?$", RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"^(?:获得了|获得|取得了|取得|拾取了|拾取)\s*(?<item>.+?)(?:\s*[xX×]\s*(?<count>\d+))?[。.!！?？]?$", RegexOptions.Compiled | RegexOptions.CultureInvariant)
    ];

    private static readonly Regex[] EnglishRegexes =
    [
        new(@"^(?<actor>You|.+?)\s+(?:obtain|obtained|receive|received|acquire|acquired|pick up|picked up|gain|gained)\s+(?<item>.+?)(?:\s*[xX×]\s*(?<count>\d+))?[.!?]?$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
        new(@"^(?:You\s+)?(?:obtain|obtained|receive|received|acquire|acquired|pick up|picked up|gain|gained)\s+(?<item>.+?)(?:\s*[xX×]\s*(?<count>\d+))?[.!?]?$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
    ];

    private static readonly Regex LeadingArticleRegex = new(@"^(?:a|an|the)\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex QuantitySuffixRegex = new(@"\s*(?:x|×)\s*(?<count>\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool TryParse(string rawText, string sender, out ItemAcquiredParseResult result)
    {
        var text = CleanText(rawText);
        sender = CleanText(sender);
        if (!LooksLikeItemAcquisition(text))
        {
            result = default;
            return false;
        }

        foreach (var regex in ChineseRegexes)
        {
            if (TryMatch(regex, text, sender, out result))
                return true;
        }

        foreach (var regex in EnglishRegexes)
        {
            if (TryMatch(regex, text, sender, out result))
                return true;
        }

        result = default;
        return false;
    }

    private static bool TryMatch(Regex regex, string text, string sender, out ItemAcquiredParseResult result)
    {
        var match = regex.Match(text);
        if (!match.Success)
        {
            result = default;
            return false;
        }

        var actor = match.Groups["actor"].Success
            ? CleanName(match.Groups["actor"].Value)
            : string.Empty;
        if (actor.Length == 0)
            actor = sender.Length > 0 ? sender : "你";

        var itemName = CleanItemName(match.Groups["item"].Value, out var suffixQuantity);
        var quantity = ParseQuantity(match.Groups["count"].Value);
        if (quantity <= 0)
            quantity = suffixQuantity > 0 ? suffixQuantity : 1;

        if (itemName.Length == 0)
        {
            result = default;
            return false;
        }

        result = new ItemAcquiredParseResult(actor, itemName, quantity);
        return true;
    }

    private static bool LooksLikeItemAcquisition(string text)
        => text.Contains("获得", StringComparison.Ordinal)
            || text.Contains("取得", StringComparison.Ordinal)
            || text.Contains("得到", StringComparison.Ordinal)
            || text.Contains("拾取", StringComparison.Ordinal)
            || text.Contains("捡到", StringComparison.Ordinal)
            || text.Contains("拿到", StringComparison.Ordinal)
            || text.Contains("obtain", StringComparison.OrdinalIgnoreCase)
            || text.Contains("receive", StringComparison.OrdinalIgnoreCase)
            || text.Contains("acquire", StringComparison.OrdinalIgnoreCase)
            || text.Contains("pick up", StringComparison.OrdinalIgnoreCase)
            || text.Contains("gain", StringComparison.OrdinalIgnoreCase);

    private static string CleanText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var text = Regex.Replace(raw, @"\s+", " ").Trim();
        return text.Trim('\u0000', '\u001f', '\ufffd', ' ');
    }

    private static string CleanName(string raw)
        => CleanText(raw)
            .Trim(' ', '\u3000', '。', '.', '!', '！', '?', '？', ',', '，', ':', '：', ';', '；', '「', '」', '『', '』', '“', '”', '"');

    private static string CleanItemName(string raw, out int quantity)
    {
        quantity = 0;
        var cleaned = CleanText(raw)
            .Trim(' ', '\u3000', '。', '.', '!', '！', '?', '？', ',', '，', ':', '：', ';', '；', '「', '」', '『', '』', '“', '”', '"');

        var suffixMatch = QuantitySuffixRegex.Match(cleaned);
        if (suffixMatch.Success)
        {
            quantity = ParseQuantity(suffixMatch.Groups["count"].Value);
            cleaned = cleaned[..suffixMatch.Index].Trim();
        }

        cleaned = LeadingArticleRegex.Replace(cleaned, string.Empty);
        return cleaned
            .Trim(' ', '\u3000', '。', '.', '!', '！', '?', '？', ',', '，', ':', '：', ';', '；', '「', '」', '『', '』', '“', '”', '"');
    }

    private static int ParseQuantity(string raw)
        => int.TryParse(raw, out var value) && value > 0 ? value : 0;
}
