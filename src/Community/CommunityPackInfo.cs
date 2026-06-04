using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AllTimeSoundTrigger.Community;

public sealed class CommunityPackInfo
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string CoverUrl { get; set; } = string.Empty;

    public string PackageUrl { get; set; } = string.Empty;

    public string ReadmeUrl { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public void Normalize()
    {
        Id = NormalizeId(Id);
        Name = NormalizeText(Name);
        Author = NormalizeText(Author);
        Description = NormalizeText(Description);
        CoverUrl = NormalizeText(CoverUrl);
        PackageUrl = NormalizeText(PackageUrl);
        ReadmeUrl = NormalizeText(ReadmeUrl);
        Version = NormalizeText(Version);
        Sha256 = NormalizeText(Sha256);
        SizeBytes = Math.Max(0, SizeBytes);
        Tags = (Tags ?? [])
            .Select(NormalizeText)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (Name.Length == 0)
            Name = Id;
        if (Author.Length == 0)
            Author = "未知作者";
        if (Version.Length == 0)
            Version = "1.0.0";
    }

    private static string NormalizeText(string? value)
        => (value ?? string.Empty).Trim();

    private static string NormalizeId(string? value)
    {
        var id = NormalizeText(value);
        if (id.Length == 0)
            return string.Empty;

        foreach (var invalid in Path.GetInvalidFileNameChars())
            id = id.Replace(invalid, '-');

        return id.Replace(' ', '-');
    }
}

