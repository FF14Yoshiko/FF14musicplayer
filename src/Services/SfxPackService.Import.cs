using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using AllTimeSoundTrigger.Community;
using AllTimeSoundTrigger.ConfigurationModels;
using AllTimeSoundTrigger.Utilities;

namespace AllTimeSoundTrigger.Services;

public sealed partial class SfxPackService
{
    public SfxPackPreview Preview(string packagePath)
        => PreviewWithProfile(packagePath).Summary;

    public SfxPackProfilePreview PreviewWithProfile(string packagePath)
    {
        var inputPath = FilePathText.Normalize(packagePath);
        using var archive = ZipFile.OpenRead(inputPath);
        SfxPackSecurity.ValidateArchive(archive);
        var profile = ReadProfile(archive);
        var readme = ReadOptionalText(archive, "README.txt");
        var soundEntries = archive.Entries
            .Where(entry => entry.FullName.StartsWith(SoundsPrefix, StringComparison.OrdinalIgnoreCase) && !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .ToArray();

        var summary = new SfxPackPreview(
            inputPath,
            profile.Name,
            profile.Groups.Count,
            profile.EnumerateRules().Count(),
            soundEntries.Length,
            soundEntries.Sum(entry => entry.Length),
            readme);
        return new SfxPackProfilePreview(summary, profile);
    }

    public CommunitySubmissionManifest? TryReadSubmissionManifest(string packagePath)
    {
        var inputPath = FilePathText.Normalize(packagePath);
        using var archive = ZipFile.OpenRead(inputPath);
        SfxPackSecurity.ValidateArchive(archive);
        var entry = archive.Entries.FirstOrDefault(item => item.FullName.Equals(SubmissionEntryName, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return null;

        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize<CommunitySubmissionManifest>(stream, SerializerOptions);
        manifest?.Normalize();
        return manifest;
    }

    public ProfileDefinition Import(string packagePath, SoundLibraryConfiguration soundLibrary)
        => ImportWithDetails(packagePath, soundLibrary).Profile;

    public SfxPackImportResult ImportWithDetails(string packagePath, SoundLibraryConfiguration soundLibrary)
    {
        var inputPath = FilePathText.Normalize(packagePath);
        using var archive = ZipFile.OpenRead(inputPath);
        SfxPackSecurity.ValidateArchive(archive);
        var profile = ReadProfile(archive);
        RegenerateIds(profile);
        soundLibrary.Normalize();
        var importedSoundIds = new List<string>();

        var importDirectory = Path.Combine(
            ImportSoundDirectory,
            $"{MakeSafeFileName(Path.GetFileNameWithoutExtension(inputPath))}_{DateTime.Now:yyyyMMddHHmmss}");
        Directory.CreateDirectory(importDirectory);

        try
        {
            var extractedSounds = ExtractSounds(archive, importDirectory);
            var packSoundLibrary = ReadOptionalSoundLibrary(archive);
            if (packSoundLibrary.Sounds.Count > 0)
            {
                var soundIdMap = ImportSoundLibraryEntries(packSoundLibrary, extractedSounds, soundLibrary);
                importedSoundIds.AddRange(soundIdMap.Values);
                foreach (var action in profile.EnumerateRules().SelectMany(rule => rule.Actions))
                {
                    if (!action.Type.Equals("Sound", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var actionSoundIds = GetActionSoundIds(action)
                        .Select(soundId => soundIdMap.TryGetValue(soundId, out var importedSoundId) ? importedSoundId : soundId)
                        .Where(soundId => !string.IsNullOrWhiteSpace(soundId))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (actionSoundIds.Count == 0)
                        continue;

                    action.SoundId = actionSoundIds[0];
                    action.SoundIds = actionSoundIds.Count > 1 ? actionSoundIds : [];
                    action.FilePath = string.Empty;
                    action.FilePaths = [];
                }
            }
            else
            {
                foreach (var action in profile.EnumerateRules().SelectMany(rule => rule.Actions))
                {
                    if (!action.Type.Equals("Sound", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var importedPaths = new List<string>();
                    foreach (var path in GetActionFilePaths(action))
                    {
                        var relativePath = NormalizeZipPath(path);
                        importedPaths.Add(extractedSounds.TryGetValue(relativePath, out var extractedPath)
                            ? extractedPath
                            : path);
                    }

                    action.SoundId = string.Empty;
                    action.SoundIds = [];
                    action.FilePath = importedPaths.Count > 0 ? importedPaths[0] : action.FilePath;
                    action.FilePaths = importedPaths.Count > 1 ? importedPaths : [];
                }
            }

            profile.Normalize();
            log.Information(
                "[AllTimeSoundTrigger] Imported sfxpack {Path}: {GroupCount} groups, {RuleCount} rules.",
                inputPath,
                profile.Groups.Count,
                profile.EnumerateRules().Count());
            return new SfxPackImportResult(profile, importedSoundIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), [importDirectory]);
        }
        catch
        {
            TryDeleteImportedDirectory(importDirectory);
            throw;
        }
    }
}
