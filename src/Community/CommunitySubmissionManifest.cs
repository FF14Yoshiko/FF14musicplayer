using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AllTimeSoundTrigger.Community;

public sealed class CommunitySubmissionManifest
{
    public int Version { get; set; } = 1;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string PackageVersion { get; set; } = "1.0.0";

    public List<string> Tags { get; set; } = [];

    public string Readme { get; set; } = string.Empty;

    public string CoverEntryName { get; set; } = string.Empty;

    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.Now;

    public void Normalize()
    {
        if (Version <= 0)
            Version = 1;

        Name = NormalizeText(Name);
        Author = NormalizeText(Author);
        Description = NormalizeText(Description);
        PackageVersion = NormalizeText(PackageVersion);
        Readme = NormalizeText(Readme);
        CoverEntryName = NormalizeZipPath(CoverEntryName);
        Tags = (Tags ?? [])
            .Select(NormalizeText)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Id = NormalizeId(Id);
        if (Id.Length == 0)
            Id = GenerateId(Name);
        if (Name.Length == 0)
            Name = "未命名音效包";
        if (Author.Length == 0)
            Author = "未署名玩家";
        if (PackageVersion.Length == 0)
            PackageVersion = "1.0.0";
    }

    public static string GenerateId(string value)
    {
        var normalized = NormalizeId(value);
        return normalized.Length > 0
            ? normalized
            : $"user-pack-{DateTimeOffset.Now:yyyyMMddHHmmss}";
    }

    public static string NormalizeId(string? value)
    {
        var text = NormalizeText(value).ToLowerInvariant();
        if (text.Length == 0)
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        var lastWasDash = false;
        foreach (var c in text)
        {
            var safe = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            if (safe)
            {
                builder.Append(c);
                lastWasDash = false;
                continue;
            }

            if (lastWasDash)
                continue;

            builder.Append('-');
            lastWasDash = true;
        }

        return builder.ToString().Trim('-');
    }

    private static string NormalizeText(string? value)
        => (value ?? string.Empty).Trim();

    private static string NormalizeZipPath(string? path)
    {
        var value = NormalizeText(path).Replace('\\', '/').TrimStart('/');
        return value.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part == "..")
            ? string.Empty
            : value;
    }
}
