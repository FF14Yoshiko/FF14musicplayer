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

        var preflight = ValidateSubmissionPreflight(
            sourceProfile,
            selectedGroupIds,
            selectedRuleIds,
            soundLibrary,
            coverPath);
        if (preflight.HasErrors)
            return SfxPackExportResult.Fail(BuildSubmissionPreflightFailedMessage(preflight));

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
        var gameModes = manifest.GameModes.Count == 0 ? "未填写" : string.Join("，", manifest.GameModes);
        var jobs = manifest.Jobs.Count == 0 ? "未填写" : string.Join("，", manifest.Jobs);
        var triggerTypes = manifest.TriggerTypes.Count == 0 ? "未填写" : string.Join("，", manifest.TriggerTypes);
        var cover = string.IsNullOrWhiteSpace(FilePathText.Normalize(coverPath)) ? "无，如有配图请将图片文件一并发送。" : coverPath;
        var text = $"""
                   音效包名称：{manifest.Name}
                   包 ID：{manifest.Id}
                   作者署名：{manifest.Author}
                   简介：{manifest.Description}
                   标签：{tags}
                   分类：{manifest.Category}
                   玩法：{gameModes}
                   职业：{jobs}
                   触发器：{triggerTypes}
                   兼容插件版本：{manifest.CompatiblePluginVersion}
                   许可证：{manifest.License}
                   内容提醒：{manifest.ContentWarning}
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
}
