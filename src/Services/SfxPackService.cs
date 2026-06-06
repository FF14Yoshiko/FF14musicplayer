using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AllTimeSoundTrigger.Community;
using AllTimeSoundTrigger.ConfigurationModels;
using AllTimeSoundTrigger.Utilities;
using Dalamud.Plugin.Services;

namespace AllTimeSoundTrigger.Services;

public sealed class SfxPackService
{
    private const string ProfileEntryName = "profile.json";
    private const string SoundLibraryEntryName = "sound-library.json";
    private const string SubmissionEntryName = "submission.json";
    private const string SoundsPrefix = "sounds/";
    private const string CoversPrefix = "covers/";
    private const long MaxCoverBytes = 2L * 1024L * 1024L;

    private static readonly string[] AllowedCoverExtensions = [".png", ".jpg", ".jpeg", ".webp"];

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
        SubmissionDirectory = Path.Combine(RootDirectory, "submissions");
    }

    public string RootDirectory { get; }

    public string ManagedSoundDirectory { get; }

    public string ImportSoundDirectory { get; }

    public string ExportDirectory { get; }

    public string SubmissionDirectory { get; }

    public string BuildDefaultExportPath(string profileName)
    {
        Directory.CreateDirectory(ExportDirectory);
        var safeName = MakeSafeFileName(string.IsNullOrWhiteSpace(profileName) ? "音效分享包" : profileName.Trim());
        return Path.Combine(ExportDirectory, $"{safeName}.sfxpack");
    }

    public string BuildDefaultSubmissionPath(string packageName)
    {
        Directory.CreateDirectory(SubmissionDirectory);
        var safeName = MakeSafeFileName(string.IsNullOrWhiteSpace(packageName) ? "社区投稿包" : packageName.Trim());
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var directory = string.IsNullOrWhiteSpace(desktop) ? SubmissionDirectory : desktop;
        return Path.Combine(directory, $"{safeName}.sfxpack");
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
        var directPathToSoundId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var soundIdToPackageId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var packSoundLibrary = new SfxPackSoundLibrary();
        var missingSounds = new List<string>();
        var exportedSounds = new List<(string SourcePath, string ZipPath)>();

        foreach (var action in exportProfile.EnumerateRules().SelectMany(rule => rule.Actions))
        {
            if (!action.Type.Equals("Sound", StringComparison.OrdinalIgnoreCase))
                continue;

            var exportedActionSoundIds = new List<string>();
            foreach (var soundRef in ResolveSoundReferences(action, soundLibrary))
            {
                if (string.IsNullOrWhiteSpace(soundRef.SourcePath) || !File.Exists(soundRef.SourcePath))
                {
                    missingSounds.Add(soundRef.DisplayName);
                    continue;
                }

                var sourcePath = Path.GetFullPath(soundRef.SourcePath);
                if (!sourceToZipPath.TryGetValue(sourcePath, out var zipPath))
                {
                    zipPath = BuildUniqueSoundZipPath(sourcePath, sourceToZipPath.Values);
                    sourceToZipPath[sourcePath] = zipPath;
                    exportedSounds.Add((sourcePath, zipPath));
                }

                var packageSoundId = soundRef.SoundId;
                if (string.IsNullOrWhiteSpace(packageSoundId))
                {
                    if (!directPathToSoundId.TryGetValue(sourcePath, out packageSoundId))
                    {
                        packageSoundId = BuildUniqueSoundId(Path.GetFileNameWithoutExtension(sourcePath), packSoundLibrary.Sounds.Select(sound => sound.Id));
                        directPathToSoundId[sourcePath] = packageSoundId;
                    }
                }
                else
                {
                    var originalSoundId = packageSoundId;
                    if (!soundIdToPackageId.TryGetValue(originalSoundId, out packageSoundId))
                    {
                        packageSoundId = packSoundLibrary.Sounds.Any(sound => sound.Id.Equals(originalSoundId, StringComparison.OrdinalIgnoreCase))
                            ? BuildUniqueSoundId(originalSoundId, packSoundLibrary.Sounds.Select(sound => sound.Id))
                            : MakeSafeSoundId(originalSoundId);
                        soundIdToPackageId[originalSoundId] = packageSoundId;
                    }
                }

                if (!packSoundLibrary.Sounds.Any(sound => sound.Id.Equals(packageSoundId, StringComparison.OrdinalIgnoreCase)))
                {
                    packSoundLibrary.Sounds.Add(new SfxPackSoundEntry
                    {
                        Id = packageSoundId,
                        Name = string.IsNullOrWhiteSpace(soundRef.Name) ? Path.GetFileNameWithoutExtension(sourcePath) : soundRef.Name,
                        ZipPath = zipPath,
                        DefaultVolume = soundRef.DefaultVolume,
                        Priority = soundRef.Priority,
                        InterruptLowerPriority = soundRef.InterruptLowerPriority
                    });
                }

                exportedActionSoundIds.Add(packageSoundId);
            }

            if (exportedActionSoundIds.Count == 0)
                continue;

            action.SoundId = exportedActionSoundIds[0];
            action.SoundIds = exportedActionSoundIds.Count > 1 ? exportedActionSoundIds : [];
            action.FilePath = string.Empty;
            action.FilePaths = [];
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

            if (packSoundLibrary.Sounds.Count > 0)
                AddTextEntry(archive, SoundLibraryEntryName, JsonSerializer.Serialize(packSoundLibrary, SerializerOptions));

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

    public SfxPackExportResult ExportSubmission(
        ProfileDefinition sourceProfile,
        IReadOnlyCollection<string> selectedGroupIds,
        IReadOnlyCollection<string> selectedRuleIds,
        SoundLibraryConfiguration soundLibrary,
        string packagePath,
        CommunitySubmissionManifest manifest,
        string coverPath)
    {
        manifest.Normalize();
        if (string.IsNullOrWhiteSpace(manifest.Readme))
            manifest.Readme = BuildDefaultSubmissionReadme(manifest);

        var normalizedCoverPath = FilePathText.Normalize(coverPath);
        if (!string.IsNullOrWhiteSpace(normalizedCoverPath))
        {
            if (!File.Exists(normalizedCoverPath))
                return SfxPackExportResult.Fail("封面文件不存在。");

            var coverInfo = new FileInfo(normalizedCoverPath);
            if (coverInfo.Length > MaxCoverBytes)
                return SfxPackExportResult.Fail("封面超过 2MB，请换一张更小的图片。");

            var extension = Path.GetExtension(normalizedCoverPath).ToLowerInvariant();
            if (!AllowedCoverExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                return SfxPackExportResult.Fail("封面只支持 png、jpg、jpeg、webp。");

            manifest.CoverEntryName = $"{CoversPrefix}cover{extension}";
        }

        var result = Export(
            sourceProfile,
            selectedGroupIds,
            selectedRuleIds,
            soundLibrary,
            packagePath,
            manifest.Readme);
        if (!result.Success)
            return result;
        if (result.MissingSounds.Count > 0)
        {
            TryDeleteFile(result.PackagePath);
            return SfxPackExportResult.Fail($"投稿包生成失败：有 {result.MissingSounds.Count} 个音效文件缺失，请先在音效库里修好路径。");
        }

        using (var archive = ZipFile.Open(result.PackagePath, ZipArchiveMode.Update, Encoding.UTF8))
        {
            AddTextEntry(archive, SubmissionEntryName, JsonSerializer.Serialize(manifest, SerializerOptions));
            if (!string.IsNullOrWhiteSpace(manifest.CoverEntryName) && File.Exists(normalizedCoverPath))
                archive.CreateEntryFromFile(normalizedCoverPath, manifest.CoverEntryName, CompressionLevel.Optimal);
        }

        log.Information(
            "[AllTimeSoundTrigger] Exported community submission {Path}: {Name} by {Author}.",
            result.PackagePath,
            manifest.Name,
            manifest.Author);

        return result with { Message = "投稿包生成完成。" };
    }

    public string WriteSubmissionInfoText(string packagePath, CommunitySubmissionManifest manifest, string coverPath)
    {
        manifest.Normalize();
        var normalizedPackagePath = FilePathText.Normalize(packagePath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(normalizedPackagePath));
        if (string.IsNullOrWhiteSpace(directory))
            directory = SubmissionDirectory;

        Directory.CreateDirectory(directory);
        var packName = Path.GetFileNameWithoutExtension(normalizedPackagePath);
        if (string.IsNullOrWhiteSpace(packName))
            packName = MakeSafeFileName(manifest.Name);

        var infoPath = Path.Combine(directory, $"{packName}_投稿信息.txt");
        var tags = manifest.Tags.Count == 0 ? "未填写" : string.Join("，", manifest.Tags);
        var cover = string.IsNullOrWhiteSpace(FilePathText.Normalize(coverPath)) ? "无，如有配图请将图片文件一并发送。" : coverPath;
        var text = $"""
                   音效包名称：{manifest.Name}
                   包 ID：{manifest.Id}
                   作者署名：{manifest.Author}
                   简介：{manifest.Description}
                   标签：{tags}
                   版本：{manifest.PackageVersion}
                   投稿日期：{DateTime.Now:yyyy-MM-dd}
                   配图：{cover}
                   ---
                   投稿方式：
                   请将 .sfxpack、这份投稿信息.txt，以及可选配图发送到：
                   邮箱：1104449674@qq.com
                   QQ 群：659827727

                   审核通过后，你的音效包会出现在插件的社区列表中。
                   """;

        File.WriteAllText(infoPath, text, new UTF8Encoding(false));
        return infoPath;
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

    public CommunitySubmissionManifest? TryReadSubmissionManifest(string packagePath)
    {
        var inputPath = FilePathText.Normalize(packagePath);
        using var archive = ZipFile.OpenRead(inputPath);
        var entry = archive.Entries.FirstOrDefault(item => item.FullName.Equals(SubmissionEntryName, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return null;

        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize<CommunitySubmissionManifest>(stream, SerializerOptions);
        manifest?.Normalize();
        return manifest;
    }

    public ProfileDefinition Import(string packagePath, SoundLibraryConfiguration soundLibrary)
    {
        var inputPath = FilePathText.Normalize(packagePath);
        using var archive = ZipFile.OpenRead(inputPath);
        var profile = ReadProfile(archive);
        RegenerateIds(profile);
        soundLibrary.Normalize();

        var importDirectory = Path.Combine(
            ImportSoundDirectory,
            $"{MakeSafeFileName(Path.GetFileNameWithoutExtension(inputPath))}_{DateTime.Now:yyyyMMddHHmmss}");
        Directory.CreateDirectory(importDirectory);

        var extractedSounds = ExtractSounds(archive, importDirectory);
        var packSoundLibrary = ReadOptionalSoundLibrary(archive);
        if (packSoundLibrary.Sounds.Count > 0)
        {
            var soundIdMap = ImportSoundLibraryEntries(packSoundLibrary, extractedSounds, soundLibrary);
            foreach (var action in profile.EnumerateRules().SelectMany(rule => rule.Actions))
            {
                if (!action.Type.Equals("Sound", StringComparison.OrdinalIgnoreCase))
                    continue;

                var importedSoundIds = GetActionSoundIds(action)
                    .Select(soundId => soundIdMap.TryGetValue(soundId, out var importedSoundId) ? importedSoundId : soundId)
                    .Where(soundId => !string.IsNullOrWhiteSpace(soundId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (importedSoundIds.Count == 0)
                    continue;

                action.SoundId = importedSoundIds[0];
                action.SoundIds = importedSoundIds.Count > 1 ? importedSoundIds : [];
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
