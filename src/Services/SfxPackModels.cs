using System;
using System.Collections.Generic;
using System.Linq;
using AllTimeSoundTrigger.ConfigurationModels;

namespace AllTimeSoundTrigger.Services;
public sealed class SfxPackSoundLibrary
{
    public int Version { get; set; } = 1;

    public List<SfxPackSoundEntry> Sounds { get; set; } = [];

    public void Normalize()
    {
        if (Version <= 0)
            Version = 1;

        Sounds ??= [];
        for (var i = Sounds.Count - 1; i >= 0; i--)
        {
            if (Sounds[i] == null)
            {
                Sounds.RemoveAt(i);
                continue;
            }

            Sounds[i].Normalize();
            if (string.IsNullOrWhiteSpace(Sounds[i].Id) || string.IsNullOrWhiteSpace(Sounds[i].ZipPath))
                Sounds.RemoveAt(i);
        }
    }
}

public sealed class SfxPackSoundEntry
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string ZipPath { get; set; } = string.Empty;

    public float DefaultVolume { get; set; } = 1f;

    public int Priority { get; set; }

    public bool InterruptLowerPriority { get; set; } = true;

    public void Normalize()
    {
        Id = (Id ?? string.Empty).Trim();
        Name = (Name ?? string.Empty).Trim();
        ZipPath = (ZipPath ?? string.Empty).Replace('\\', '/').TrimStart('/');
        DefaultVolume = Math.Clamp(DefaultVolume, 0f, 1f);
        Priority = Math.Clamp(Priority, -100, 100);
    }
}

public sealed record SfxPackPreview(
    string PackagePath,
    string ProfileName,
    int GroupCount,
    int RuleCount,
    int SoundCount,
    long TotalSoundBytes,
    string Readme);

public sealed record SfxPackProfilePreview(
    SfxPackPreview Summary,
    ProfileDefinition Profile);

public sealed record SfxPackImportResult(
    ProfileDefinition Profile,
    IReadOnlyList<string> ImportedSoundIds,
    IReadOnlyList<string> ImportDirectories);

public sealed record SfxPackExportResult(
    bool Success,
    string Message,
    string PackagePath,
    int GroupCount,
    int RuleCount,
    int SoundCount,
    IReadOnlyList<string> MissingSounds)
{
    public static SfxPackExportResult Fail(string message)
        => new(false, message, string.Empty, 0, 0, 0, []);
}

public enum SfxPackSubmissionPreflightSeverity
{
    Info,
    Warning,
    Error
}

public sealed record SfxPackSubmissionPreflightIssue(
    SfxPackSubmissionPreflightSeverity Severity,
    string Title,
    string Message);

public sealed record SfxPackSubmissionPreflightResult(
    int RuleCount,
    int SoundCount,
    long TotalSoundBytes,
    IReadOnlyList<SfxPackSubmissionPreflightIssue> Issues)
{
    public int ErrorCount => Issues.Count(issue => issue.Severity == SfxPackSubmissionPreflightSeverity.Error);

    public int WarningCount => Issues.Count(issue => issue.Severity == SfxPackSubmissionPreflightSeverity.Warning);

    public bool HasErrors => ErrorCount > 0;
}
