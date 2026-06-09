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

    public string SourcePackageUrl { get; set; } = string.Empty;

    public string ReadmeUrl { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public long DownloadCount { get; set; }

    public int GroupCount { get; set; }

    public int RuleCount { get; set; }

    public int SoundCount { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public string Category { get; set; } = string.Empty;

    public List<string> GameModes { get; set; } = [];

    public List<string> Jobs { get; set; } = [];

    public List<string> TriggerTypes { get; set; } = [];

    public string CompatiblePluginVersion { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;

    public string UpdatedAt { get; set; } = string.Empty;

    public string License { get; set; } = string.Empty;

    public string ContentWarning { get; set; } = string.Empty;

    public string Changelog { get; set; } = string.Empty;

    public string ChangelogUrl { get; set; } = string.Empty;

    public string ReleaseNotesUrl { get; set; } = string.Empty;

    public bool Deprecated { get; set; }

    public bool Hidden { get; set; }

    public string CoverSha256 { get; set; } = string.Empty;

    public void Normalize()
    {
        Id = NormalizeId(Id);
        Name = NormalizeText(Name);
        Author = NormalizeText(Author);
        Description = NormalizeText(Description);
        CoverUrl = NormalizeText(CoverUrl);
        PackageUrl = NormalizeText(PackageUrl);
        SourcePackageUrl = NormalizeText(SourcePackageUrl);
        ReadmeUrl = NormalizeText(ReadmeUrl);
        Version = NormalizeText(Version);
        Sha256 = NormalizeText(Sha256);
        Category = NormalizeText(Category);
        CompatiblePluginVersion = NormalizeText(CompatiblePluginVersion);
        CreatedAt = NormalizeText(CreatedAt);
        UpdatedAt = NormalizeText(UpdatedAt);
        License = NormalizeText(License);
        ContentWarning = NormalizeText(ContentWarning);
        Changelog = NormalizeText(Changelog);
        ChangelogUrl = NormalizeText(ChangelogUrl);
        ReleaseNotesUrl = NormalizeText(ReleaseNotesUrl);
        CoverSha256 = NormalizeText(CoverSha256);
        SizeBytes = Math.Max(0, SizeBytes);
        DownloadCount = Math.Max(0, DownloadCount);
        GroupCount = Math.Max(0, GroupCount);
        RuleCount = Math.Max(0, RuleCount);
        SoundCount = Math.Max(0, SoundCount);
        Tags = NormalizeList(Tags);
        GameModes = NormalizeList(GameModes);
        Jobs = NormalizeList(Jobs);
        TriggerTypes = NormalizeList(TriggerTypes);

        if (Name.Length == 0)
            Name = Id;
        if (Author.Length == 0)
            Author = "未知作者";
        if (Version.Length == 0)
            Version = "1.0.0";
        if (Category.Length == 0)
            Category = "未分类";
    }

    private static string NormalizeText(string? value)
        => (value ?? string.Empty).Trim();

    private static List<string> NormalizeList(IEnumerable<string>? values)
        => (values ?? [])
            .Select(NormalizeText)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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
