using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AllTimeSoundTrigger.ConfigurationModels;
using AllTimeSoundTrigger.Services;
using AllTimeSoundTrigger.Utilities;

namespace AllTimeSoundTrigger.Community;

public static class CommunitySubmissionPublisher
{
    private const string RawBaseUrl = "https://gitee.com/aikyan931023/ffxiv-sfx-community/raw/master";
    private const long MaxPackageBytes = 100L * 1024L * 1024L;
    private const long MaxCoverBytes = 2L * 1024L * 1024L;

    private static readonly string[] AllowedSoundExtensions = [".mp3", ".wav", ".ogg"];
    private static readonly string[] AllowedCoverExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static CommunityPublishResult ApproveAndPublish(CommunityPublishRequest request)
    {
        var repositoryPath = Path.GetFullPath(FilePathText.Normalize(request.RepositoryPath));
        var packagePath = Path.GetFullPath(FilePathText.Normalize(request.PackagePath));

        if (!Directory.Exists(repositoryPath) || !Directory.Exists(Path.Combine(repositoryPath, ".git")))
            throw new InvalidOperationException("Gitee 社区仓库路径不正确，找不到 .git。");
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("待审核 .sfxpack 不存在。", packagePath);
        if (new FileInfo(packagePath).Length > MaxPackageBytes)
            throw new InvalidOperationException("待审核包超过 100MB，已拒绝发布。");

        var validation = ValidatePackage(packagePath);
        var manifest = validation.Manifest ?? new CommunitySubmissionManifest();
        MergeRequestIntoManifest(request, manifest, validation.ProfileName);

        var packId = CommunitySubmissionManifest.NormalizeId(manifest.Id);
        if (packId.Length == 0)
            packId = CommunitySubmissionManifest.GenerateId(manifest.Name);

        EnsureCleanGitWorktree(repositoryPath);
        var gitOutput = new StringBuilder();
        gitOutput.AppendLine(RunGit(repositoryPath, "pull", "--rebase"));

        var packDirectory = Path.Combine(repositoryPath, "packs", packId);
        if (Directory.Exists(packDirectory) && !request.AllowOverwrite)
            throw new InvalidOperationException($"packs/{packId} 已存在。请换一个包 ID，或勾选允许覆盖。");

        Directory.CreateDirectory(packDirectory);
        var destinationPackagePath = Path.Combine(packDirectory, "package.sfxpack");
        File.Copy(packagePath, destinationPackagePath, true);

        var coverPath = CopyCover(request, validation, packagePath, packDirectory);
        var readmeText = BuildReadme(request, manifest, validation);
        File.WriteAllText(Path.Combine(packDirectory, "README.txt"), readmeText, new UTF8Encoding(false));

        var pack = BuildPackInfo(repositoryPath, packId, destinationPackagePath, coverPath, manifest, request, validation);
        UpdateIndex(repositoryPath, pack);

        gitOutput.AppendLine(RunGit(repositoryPath, "add", "index.json", $"packs/{packId}"));
        var status = RunGit(repositoryPath, "status", "--porcelain");
        if (string.IsNullOrWhiteSpace(status))
            return new CommunityPublishResult(true, "审核通过，但社区仓库没有新的改动。", packDirectory, pack, gitOutput.ToString());

        var commitMessage = $"Add community pack {packId}";
        gitOutput.AppendLine(RunGit(repositoryPath, "commit", "-m", commitMessage));
        if (request.PushToRemote)
            gitOutput.AppendLine(RunGit(repositoryPath, "-c", "credential.helper=wincred", "-c", "http.postBuffer=524288000", "-c", "http.version=HTTP/1.1", "push", "origin", "master"));

        return new CommunityPublishResult(
            true,
            request.PushToRemote ? "审核通过，已提交并推送到 Gitee。" : "审核通过，已提交到本地社区仓库。",
            packDirectory,
            pack,
            gitOutput.ToString());
    }

