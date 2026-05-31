namespace AllTimeSoundTrigger.Utilities;

public static class FilePathText
{
    public static string Normalize(string? filePath)
    {
        var normalized = (filePath ?? string.Empty).Trim();
        if (normalized.Length >= 2 && IsMatchingQuotePair(normalized[0], normalized[^1]))
            normalized = normalized[1..^1].Trim();

        return normalized;
    }

    private static bool IsMatchingQuotePair(char first, char last)
        => first == last && (first == '"' || first == '\'');
}
