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
    private static ProfileDefinition CreateSelectedProfile(
        ProfileDefinition sourceProfile,
        IReadOnlyCollection<string> selectedGroupIds,
        IReadOnlyCollection<string> selectedRuleIds)
    {
        var groupSet = new HashSet<string>(selectedGroupIds, StringComparer.Ordinal);
        var ruleSet = new HashSet<string>(selectedRuleIds, StringComparer.Ordinal);

        var profile = new ProfileDefinition
        {
            Id = sourceProfile.Id,
            Name = sourceProfile.Name,
            Groups = []
        };

        foreach (var group in sourceProfile.Groups)
        {
            var includeWholeGroup = groupSet.Contains(group.Id);
            var selectedRules = includeWholeGroup
                ? group.Rules
                : group.Rules.Where(rule => ruleSet.Contains(rule.Id)).ToList();
            if (selectedRules.Count == 0)
                continue;

            profile.Groups.Add(new RuleGroupDefinition
            {
                Id = group.Id,
                Name = group.Name,
                Rules = selectedRules.Select(Clone).ToList()
            });
        }

        profile.Normalize();
        return profile;
    }

    private static RuleDefinition Clone(RuleDefinition rule)
        => JsonSerializer.Deserialize<RuleDefinition>(JsonSerializer.Serialize(rule, SerializerOptions), SerializerOptions)
            ?? throw new InvalidOperationException("无法复制规则。");

    private static IReadOnlyList<SoundExportReference> ResolveSoundReferences(ActionDefinition action, SoundLibraryConfiguration soundLibrary)
    {
        var soundIds = GetActionSoundIds(action);
        if (soundIds.Count > 0)
        {
            return soundIds
                .Select(soundId =>
                {
                    var entry = soundLibrary.FindById(soundId);
                    return new SoundExportReference(
                        entry?.FilePath ?? string.Empty,
                        soundId,
                        soundId,
                        entry?.Name ?? soundId,
                        entry?.DefaultVolume ?? action.Volume,
                        entry?.Priority ?? action.Priority,
                        entry?.InterruptLowerPriority ?? action.InterruptLowerPriority);
                })
                .ToArray();
        }

        return GetActionFilePaths(action)
            .Select(path => new SoundExportReference(
                path,
                path,
                string.Empty,
                Path.GetFileNameWithoutExtension(path),
                action.Volume,
                action.Priority,
                action.InterruptLowerPriority))
            .ToArray();
    }

    private static IReadOnlyList<string> GetActionSoundIds(ActionDefinition action)
    {
        var soundIds = new List<string>();
        if (action.SoundIds != null)
        {
            soundIds.AddRange(action.SoundIds
                .Select(item => (item ?? string.Empty).Trim())
                .Where(item => item.Length > 0));
        }

        var soundId = (action.SoundId ?? string.Empty).Trim();
        if (soundId.Length > 0)
            soundIds.Insert(0, soundId);

        return soundIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> GetActionFilePaths(ActionDefinition action)
    {
        var paths = new List<string>();
        if (action.FilePaths != null)
        {
            paths.AddRange(action.FilePaths
                .Select(item => FilePathText.Normalize(item))
                .Where(item => item.Length > 0));
        }

        var filePath = FilePathText.Normalize(action.FilePath);
        if (filePath.Length > 0)
            paths.Insert(0, filePath);

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private readonly record struct SoundExportReference(
        string SourcePath,
        string DisplayName,
        string SoundId,
        string Name,
        float DefaultVolume,
        int Priority,
        bool InterruptLowerPriority);

    private static string BuildUniqueSoundZipPath(string sourcePath, IEnumerable<string> usedPaths)
    {
        var used = new HashSet<string>(usedPaths, StringComparer.OrdinalIgnoreCase);
        var fileName = MakeSafeFileName(Path.GetFileName(sourcePath));
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = $"sound{Path.GetExtension(sourcePath)}";

        var candidate = $"{SoundsPrefix}{fileName}";
        var index = 2;
        while (used.Contains(candidate))
        {
            candidate = $"{SoundsPrefix}{Path.GetFileNameWithoutExtension(fileName)}_{index}{Path.GetExtension(fileName)}";
            index++;
        }

        return candidate;
    }

    private static ProfileDefinition ReadProfile(ZipArchive archive)
    {
        var entry = archive.Entries.FirstOrDefault(item => item.FullName.Equals(ProfileEntryName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("分享包缺少 profile.json。");

        using var stream = entry.Open();
        var profile = JsonSerializer.Deserialize<ProfileDefinition>(stream, SerializerOptions)
            ?? throw new InvalidOperationException("profile.json 无法读取。");
        profile.Normalize();
        return profile;
    }

    private static string ReadOptionalText(ZipArchive archive, string entryName)
    {
        var entry = archive.Entries.FirstOrDefault(item => item.FullName.Equals(entryName, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return string.Empty;

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static SfxPackSoundLibrary ReadOptionalSoundLibrary(ZipArchive archive)
    {
        var entry = archive.Entries.FirstOrDefault(item => item.FullName.Equals(SoundLibraryEntryName, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return new SfxPackSoundLibrary();

        using var stream = entry.Open();
        var soundLibrary = JsonSerializer.Deserialize<SfxPackSoundLibrary>(stream, SerializerOptions)
            ?? new SfxPackSoundLibrary();
        soundLibrary.Normalize();
        return soundLibrary;
    }

    private static Dictionary<string, string> ImportSoundLibraryEntries(
        SfxPackSoundLibrary packSoundLibrary,
        IReadOnlyDictionary<string, string> extractedSounds,
        SoundLibraryConfiguration targetSoundLibrary)
    {
        var soundIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var reservedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var packSound in packSoundLibrary.Sounds)
        {
            var zipPath = NormalizeZipPath(packSound.ZipPath);
            if (zipPath.Length == 0 || !extractedSounds.TryGetValue(zipPath, out var extractedPath))
                continue;

            var importedId = BuildUniqueSoundLibraryId(packSound.Id, targetSoundLibrary, reservedIds);
            reservedIds.Add(importedId);
            soundIdMap[packSound.Id] = importedId;

            var entry = new SoundLibraryEntry
            {
                Id = importedId,
                Name = string.IsNullOrWhiteSpace(packSound.Name)
                    ? Path.GetFileNameWithoutExtension(extractedPath)
                    : packSound.Name,
                FilePath = extractedPath,
                DefaultVolume = packSound.DefaultVolume,
                Priority = packSound.Priority,
                InterruptLowerPriority = packSound.InterruptLowerPriority
            };
            entry.Normalize();
            targetSoundLibrary.Entries.Add(entry);
        }

        return soundIdMap;
    }

    private static void AddTextEntry(ZipArchive archive, string entryName, string text)
    {
        var existing = archive.GetEntry(entryName);
        existing?.Delete();

        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup after a failed export.
        }
    }

    private static string BuildDefaultSubmissionReadme(CommunitySubmissionManifest manifest)
        => $"""
           {manifest.Name}

           作者：{manifest.Author}

           {manifest.Description}

           投稿说明：
           这个 .sfxpack 是给「全时刻音效触发器」社区审核用的投稿包。
           生成后请发送到邮箱 1104449674@qq.com，或者发送到 QQ 群 659827727。
           审核通过后，作者会把它加入 Gitee 社区列表，其他用户才能在插件里一键安装。
           """.Trim();

    private static Dictionary<string, string> ExtractSounds(ZipArchive archive, string importDirectory)
    {
        var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var relativePath = NormalizeZipPath(entry.FullName);
            if (!relativePath.StartsWith(SoundsPrefix, StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith("/", StringComparison.Ordinal))
                continue;
            if (!SfxPackSecurity.IsSafeZipPath(relativePath))
                throw new InvalidOperationException($"分享包里包含非法路径：{entry.FullName}");

            var localRelativePath = relativePath[SoundsPrefix.Length..];
            var destinationPath = Path.GetFullPath(Path.Combine(importDirectory, localRelativePath));
            var root = Path.GetFullPath(importDirectory);
            if (!root.EndsWith(Path.DirectorySeparatorChar))
                root += Path.DirectorySeparatorChar;
            if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"分享包里包含非法路径：{entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? importDirectory);
            SfxPackSecurity.ExtractToFileLimited(entry, destinationPath, SfxPackSecurity.MaxSingleSoundBytes);
            extracted[relativePath] = destinationPath;
        }

        return extracted;
    }

    private static void RegenerateIds(ProfileDefinition profile)
    {
        profile.Id = Guid.NewGuid().ToString("N");
        foreach (var group in profile.Groups)
        {
            group.Id = Guid.NewGuid().ToString("N");
            foreach (var rule in group.Rules)
                rule.Id = Guid.NewGuid().ToString("N");
        }
    }

    private static string EnsureSfxPackExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return Path.GetExtension(path).Equals(".sfxpack", StringComparison.OrdinalIgnoreCase)
            ? path
            : Path.ChangeExtension(path, ".sfxpack");
    }

    private static string NormalizeZipPath(string path)
        => SfxPackSecurity.NormalizeZipPath(path);

    private static string BuildUniqueSoundLibraryId(
        string requestedId,
        SoundLibraryConfiguration soundLibrary,
        ISet<string> reservedIds)
    {
        var baseId = MakeSafeSoundId(requestedId);
        var candidate = baseId;
        var index = 2;
        while (reservedIds.Contains(candidate)
               || soundLibrary.Entries.Any(entry => entry.Id.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseId}_{index}";
            index++;
        }

        return candidate;
    }

    private static string BuildUniqueSoundId(string requestedId, IEnumerable<string> usedIds)
    {
        var used = new HashSet<string>(usedIds, StringComparer.OrdinalIgnoreCase);
        var baseId = MakeSafeSoundId(requestedId);
        var candidate = baseId;
        var index = 2;
        while (used.Contains(candidate))
        {
            candidate = $"{baseId}_{index}";
            index++;
        }

        return candidate;
    }

    private static string MakeSafeSoundId(string requestedId)
    {
        var value = string.IsNullOrWhiteSpace(requestedId)
            ? Guid.NewGuid().ToString("N")
            : requestedId.Trim();

        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        value = value.Replace('/', '_').Replace('\\', '_');
        return string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value;
    }

    private static string MakeSafeFileName(string name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? "sfxpack" : name.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return value;
    }
}