    public static CommunitySubmissionValidation ValidatePackage(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        SfxPackSecurity.ValidateArchive(archive);
        var entries = archive.Entries
            .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .ToArray();

        if (entries.All(entry => !entry.FullName.Equals("profile.json", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("缺少 profile.json。");

        foreach (var entry in entries)
            ValidateEntry(entry);

        var profile = ReadProfile(archive);
        var manifest = ReadManifest(archive);
        var readme = ReadTextEntry(archive, "README.txt");
        var coverEntryName = ResolveCoverEntryName(archive, manifest);

        return new CommunitySubmissionValidation(
            profile.Name,
            profile.Groups.Count,
            profile.EnumerateRules().Count(),
            entries.Count(entry => NormalizeZipPath(entry.FullName).StartsWith("sounds/", StringComparison.OrdinalIgnoreCase)),
            entries.Where(entry => NormalizeZipPath(entry.FullName).StartsWith("sounds/", StringComparison.OrdinalIgnoreCase)).Sum(entry => entry.Length),
            manifest,
            readme,
            coverEntryName);
    }

    private static void MergeRequestIntoManifest(CommunityPublishRequest request, CommunitySubmissionManifest manifest, string profileName)
    {
        manifest.Name = FirstNonEmpty(request.Name, manifest.Name, profileName, "未命名音效包");
        manifest.Author = FirstNonEmpty(request.Author, manifest.Author, "未署名玩家");
        manifest.Description = FirstNonEmpty(request.Description, manifest.Description, $"由玩家投稿的「{manifest.Name}」音效包。");
        manifest.PackageVersion = FirstNonEmpty(request.PackageVersion, manifest.PackageVersion, "1.0.0");
        manifest.Id = FirstNonEmpty(request.Id, manifest.Id, CommunitySubmissionManifest.GenerateId(manifest.Name));

        var tags = SplitTags(request.TagsText);
        if (tags.Count > 0)
            manifest.Tags = tags.ToList();
        if (manifest.Tags.Count == 0)
            manifest.Tags = ["玩家投稿"];

        manifest.Category = FirstNonEmpty(request.Category, manifest.Category, "玩家投稿");

        var gameModes = SplitTags(request.GameModesText);
        if (gameModes.Count > 0)
            manifest.GameModes = gameModes.ToList();
        var jobs = SplitTags(request.JobsText);
        if (jobs.Count > 0)
            manifest.Jobs = jobs.ToList();
        var triggerTypes = SplitTags(request.TriggerTypesText);
        if (triggerTypes.Count > 0)
            manifest.TriggerTypes = triggerTypes.ToList();
        if (manifest.GameModes.Count == 0)
            manifest.GameModes = ["通用"];

        manifest.CompatiblePluginVersion = FirstNonEmpty(request.CompatiblePluginVersion, manifest.CompatiblePluginVersion);
        manifest.License = FirstNonEmpty(request.License, manifest.License, "个人投稿，仅限插件社区内使用");
        manifest.ContentWarning = FirstNonEmpty(request.ContentWarning, manifest.ContentWarning);

        if (!string.IsNullOrWhiteSpace(request.Readme))
            manifest.Readme = request.Readme.Trim();

        manifest.Normalize();
    }

    private static void ValidateEntry(ZipArchiveEntry entry)
    {
        var path = NormalizeZipPath(entry.FullName);
        if (path.Length == 0
            || Path.IsPathRooted(path)
            || path.Contains(':')
            || path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part == ".."))
        {
            throw new InvalidOperationException($"压缩包内含非法路径：{entry.FullName}");
        }

        if (entry.Length > MaxPackageBytes)
            throw new InvalidOperationException($"压缩包内文件过大：{entry.FullName}");

        if (path.Equals("profile.json", StringComparison.OrdinalIgnoreCase)
            || path.Equals("README.txt", StringComparison.OrdinalIgnoreCase)
            || path.Equals("submission.json", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (path.StartsWith("sounds/", StringComparison.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(path);
            if (!AllowedSoundExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"音效文件格式不支持：{entry.FullName}");
            return;
        }

        if (path.StartsWith("covers/", StringComparison.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(path);
            if (!AllowedCoverExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"封面格式不支持：{entry.FullName}");
            if (entry.Length > MaxCoverBytes)
                throw new InvalidOperationException($"封面超过 2MB：{entry.FullName}");
            return;
        }

        throw new InvalidOperationException($"压缩包内含不允许的文件：{entry.FullName}");
    }

    private static string CopyCover(CommunityPublishRequest request, CommunitySubmissionValidation validation, string packagePath, string packDirectory)
    {
        var overrideCover = FilePathText.Normalize(request.CoverPath);
        if (!string.IsNullOrWhiteSpace(overrideCover))
        {
            if (!File.Exists(overrideCover))
                throw new FileNotFoundException("审核封面文件不存在。", overrideCover);

            var info = new FileInfo(overrideCover);
            if (info.Length > MaxCoverBytes)
                throw new InvalidOperationException("审核封面超过 2MB。");

            var extension = Path.GetExtension(overrideCover).ToLowerInvariant();
            if (!AllowedCoverExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("审核封面只支持 png、jpg、jpeg、webp。");

            var destination = Path.Combine(packDirectory, $"cover{extension}");
            File.Copy(overrideCover, destination, true);
            return destination;
        }

        if (string.IsNullOrWhiteSpace(validation.CoverEntryName))
            return string.Empty;

        using var archive = ZipFile.OpenRead(packagePath);
        var entry = archive.Entries.First(item => NormalizeZipPath(item.FullName).Equals(validation.CoverEntryName, StringComparison.OrdinalIgnoreCase));
        var embeddedExtension = Path.GetExtension(entry.FullName).ToLowerInvariant();
        var embeddedDestination = Path.Combine(packDirectory, $"cover{embeddedExtension}");
        SfxPackSecurity.ExtractToFileLimited(entry, embeddedDestination, MaxCoverBytes);
        return embeddedDestination;
    }

    private static CommunityPackInfo BuildPackInfo(
        string repositoryPath,
        string packId,
        string packagePath,
        string coverPath,
        CommunitySubmissionManifest manifest,
        CommunityPublishRequest request,
        CommunitySubmissionValidation validation)
    {
        var relativePackage = ToRepoRelativePath(repositoryPath, packagePath);
        var packageUrl = $"{RawBaseUrl}/{relativePackage}";
        var relativeReadme = $"packs/{packId}/README.txt";
        var pack = new CommunityPackInfo
        {
            Id = packId,
            Name = manifest.Name,
            Author = manifest.Author,
            Description = manifest.Description,
            PackageUrl = packageUrl,
            SourcePackageUrl = packageUrl,
            ReadmeUrl = $"{RawBaseUrl}/{relativeReadme}",
            Version = manifest.PackageVersion,
            SizeBytes = new FileInfo(packagePath).Length,
            Sha256 = ComputeSha256(packagePath),
            GroupCount = validation.GroupCount,
            RuleCount = validation.RuleCount,
            SoundCount = validation.SoundCount,
            Tags = manifest.Tags.ToList(),
            Category = manifest.Category,
            GameModes = manifest.GameModes.ToList(),
            Jobs = manifest.Jobs.ToList(),
            TriggerTypes = manifest.TriggerTypes.ToList(),
            CompatiblePluginVersion = manifest.CompatiblePluginVersion,
            License = manifest.License,
            ContentWarning = manifest.ContentWarning,
            Changelog = request.Changelog,
            ChangelogUrl = request.ChangelogUrl,
            ReleaseNotesUrl = request.ChangelogUrl,
            Deprecated = request.Deprecated,
            Hidden = request.Hidden
        };

        if (!string.IsNullOrWhiteSpace(coverPath))
        {
            pack.CoverUrl = $"{RawBaseUrl}/{ToRepoRelativePath(repositoryPath, coverPath)}";
            pack.CoverSha256 = ComputeSha256(coverPath);
        }

        pack.Normalize();
        return pack;
    }

    private static void UpdateIndex(string repositoryPath, CommunityPackInfo pack)
    {
        var indexPath = Path.Combine(repositoryPath, "index.json");
        var index = File.Exists(indexPath)
            ? JsonSerializer.Deserialize<CommunityPackIndex>(File.ReadAllText(indexPath), SerializerOptions) ?? new CommunityPackIndex()
            : new CommunityPackIndex();

        var now = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
        var existing = index.Packs.FirstOrDefault(item => item.Id.Equals(pack.Id, StringComparison.OrdinalIgnoreCase));
        pack.CreatedAt = FirstNonEmpty(pack.CreatedAt, existing?.CreatedAt ?? string.Empty, now);
        pack.DownloadCount = Math.Max(pack.DownloadCount, existing?.DownloadCount ?? 0);
        pack.Changelog = FirstNonEmpty(pack.Changelog, existing?.Changelog ?? string.Empty);
        pack.ChangelogUrl = FirstNonEmpty(pack.ChangelogUrl, existing?.ChangelogUrl ?? string.Empty);
        pack.ReleaseNotesUrl = FirstNonEmpty(pack.ReleaseNotesUrl, existing?.ReleaseNotesUrl ?? string.Empty, pack.ChangelogUrl);
        if (existing != null
            && !string.IsNullOrWhiteSpace(existing.SourcePackageUrl)
            && !string.IsNullOrWhiteSpace(existing.PackageUrl)
            && !existing.PackageUrl.Equals(existing.SourcePackageUrl, StringComparison.OrdinalIgnoreCase))
        {
            pack.PackageUrl = existing.PackageUrl;
        }

        pack.UpdatedAt = now;
        index.Version = Math.Max(index.Version, 2);
        index.Packs.RemoveAll(item => item.Id.Equals(pack.Id, StringComparison.OrdinalIgnoreCase));
        index.Packs.Add(pack);
        index.UpdatedAt = now;
        index.Normalize();
        File.WriteAllText(indexPath, JsonSerializer.Serialize(index, SerializerOptions), new UTF8Encoding(false));
    }

    private static string BuildReadme(CommunityPublishRequest request, CommunitySubmissionManifest manifest, CommunitySubmissionValidation validation)
    {
        var readme = FirstNonEmpty(request.Readme, manifest.Readme, validation.Readme);
        if (readme.Length > 0)
            return readme;

        return $"""
               {manifest.Name}

               作者：{manifest.Author}

               {manifest.Description}

               内容：
               - 分组：{validation.GroupCount}
               - 规则：{validation.RuleCount}
               - 音效：{validation.SoundCount}

               安装方式：
               在「全时刻音效触发器」的社区页面点击安装即可。
               """.Trim();
    }

    private static ProfileDefinition ReadProfile(ZipArchive archive)
    {
        var entry = archive.Entries.First(item => item.FullName.Equals("profile.json", StringComparison.OrdinalIgnoreCase));
        using var stream = entry.Open();
        var profile = JsonSerializer.Deserialize<ProfileDefinition>(stream, SerializerOptions)
            ?? throw new InvalidOperationException("profile.json 无法读取。");
        profile.Normalize();
        return profile;
    }

    private static CommunitySubmissionManifest? ReadManifest(ZipArchive archive)
    {
        var entry = archive.Entries.FirstOrDefault(item => item.FullName.Equals("submission.json", StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return null;

        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize<CommunitySubmissionManifest>(stream, SerializerOptions);
        manifest?.Normalize();
        return manifest;
    }

    private static string ReadTextEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.Entries.FirstOrDefault(item => item.FullName.Equals(entryName, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return string.Empty;

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd().Trim();
    }

    private static string ResolveCoverEntryName(ZipArchive archive, CommunitySubmissionManifest? manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest?.CoverEntryName))
        {
            var manifestPath = NormalizeZipPath(manifest.CoverEntryName);
            if (archive.Entries.Any(entry => NormalizeZipPath(entry.FullName).Equals(manifestPath, StringComparison.OrdinalIgnoreCase)))
                return manifestPath;
        }

        return archive.Entries
            .Select(entry => NormalizeZipPath(entry.FullName))
            .FirstOrDefault(path => path.StartsWith("covers/", StringComparison.OrdinalIgnoreCase)
                                    && AllowedCoverExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            ?? string.Empty;
    }

    private static void EnsureCleanGitWorktree(string repositoryPath)
    {
        var status = RunGit(repositoryPath, "status", "--porcelain");
        if (!string.IsNullOrWhiteSpace(status))
            throw new InvalidOperationException("社区仓库存在未提交修改，请先处理后再发布。");
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(120000))
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
                // Process cleanup only.
            }

            throw new TimeoutException("Git 命令超时。");
        }

        var combined = (output + error).Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git 命令失败：git {string.Join(' ', arguments)}\n{combined}");

        return combined;
    }

    private static IReadOnlyList<string> SplitTags(string tagsText)
        => (tagsText ?? string.Empty)
            .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    private static string FirstNonEmpty(params string?[] values)
        => values.Select(value => (value ?? string.Empty).Trim()).FirstOrDefault(value => value.Length > 0) ?? string.Empty;

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ToRepoRelativePath(string repositoryPath, string path)
        => Path.GetRelativePath(repositoryPath, path).Replace('\\', '/');

    private static string NormalizeZipPath(string path)
        => (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
}

public sealed record CommunitySubmissionValidation(
    string ProfileName,
    int GroupCount,
    int RuleCount,
    int SoundCount,
    long TotalSoundBytes,
    CommunitySubmissionManifest? Manifest,
    string Readme,
    string CoverEntryName);
