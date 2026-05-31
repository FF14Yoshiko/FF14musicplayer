using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AllTimeSoundTrigger.ConfigurationModels;
using AllTimeSoundTrigger.Utilities;
using Dalamud.Plugin.Services;

namespace AllTimeSoundTrigger.Services;

public sealed class SfxPackService
{
    private const string ProfileEntryName = "profile.json";
    private const string SoundsPrefix = "sounds/";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IPluginLog log;

    public SfxPackService(IPluginLog log)
    {
        this.log = log;
        RootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncherCN",
            "pluginConfigs",
            "AllTimeSoundTrigger");
        ManagedSoundDirectory = Path.Combine(RootDirectory, "sounds");
        ImportSoundDirectory = Path.Combine(RootDirectory, "importedSounds");
        ExportDirectory = Path.Combine(RootDirectory, "exports");
    }

    public string RootDirectory { get; }

    public string ManagedSoundDirectory { get; }

    public string ImportSoundDirectory { get; }

    public string ExportDirectory { get; }

    public string BuildDefaultExportPath(string profileName)
    {
        Directory.CreateDirectory(ExportDirectory);
        var safeName = MakeSafeFileName(string.IsNullOrWhiteSpace(profileName) ? "音效分享包" : profileName.Trim());
        return Path.Combine(ExportDirectory, $"{safeName}.sfxpack");
    }

    public string CopySoundIntoManagedDirectory(string sourcePath)
    {
        var normalizedSource = FilePathText.Normalize(sourcePath);
        if (string.IsNullOrWhiteSpace(normalizedSource) || !File.Exists(normalizedSource))
            throw new FileNotFoundException("音效文件不存在。", normalizedSource);

        Directory.CreateDirectory(ManagedSoundDirectory);
        var fileName = MakeSafeFileName(Path.GetFileName(normalizedSource));
        var destinationPath = Path.Combine(ManagedSoundDirectory, fileName);
        var index = 2;
        while (File.Exists(destinationPath))
        {
            destinationPath = Path.Combine(
                ManagedSoundDirectory,
                $"{Path.GetFileNameWithoutExtension(fileName)}_{index}{Path.GetExtension(fileName)}");
            index++;
        }

        File.Copy(normalizedSource, destinationPath);
        return destinationPath;
    }

    public SfxPackExportResult Export(
        ProfileDefinition sourceProfile,
        IReadOnlyCollection<string> selectedGroupIds,
        IReadOnlyCollection<string> selectedRuleIds,
        SoundLibraryConfiguration soundLibrary,
        string packagePath,
        string readme)
    {
        var outputPath = EnsureSfxPackExtension(FilePathText.Normalize(packagePath));
        if (string.IsNullOrWhiteSpace(outputPath))
            return SfxPackExportResult.Fail("请先填写导出路径。");
        if (!Path.IsPathRooted(outputPath))
            outputPath = Path.Combine(ExportDirectory, outputPath);

        var exportProfile = CreateSelectedProfile(sourceProfile, selectedGroupIds, selectedRuleIds);
        if (!exportProfile.EnumerateRules().Any())
            return SfxPackExportResult.Fail("请至少勾选一个分组或规则。");

        var sourceToZipPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missingSounds = new List<string>();
        var exportedSounds = new List<(string SourcePath, string ZipPath)>();

        foreach (var action in exportProfile.EnumerateRules().SelectMany(rule => rule.Actions))
        {
            if (!action.Type.Equals("Sound", StringComparison.OrdinalIgnoreCase))
                continue;

            var sourcePath = ResolveSoundPath(action, soundLibrary);
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                missingSounds.Add(string.IsNullOrWhiteSpace(action.SoundId) ? action.FilePath : action.SoundId);
                continue;
            }

            sourcePath = Path.GetFullPath(sourcePath);
            if (!sourceToZipPath.TryGetValue(sourcePath, out var zipPath))
            {
                zipPath = BuildUniqueSoundZipPath(sourcePath, sourceToZipPath.Values);
                sourceToZipPath[sourcePath] = zipPath;
                exportedSounds.Add((sourcePath, zipPath));
            }

            action.SoundId = string.Empty;
            action.FilePath = zipPath;
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        Directory.CreateDirectory(string.IsNullOrWhiteSpace(outputDirectory) ? ExportDirectory : outputDirectory);
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create, Encoding.UTF8))
        {
            var profileEntry = archive.CreateEntry(ProfileEntryName, CompressionLevel.Optimal);
            using (var stream = profileEntry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(JsonSerializer.Serialize(exportProfile, SerializerOptions));
            }

            foreach (var sound in exportedSounds)
                archive.CreateEntryFromFile(sound.SourcePath, sound.ZipPath, CompressionLevel.Optimal);

            if (!string.IsNullOrWhiteSpace(readme))
            {
                var readmeEntry = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
                using var stream = readmeEntry.Open();
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(readme.Trim());
            }
        }

        log.Information(
            "[AllTimeSoundTrigger] Exported sfxpack {Path}: {GroupCount} groups, {RuleCount} rules, {SoundCount} sounds.",
            outputPath,
            exportProfile.Groups.Count,
            exportProfile.EnumerateRules().Count(),
            exportedSounds.Count);

        var message = missingSounds.Count == 0
            ? "导出完成。"
            : $"导出完成，但有 {missingSounds.Count} 个音效文件缺失。";
        return new SfxPackExportResult(true, message, outputPath, exportProfile.Groups.Count, exportProfile.EnumerateRules().Count(), exportedSounds.Count, missingSounds);
    }

    public SfxPackPreview Preview(string packagePath)
    {
        var inputPath = FilePathText.Normalize(packagePath);
        using var archive = ZipFile.OpenRead(inputPath);
        var profile = ReadProfile(archive);
        var readme = ReadOptionalText(archive, "README.txt");
        var soundEntries = archive.Entries
            .Where(entry => entry.FullName.StartsWith(SoundsPrefix, StringComparison.OrdinalIgnoreCase) && !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .ToArray();

        return new SfxPackPreview(
            inputPath,
            profile.Name,
            profile.Groups.Count,
            profile.EnumerateRules().Count(),
            soundEntries.Length,
            soundEntries.Sum(entry => entry.Length),
            readme);
    }

    public ProfileDefinition Import(string packagePath)
    {
        var inputPath = FilePathText.Normalize(packagePath);
        using var archive = ZipFile.OpenRead(inputPath);
        var profile = ReadProfile(archive);
        RegenerateIds(profile);

        var importDirectory = Path.Combine(
            ImportSoundDirectory,
            $"{MakeSafeFileName(Path.GetFileNameWithoutExtension(inputPath))}_{DateTime.Now:yyyyMMddHHmmss}");
        Directory.CreateDirectory(importDirectory);

        var extractedSounds = ExtractSounds(archive, importDirectory);
        foreach (var action in profile.EnumerateRules().SelectMany(rule => rule.Actions))
        {
            if (!action.Type.Equals("Sound", StringComparison.OrdinalIgnoreCase))
                continue;

            var relativePath = NormalizeZipPath(action.FilePath);
            if (extractedSounds.TryGetValue(relativePath, out var extractedPath))
                action.FilePath = extractedPath;

            action.SoundId = string.Empty;
        }

        profile.Normalize();
        log.Information(
            "[AllTimeSoundTrigger] Imported sfxpack {Path}: {GroupCount} groups, {RuleCount} rules.",
            inputPath,
            profile.Groups.Count,
            profile.EnumerateRules().Count());
        return profile;
    }

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

    private static string ResolveSoundPath(ActionDefinition action, SoundLibraryConfiguration soundLibrary)
    {
        if (!string.IsNullOrWhiteSpace(action.SoundId))
            return soundLibrary.FindById(action.SoundId)?.FilePath ?? string.Empty;

        return action.FilePath;
    }

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

    private static Dictionary<string, string> ExtractSounds(ZipArchive archive, string importDirectory)
    {
        var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var relativePath = NormalizeZipPath(entry.FullName);
            if (!relativePath.StartsWith(SoundsPrefix, StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith("/", StringComparison.Ordinal))
                continue;

            var localRelativePath = relativePath[SoundsPrefix.Length..];
            var destinationPath = Path.GetFullPath(Path.Combine(importDirectory, localRelativePath));
            var root = Path.GetFullPath(importDirectory);
            if (!root.EndsWith(Path.DirectorySeparatorChar))
                root += Path.DirectorySeparatorChar;
            if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"分享包里包含非法路径：{entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? importDirectory);
            entry.ExtractToFile(destinationPath, true);
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
        => (path ?? string.Empty).Replace('\\', '/').TrimStart('/');

    private static string MakeSafeFileName(string name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? "sfxpack" : name.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return value;
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
