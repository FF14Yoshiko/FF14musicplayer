using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

var options = CheckOptions.Parse(args);
var failures = new List<string>();
var repoRoot = Path.GetFullPath(options.RepoRoot);

CheckVersionSync(repoRoot, failures);
CheckLocalSfxPacks(repoRoot, failures);
await CheckCommunityIndexAsync(options, failures);

if (failures.Count > 0)
{
    Console.Error.WriteLine("Repository checks failed:");
    foreach (var failure in failures)
        Console.Error.WriteLine($"- {failure}");
    return 1;
}

Console.WriteLine("Repository checks passed.");
return 0;

static void CheckVersionSync(string repoRoot, List<string> failures)
{
    var csprojPath = Path.Combine(repoRoot, "AllTimeSoundTrigger.csproj");
    var pluginPath = Path.Combine(repoRoot, "plugin.json");
    var pluginMasterPath = Path.Combine(repoRoot, "pluginmaster.json");

    var csproj = XDocument.Load(csprojPath);
    var properties = csproj.Root?
        .Elements("PropertyGroup")
        .Elements()
        .GroupBy(element => element.Name.LocalName)
        .ToDictionary(group => group.Key, group => group.First().Value.Trim(), StringComparer.OrdinalIgnoreCase)
        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    var plugin = JsonNode.Parse(File.ReadAllText(pluginPath))?.AsObject()
        ?? throw new InvalidOperationException("plugin.json could not be parsed.");
    var pluginMaster = JsonNode.Parse(File.ReadAllText(pluginMasterPath))?.AsArray()
        ?? throw new InvalidOperationException("pluginmaster.json could not be parsed.");
    if (pluginMaster.Count != 1 || pluginMaster[0] is not JsonObject pluginMasterEntry)
    {
        failures.Add("pluginmaster.json must contain exactly one plugin entry.");
        return;
    }

    var assemblyVersion = GetProperty(properties, "AssemblyVersion");
    var fileVersion = GetProperty(properties, "FileVersion");
    var packageVersion = GetProperty(properties, "Version");
    var pluginVersion = GetJsonString(plugin, "AssemblyVersion");
    var masterVersion = GetJsonString(pluginMasterEntry, "AssemblyVersion");

    AddMismatch(failures, "csproj AssemblyVersion vs FileVersion", assemblyVersion, fileVersion);
    AddMismatch(failures, "csproj AssemblyVersion vs plugin.json AssemblyVersion", assemblyVersion, pluginVersion);
    AddMismatch(failures, "plugin.json AssemblyVersion vs pluginmaster.json AssemblyVersion", pluginVersion, masterVersion);
    if (!assemblyVersion.StartsWith($"{packageVersion}.", StringComparison.Ordinal)
        && !assemblyVersion.Equals(packageVersion, StringComparison.Ordinal))
    {
        failures.Add($"csproj Version ({packageVersion}) does not line up with AssemblyVersion ({assemblyVersion}).");
    }

    AddMismatch(failures, "csproj AssemblyName vs plugin InternalName", GetProperty(properties, "AssemblyName"), GetJsonString(plugin, "InternalName"));
    AddMismatch(failures, "csproj Authors vs plugin Author", GetProperty(properties, "Authors"), GetJsonString(plugin, "Author"));
    AddMismatch(failures, "csproj AssemblyTitle vs plugin Name", GetProperty(properties, "AssemblyTitle"), GetJsonString(plugin, "Name"));

    foreach (var property in plugin)
    {
        if (pluginMasterEntry.TryGetPropertyValue(property.Key, out var masterValue)
            && !JsonNode.DeepEquals(property.Value, masterValue))
        {
            failures.Add($"plugin.json and pluginmaster.json differ for '{property.Key}'.");
        }
    }
}

static void CheckLocalSfxPacks(string repoRoot, List<string> failures)
{
    var packagePaths = Directory.EnumerateFiles(repoRoot, "*.sfxpack", SearchOption.AllDirectories)
        .Where(path => !IsGeneratedPath(repoRoot, path))
        .ToArray();

    foreach (var packagePath in packagePaths)
    {
        try
        {
            ValidateSfxPack(packagePath);
        }
        catch (Exception ex)
        {
            failures.Add($"{Path.GetRelativePath(repoRoot, packagePath)} failed .sfxpack validation: {ex.Message}");
        }
    }
}

static async Task CheckCommunityIndexAsync(CheckOptions options, List<string> failures)
{
    if (options.SkipCommunityIndex)
    {
        if (options.Fix)
            failures.Add("--fix cannot be combined with --skip-community-index.");
        else
            Console.WriteLine("Community index check skipped.");
        return;
    }

    if (string.IsNullOrWhiteSpace(options.CommunityIndexPath)
        && string.IsNullOrWhiteSpace(options.CommunityIndexUrl))
    {
        if (options.Fix)
            failures.Add("--fix requires --community-index-path.");
        else
            Console.WriteLine("Community index check skipped because no index path or URL was provided.");
        return;
    }

    IndexSource source;
    try
    {
        source = await LoadIndexSourceAsync(options);
    }
    catch (Exception ex)
    {
        failures.Add($"community index could not be loaded: {ex.Message}");
        return;
    }

    JsonObject indexJson;
    try
    {
        indexJson = JsonNode.Parse(source.Json)?.AsObject()
                    ?? throw new InvalidOperationException("root JSON value must be an object.");
    }
    catch (Exception ex)
    {
        failures.Add($"community index JSON is invalid: {ex.Message}");
        return;
    }

    if (options.Fix)
        await FixCommunityIndexAsync(options, source, indexJson, failures);

    CheckCommunityIndexShape(indexJson, failures);

    CommunityPackIndex index;
    try
    {
        index = indexJson.Deserialize<CommunityPackIndex>(RepositoryCheckJson.Options)
                ?? new CommunityPackIndex();
    }
    catch (Exception ex)
    {
        failures.Add($"community index JSON is invalid after normalization: {ex.Message}");
        return;
    }

    var duplicateIds = index.Packs
        .GroupBy(pack => pack.Id, StringComparer.OrdinalIgnoreCase)
        .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
        .Select(group => group.Key)
        .ToArray();
    foreach (var duplicateId in duplicateIds)
        failures.Add($"community index has duplicate pack id '{duplicateId}'.");

    var tempDirectory = Path.Combine(Path.GetTempPath(), $"ats-community-index-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDirectory);
    try
    {
        foreach (var pack in index.Packs)
            await CheckCommunityPackAsync(pack, source, tempDirectory, failures);
    }
    finally
    {
        TryDeleteDirectory(tempDirectory);
    }
}

static async Task FixCommunityIndexAsync(
    CheckOptions options,
    IndexSource source,
    JsonObject indexJson,
    List<string> failures)
{
    if (string.IsNullOrWhiteSpace(options.CommunityIndexPath))
    {
        failures.Add("--fix requires --community-index-path so the repaired index can be written back.");
        return;
    }

    if (string.IsNullOrWhiteSpace(source.BaseDirectory))
    {
        failures.Add("--fix requires a local community index path.");
        return;
    }

    var fixes = new List<string>();
    var changed = false;
    var now = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
    var defaultCompatibleVersion = ResolveDefaultPluginVersion(Path.GetFullPath(options.RepoRoot));

    if (GetJsonInt(indexJson, "Version") < 2)
        changed |= SetJsonNumber(indexJson, "Version", 2, fixes, "index Version");

    var packs = GetJsonArray(indexJson, "Packs");
    if (packs == null)
    {
        packs = [];
        SetJsonArray(indexJson, "Packs", packs, fixes, "index Packs");
        changed = true;
    }

    var tempDirectory = Path.Combine(Path.GetTempPath(), $"ats-community-fix-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDirectory);
    try
    {
        foreach (var packNode in packs)
        {
            if (packNode is not JsonObject packJson)
            {
                failures.Add("community index contains a pack entry that is not an object.");
                continue;
            }

            changed |= await FixCommunityPackAsync(
                packJson,
                source,
                tempDirectory,
                now,
                defaultCompatibleVersion,
                fixes,
                failures);
        }
    }
    finally
    {
        TryDeleteDirectory(tempDirectory);
    }

    if (changed || string.IsNullOrWhiteSpace(GetJsonString(indexJson, "UpdatedAt")))
        changed |= SetJsonString(indexJson, "UpdatedAt", now, fixes, "index UpdatedAt");

    if (!changed)
    {
        Console.WriteLine("Community index fix: no changes needed.");
        return;
    }

    var indexPath = Path.GetFullPath(options.CommunityIndexPath);
    await File.WriteAllTextAsync(
        indexPath,
        indexJson.ToJsonString(RepositoryCheckJson.Options) + Environment.NewLine,
        new UTF8Encoding(false));

    Console.WriteLine($"Community index fixed: {indexPath}");
    foreach (var fix in fixes)
        Console.WriteLine($"  - {fix}");
}

static async Task<bool> FixCommunityPackAsync(
    JsonObject packJson,
    IndexSource source,
    string tempDirectory,
    string now,
    string defaultCompatibleVersion,
    List<string> fixes,
    List<string> failures)
{
    var label = FirstNonEmpty(GetJsonString(packJson, "Id"), GetJsonString(packJson, "Name"), "(missing id)");
    var changed = false;
    var metadataChanged = false;

    try
    {
        var packageUrl = GetJsonString(packJson, "PackageUrl");
        if (string.IsNullOrWhiteSpace(packageUrl))
        {
            failures.Add($"community pack '{label}' cannot be fixed because PackageUrl is empty.");
            return false;
        }

        var package = await MaterializeResourceAsync(
            packageUrl,
            source,
            tempDirectory,
            $"{SafeFileName(label)}.sfxpack",
            RepositoryCheckSecurity.MaxPackageBytes,
            expectedSize: 0);
        ValidateSfxPack(package.Path);

        var inspection = InspectSfxPack(package.Path);
        var manifest = inspection.Manifest;

        metadataChanged |= SetJsonNumber(packJson, "SizeBytes", package.Length, fixes, $"{label} SizeBytes");
        metadataChanged |= SetJsonString(packJson, "Sha256", package.Sha256, fixes, $"{label} Sha256");
        metadataChanged |= SetJsonNumber(packJson, "GroupCount", inspection.GroupCount, fixes, $"{label} GroupCount");
        metadataChanged |= SetJsonNumber(packJson, "RuleCount", inspection.RuleCount, fixes, $"{label} RuleCount");
        metadataChanged |= SetJsonNumber(packJson, "SoundCount", inspection.SoundCount, fixes, $"{label} SoundCount");

        metadataChanged |= SetJsonStringIfMissing(packJson, "SourcePackageUrl", packageUrl, fixes, $"{label} SourcePackageUrl");
        metadataChanged |= SetJsonStringIfMissing(packJson, "Name", FirstNonEmpty(manifest?.Name, label), fixes, $"{label} Name");
        metadataChanged |= SetJsonStringIfMissing(packJson, "Author", FirstNonEmpty(manifest?.Author, "未知作者"), fixes, $"{label} Author");
        metadataChanged |= SetJsonStringIfMissing(packJson, "Description", FirstNonEmpty(manifest?.Description, $"{label} community sound pack."), fixes, $"{label} Description");
        metadataChanged |= SetJsonStringIfMissing(packJson, "Version", FirstNonEmpty(manifest?.PackageVersion, "1.0.0"), fixes, $"{label} Version");
        metadataChanged |= SetJsonNumberIfMissing(packJson, "DownloadCount", 0, fixes, $"{label} DownloadCount");
        metadataChanged |= SetJsonArrayIfMissingOrEmpty(packJson, "Tags", FirstNonEmptyList(manifest?.Tags, ["玩家投稿"]), fixes, $"{label} Tags");
        metadataChanged |= SetJsonStringIfMissing(packJson, "Category", FirstNonEmpty(manifest?.Category, "玩家投稿"), fixes, $"{label} Category");
        metadataChanged |= SetJsonArrayIfMissingOrEmpty(packJson, "GameModes", FirstNonEmptyList(manifest?.GameModes, ["通用"]), fixes, $"{label} GameModes");
        metadataChanged |= SetJsonArrayIfMissing(packJson, "Jobs", manifest?.Jobs ?? [], fixes, $"{label} Jobs");
        metadataChanged |= SetJsonArrayIfMissingOrEmpty(packJson, "TriggerTypes", FirstNonEmptyList(manifest?.TriggerTypes, inspection.TriggerTypes), fixes, $"{label} TriggerTypes");
        metadataChanged |= SetJsonStringIfMissing(packJson, "CompatiblePluginVersion", FirstNonEmpty(manifest?.CompatiblePluginVersion, defaultCompatibleVersion), fixes, $"{label} CompatiblePluginVersion");
        metadataChanged |= SetJsonStringIfMissing(packJson, "License", FirstNonEmpty(manifest?.License, "个人投稿，仅限插件社区内使用"), fixes, $"{label} License");
        metadataChanged |= SetJsonStringIfMissing(packJson, "ContentWarning", manifest?.ContentWarning ?? string.Empty, fixes, $"{label} ContentWarning");
        metadataChanged |= SetJsonStringIfMissing(packJson, "Changelog", string.Empty, fixes, $"{label} Changelog");
        var releaseNotesUrl = FirstNonEmpty(GetJsonString(packJson, "ReleaseNotesUrl"), GetJsonString(packJson, "ChangelogUrl"));
        metadataChanged |= SetJsonStringIfMissing(packJson, "ChangelogUrl", releaseNotesUrl, fixes, $"{label} ChangelogUrl");
        metadataChanged |= SetJsonStringIfMissing(packJson, "ReleaseNotesUrl", releaseNotesUrl, fixes, $"{label} ReleaseNotesUrl");
        metadataChanged |= SetJsonBoolIfMissing(packJson, "Deprecated", false, fixes, $"{label} Deprecated");
        metadataChanged |= SetJsonBoolIfMissing(packJson, "Hidden", false, fixes, $"{label} Hidden");

        metadataChanged |= await EnsureReadmeAsync(packJson, source, package, inspection, fixes, label);
        metadataChanged |= await EnsureCoverAsync(packJson, source, package, inspection, fixes, label);

        var existingUpdatedAt = GetJsonString(packJson, "UpdatedAt");
        metadataChanged |= SetJsonStringIfMissing(packJson, "CreatedAt", FirstNonEmpty(existingUpdatedAt, now), fixes, $"{label} CreatedAt");
        if (metadataChanged || string.IsNullOrWhiteSpace(existingUpdatedAt))
            changed |= SetJsonString(packJson, "UpdatedAt", now, fixes, $"{label} UpdatedAt");

        changed |= metadataChanged;
    }
    catch (Exception ex)
    {
        failures.Add($"community pack '{label}' could not be fixed: {ex.Message}");
    }

    return changed;
}

static async Task<bool> EnsureReadmeAsync(
    JsonObject packJson,
    IndexSource source,
    MaterializedFile package,
    PackageInspection inspection,
    List<string> fixes,
    string label)
{
    var readmeUrl = GetJsonString(packJson, "ReadmeUrl");
    if (!string.IsNullOrWhiteSpace(readmeUrl))
        return false;

    var localReadmePath = TryFindSiblingFile(package.Path, "README.txt")
        ?? TryResolvePackSibling(source, GetJsonString(packJson, "Id"), "README.txt");
    if (localReadmePath == null && !package.IsTemporary)
    {
        localReadmePath = Path.Combine(Path.GetDirectoryName(package.Path) ?? string.Empty, "README.txt");
        var readmeText = FirstNonEmpty(inspection.Readme, BuildGeneratedReadme(packJson, inspection));
        await File.WriteAllTextAsync(localReadmePath, readmeText + Environment.NewLine, new UTF8Encoding(false));
        fixes.Add($"{label} README.txt created");
    }

    if (localReadmePath == null || !File.Exists(localReadmePath))
        return false;

    return SetJsonString(
        packJson,
        "ReadmeUrl",
        BuildSiblingResourceValue(GetJsonString(packJson, "PackageUrl"), Path.GetFileName(localReadmePath)),
        fixes,
        $"{label} ReadmeUrl");
}

static async Task<bool> EnsureCoverAsync(
    JsonObject packJson,
    IndexSource source,
    MaterializedFile package,
    PackageInspection inspection,
    List<string> fixes,
    string label)
{
    var changed = false;
    var coverUrl = GetJsonString(packJson, "CoverUrl");
    changed |= SetJsonStringIfMissing(packJson, "CoverUrl", string.Empty, fixes, $"{label} CoverUrl");
    var coverPath = string.Empty;
    if (!string.IsNullOrWhiteSpace(coverUrl) && TryResolveLocalPath(coverUrl, source, out var resolvedCover))
    {
        coverPath = resolvedCover;
    }
    else if (string.IsNullOrWhiteSpace(coverUrl))
    {
        coverPath = TryFindSiblingCover(package.Path)
                    ?? TryResolvePackCover(source, GetJsonString(packJson, "Id"))
                    ?? string.Empty;

        if (coverPath.Length == 0
            && inspection.CoverEntryName.Length > 0
            && !package.IsTemporary)
        {
            var extension = Path.GetExtension(inspection.CoverEntryName).ToLowerInvariant();
            coverPath = Path.Combine(Path.GetDirectoryName(package.Path) ?? string.Empty, $"cover{extension}");
            ExtractCover(package.Path, inspection.CoverEntryName, coverPath);
            fixes.Add($"{label} cover extracted");
        }

        if (coverPath.Length > 0)
        {
            changed |= SetJsonString(
                packJson,
                "CoverUrl",
                BuildSiblingResourceValue(GetJsonString(packJson, "PackageUrl"), Path.GetFileName(coverPath)),
                fixes,
                $"{label} CoverUrl");
        }
    }

    if (coverPath.Length == 0 || !File.Exists(coverPath))
    {
        changed |= SetJsonStringIfMissing(packJson, "CoverSha256", string.Empty, fixes, $"{label} CoverSha256");
        return changed;
    }

    var coverHash = await ComputeFileSha256Async(coverPath);
    changed |= SetJsonString(packJson, "CoverSha256", coverHash, fixes, $"{label} CoverSha256");
    return changed;
}

static void CheckCommunityIndexShape(JsonObject indexJson, List<string> failures)
{
    if (GetJsonInt(indexJson, "Version") < 2)
        failures.Add("community index Version must be 2 or greater.");
    if (string.IsNullOrWhiteSpace(GetJsonString(indexJson, "UpdatedAt")))
        failures.Add("community index UpdatedAt is required.");
    else if (!DateTimeOffset.TryParse(GetJsonString(indexJson, "UpdatedAt"), out _))
        failures.Add("community index UpdatedAt must be a valid date/time.");

    var packs = GetJsonArray(indexJson, "Packs");
    if (packs == null)
    {
        failures.Add("community index Packs array is required.");
        return;
    }

    var requiredProperties = new[]
    {
        "Id", "Name", "Author", "Description", "CoverUrl", "PackageUrl", "SourcePackageUrl", "ReadmeUrl",
        "Version", "SizeBytes", "DownloadCount", "GroupCount", "RuleCount", "SoundCount", "Sha256", "Tags",
        "Category", "GameModes", "Jobs", "TriggerTypes", "CompatiblePluginVersion", "CreatedAt", "UpdatedAt",
        "License", "ContentWarning", "Changelog", "ChangelogUrl", "ReleaseNotesUrl", "Deprecated", "Hidden", "CoverSha256"
    };

    foreach (var packNode in packs)
    {
        if (packNode is not JsonObject packJson)
            continue;

        var label = FirstNonEmpty(GetJsonString(packJson, "Id"), GetJsonString(packJson, "Name"), "(missing id)");
        var missing = requiredProperties
            .Where(property => !HasJsonProperty(packJson, property))
            .ToArray();
        if (missing.Length > 0)
            failures.Add($"community pack '{label}' is missing v2 fields: {string.Join(", ", missing)}.");

        AddMissingStringFailure(packJson, label, "Id", failures);
        AddMissingStringFailure(packJson, label, "PackageUrl", failures);
        AddMissingStringFailure(packJson, label, "ReadmeUrl", failures);
        AddMissingStringFailure(packJson, label, "Sha256", failures);
        AddMissingStringFailure(packJson, label, "Category", failures);
        AddMissingStringFailure(packJson, label, "CreatedAt", failures);
        AddMissingStringFailure(packJson, label, "UpdatedAt", failures);
        AddMissingStringFailure(packJson, label, "License", failures);

        if (GetJsonArray(packJson, "GameModes") is not { Count: > 0 })
            failures.Add($"community pack '{label}' GameModes must contain at least one value.");
        if (GetJsonArray(packJson, "TriggerTypes") is not { Count: > 0 })
            failures.Add($"community pack '{label}' TriggerTypes must contain at least one value.");

        if (!string.IsNullOrWhiteSpace(GetJsonString(packJson, "CreatedAt"))
            && !DateTimeOffset.TryParse(GetJsonString(packJson, "CreatedAt"), out _))
        {
            failures.Add($"community pack '{label}' CreatedAt must be a valid date/time.");
        }

        if (!string.IsNullOrWhiteSpace(GetJsonString(packJson, "UpdatedAt"))
            && !DateTimeOffset.TryParse(GetJsonString(packJson, "UpdatedAt"), out _))
        {
            failures.Add($"community pack '{label}' UpdatedAt must be a valid date/time.");
        }
    }
}

static async Task CheckCommunityPackAsync(
    CommunityPackInfo pack,
    IndexSource source,
    string tempDirectory,
    List<string> failures)
{
    var label = string.IsNullOrWhiteSpace(pack.Id) ? "(missing id)" : pack.Id;
    try
    {
        pack.Tags ??= [];
        pack.GameModes ??= [];
        pack.Jobs ??= [];
        pack.TriggerTypes ??= [];

        if (string.IsNullOrWhiteSpace(pack.Id))
            throw new InvalidOperationException("pack id is required.");
        if (string.IsNullOrWhiteSpace(pack.PackageUrl))
            throw new InvalidOperationException("PackageUrl is required.");
        if (pack.SizeBytes <= 0)
            throw new InvalidOperationException("SizeBytes must be greater than zero.");
        if (pack.SizeBytes > RepositoryCheckSecurity.MaxPackageBytes)
            throw new InvalidOperationException($"SizeBytes exceeds {RepositoryCheckSecurity.FormatBytes(RepositoryCheckSecurity.MaxPackageBytes)}.");
        if (pack.DownloadCount < 0)
            throw new InvalidOperationException("DownloadCount cannot be negative.");
        if (pack.GroupCount < 0 || pack.RuleCount < 0 || pack.SoundCount < 0)
            throw new InvalidOperationException("GroupCount, RuleCount and SoundCount cannot be negative.");
        if (string.IsNullOrWhiteSpace(pack.ReadmeUrl))
            throw new InvalidOperationException("ReadmeUrl is required.");
        if (string.IsNullOrWhiteSpace(pack.Category))
            throw new InvalidOperationException("Category is required.");
        if (pack.GameModes.Count == 0)
            throw new InvalidOperationException("GameModes must contain at least one value.");
        if (pack.TriggerTypes.Count == 0)
            throw new InvalidOperationException("TriggerTypes must contain at least one value.");
        if (string.IsNullOrWhiteSpace(pack.CreatedAt) || !DateTimeOffset.TryParse(pack.CreatedAt, out _))
            throw new InvalidOperationException("CreatedAt is required and must be a valid date/time.");
        if (string.IsNullOrWhiteSpace(pack.UpdatedAt) || !DateTimeOffset.TryParse(pack.UpdatedAt, out _))
            throw new InvalidOperationException("UpdatedAt is required and must be a valid date/time.");
        if (string.IsNullOrWhiteSpace(pack.License))
            throw new InvalidOperationException("License is required.");
        if (!RepositoryCheckSecurity.IsSha256Text(pack.Sha256))
            throw new InvalidOperationException("Sha256 is required and must be 64 hex characters.");

        var package = await MaterializeResourceAsync(
            pack.PackageUrl,
            source,
            tempDirectory,
            $"{SafeFileName(pack.Id)}.sfxpack",
            RepositoryCheckSecurity.MaxPackageBytes,
            pack.SizeBytes);

        if (package.Length != pack.SizeBytes)
            throw new InvalidOperationException($"package size mismatch: index {pack.SizeBytes}, actual {package.Length}.");
        if (!package.Sha256.Equals(pack.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"package Sha256 mismatch: index {pack.Sha256}, actual {package.Sha256}.");

        ValidateSfxPack(package.Path);

        var readme = await MaterializeResourceAsync(
            pack.ReadmeUrl,
            source,
            tempDirectory,
            $"{SafeFileName(pack.Id)}-README.txt",
            RepositoryCheckSecurity.MaxReadmeBytes,
            expectedSize: 0);
        if (readme.Length <= 0)
            throw new InvalidOperationException("README is empty.");

        if (!string.IsNullOrWhiteSpace(pack.CoverUrl))
        {
            if (!RepositoryCheckSecurity.IsSha256Text(pack.CoverSha256))
                throw new InvalidOperationException("CoverSha256 is required when CoverUrl is set and must be 64 hex characters.");

            var cover = await MaterializeResourceAsync(
                pack.CoverUrl,
                source,
                tempDirectory,
                $"{SafeFileName(pack.Id)}-cover{Path.GetExtension(pack.CoverUrl)}",
                RepositoryCheckSecurity.MaxRemoteCoverBytes,
                expectedSize: 0);
            if (!cover.Sha256.Equals(pack.CoverSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"cover Sha256 mismatch: index {pack.CoverSha256}, actual {cover.Sha256}.");
        }
        else if (!string.IsNullOrWhiteSpace(pack.CoverSha256))
        {
            throw new InvalidOperationException("CoverSha256 is set but CoverUrl is empty.");
        }
    }
    catch (Exception ex)
    {
        failures.Add($"community pack '{label}' failed validation: {ex.Message}");
    }
}

static async Task<IndexSource> LoadIndexSourceAsync(CheckOptions options)
{
    if (!string.IsNullOrWhiteSpace(options.CommunityIndexPath))
    {
        var path = Path.GetFullPath(options.CommunityIndexPath);
        return new IndexSource(
            await File.ReadAllTextAsync(path),
            Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory(),
            BaseUri: null);
    }

    var uri = new Uri(options.CommunityIndexUrl!, UriKind.Absolute);
    using var client = CreateHttpClient();
    return new IndexSource(
        await client.GetStringAsync(uri),
        BaseDirectory: null,
        BaseUri: uri);
}

static async Task<MaterializedFile> MaterializeResourceAsync(
    string value,
    IndexSource source,
    string tempDirectory,
    string fileName,
    long maxBytes,
    long expectedSize)
{
    if (TryResolveLocalPath(value, source, out var localPath))
    {
        var info = new FileInfo(localPath);
        if (!info.Exists)
            throw new FileNotFoundException("resource file was not found.", localPath);
        if (info.Length > maxBytes)
            throw new InvalidOperationException($"resource exceeds {RepositoryCheckSecurity.FormatBytes(maxBytes)}.");
        if (expectedSize > 0 && info.Length != expectedSize)
            throw new InvalidOperationException($"resource size mismatch before hash: expected {expectedSize}, actual {info.Length}.");

        return new MaterializedFile(localPath, info.Length, await ComputeFileSha256Async(localPath), IsTemporary: false);
    }

    var uri = ResolveUri(value, source);
    var destinationPath = Path.Combine(tempDirectory, fileName);
    using var client = CreateHttpClient();
    using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
    response.EnsureSuccessStatusCode();

    var contentLength = response.Content.Headers.ContentLength;
    if (contentLength.HasValue)
    {
        if (contentLength.Value > maxBytes)
            throw new InvalidOperationException($"remote resource exceeds {RepositoryCheckSecurity.FormatBytes(maxBytes)}.");
        if (expectedSize > 0 && contentLength.Value != expectedSize)
            throw new InvalidOperationException($"remote Content-Length mismatch: expected {expectedSize}, actual {contentLength.Value}.");
    }

    await using var input = await response.Content.ReadAsStreamAsync();
    return await DownloadAndHashAsync(input, destinationPath, maxBytes);
}

static bool TryResolveLocalPath(string value, IndexSource source, out string path)
{
    path = string.Empty;
    if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
    {
        if (!uri.IsFile)
        {
            if (!string.IsNullOrWhiteSpace(source.BaseDirectory)
                && TryGetRawRepositoryRelativePath(uri, out var relativePath))
            {
                path = Path.GetFullPath(Path.Combine(
                    source.BaseDirectory,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));
                return true;
            }

            return false;
        }

        path = uri.LocalPath;
        return true;
    }

    if (string.IsNullOrWhiteSpace(source.BaseDirectory))
        return false;

    path = Path.GetFullPath(Path.Combine(source.BaseDirectory, value.Replace('/', Path.DirectorySeparatorChar)));
    return true;
}

static Uri ResolveUri(string value, IndexSource source)
{
    if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        return uri;
    if (source.BaseUri == null)
        throw new InvalidOperationException($"relative URL cannot be resolved without a base URL: {value}");

    return new Uri(source.BaseUri, value);
}

static async Task<MaterializedFile> DownloadAndHashAsync(Stream input, string destinationPath, long maxBytes)
{
    await using var output = File.Create(destinationPath);
    using var sha = SHA256.Create();
    var buffer = new byte[81920];
    long total = 0;
    while (true)
    {
        var read = await input.ReadAsync(buffer);
        if (read == 0)
            break;

        total += read;
        if (total > maxBytes)
            throw new InvalidOperationException($"download exceeded {RepositoryCheckSecurity.FormatBytes(maxBytes)}.");

        sha.TransformBlock(buffer, 0, read, null, 0);
        await output.WriteAsync(buffer.AsMemory(0, read));
    }

    sha.TransformFinalBlock([], 0, 0);
    return new MaterializedFile(destinationPath, total, Convert.ToHexString(sha.Hash ?? []), IsTemporary: true);
}

static async Task<string> ComputeFileSha256Async(string path)
{
    await using var stream = File.OpenRead(path);
    return Convert.ToHexString(await SHA256.HashDataAsync(stream));
}

static HttpClient CreateHttpClient()
{
    var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AllTimeSoundTrigger-CI/1.0");
    return client;
}

static void ValidateSfxPack(string packagePath)
{
    using var archive = ZipFile.OpenRead(packagePath);
    RepositoryCheckSecurity.ValidateArchive(archive);
}

static bool IsGeneratedPath(string repoRoot, string path)
{
    var relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
    return relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
           || relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
           || relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
           || relative.StartsWith(".vs/", StringComparison.OrdinalIgnoreCase);
}

static string ResolveDefaultPluginVersion(string repoRoot)
{
    var csprojPath = Path.Combine(repoRoot, "AllTimeSoundTrigger.csproj");
    if (!File.Exists(csprojPath))
        return string.Empty;

    try
    {
        var csproj = XDocument.Load(csprojPath);
        return csproj.Root?
            .Elements("PropertyGroup")
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals("Version", StringComparison.OrdinalIgnoreCase))
            ?.Value
            .Trim()
            ?? string.Empty;
    }
    catch
    {
        return string.Empty;
    }
}

static PackageInspection InspectSfxPack(string packagePath)
{
    using var archive = ZipFile.OpenRead(packagePath);
    RepositoryCheckSecurity.ValidateArchive(archive);

    var entries = archive.Entries
        .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
        .ToArray();
    var profileEntry = entries.FirstOrDefault(entry => NormalizeZipPath(entry.FullName).Equals("profile.json", StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException("profile.json is required.");
    var profile = ReadJsonObject(profileEntry);
    var groups = GetJsonArray(profile, "Groups") ?? [];
    var groupCount = groups.Count;
    var ruleCount = 0;
    var triggerTypes = new List<string>();

    foreach (var groupNode in groups)
    {
        if (groupNode is not JsonObject group)
            continue;

        var rules = GetJsonArray(group, "Rules") ?? [];
        ruleCount += rules.Count;
        foreach (var ruleNode in rules)
        {
            if (ruleNode is not JsonObject rule)
                continue;

            var trigger = GetJsonObject(rule, "Trigger");
            if (trigger == null)
                continue;

            var triggerType = FirstNonEmpty(GetJsonString(trigger, "Type"), GetJsonString(trigger, "EventType"));
            if (triggerType.Length > 0 && !triggerTypes.Contains(triggerType, StringComparer.OrdinalIgnoreCase))
                triggerTypes.Add(triggerType);
        }
    }

    var manifest = ReadManifest(archive);
    return new PackageInspection(
        groupCount,
        ruleCount,
        entries.Count(entry => NormalizeZipPath(entry.FullName).StartsWith("sounds/", StringComparison.OrdinalIgnoreCase)),
        triggerTypes,
        manifest,
        ReadTextEntry(archive, "README.txt"),
        ResolveCoverEntryName(archive, manifest));
}

static JsonObject ReadJsonObject(ZipArchiveEntry entry)
{
    using var stream = entry.Open();
    return JsonNode.Parse(stream)?.AsObject()
           ?? throw new InvalidOperationException($"{entry.FullName} must contain a JSON object.");
}

static CommunitySubmissionManifest? ReadManifest(ZipArchive archive)
{
    var entry = archive.Entries.FirstOrDefault(item => NormalizeZipPath(item.FullName).Equals("submission.json", StringComparison.OrdinalIgnoreCase));
    if (entry == null)
        return null;

    using var stream = entry.Open();
    return JsonSerializer.Deserialize<CommunitySubmissionManifest>(stream, RepositoryCheckJson.Options);
}

static string ReadTextEntry(ZipArchive archive, string entryName)
{
    var entry = archive.Entries.FirstOrDefault(item => NormalizeZipPath(item.FullName).Equals(entryName, StringComparison.OrdinalIgnoreCase));
    if (entry == null)
        return string.Empty;

    using var stream = entry.Open();
    using var reader = new StreamReader(stream, Encoding.UTF8, true);
    return reader.ReadToEnd().Trim();
}

static string ResolveCoverEntryName(ZipArchive archive, CommunitySubmissionManifest? manifest)
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
                                && RepositoryCheckSecurity.IsAllowedCoverExtension(Path.GetExtension(path)))
           ?? string.Empty;
}

static string? TryFindSiblingFile(string packagePath, string fileName)
{
    var directory = Path.GetDirectoryName(packagePath);
    if (string.IsNullOrWhiteSpace(directory))
        return null;

    var candidate = Path.Combine(directory, fileName);
    return File.Exists(candidate) ? candidate : null;
}

static string? TryResolvePackSibling(IndexSource source, string packId, string fileName)
{
    if (string.IsNullOrWhiteSpace(source.BaseDirectory) || string.IsNullOrWhiteSpace(packId))
        return null;

    var candidate = Path.Combine(source.BaseDirectory, "packs", SafeFileName(packId), fileName);
    return File.Exists(candidate) ? candidate : null;
}

static string? TryFindSiblingCover(string packagePath)
{
    var directory = Path.GetDirectoryName(packagePath);
    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        return null;

    return Directory.EnumerateFiles(directory, "cover.*")
        .FirstOrDefault(path => RepositoryCheckSecurity.IsAllowedCoverExtension(Path.GetExtension(path)));
}

static string? TryResolvePackCover(IndexSource source, string packId)
{
    if (string.IsNullOrWhiteSpace(source.BaseDirectory) || string.IsNullOrWhiteSpace(packId))
        return null;

    var directory = Path.Combine(source.BaseDirectory, "packs", SafeFileName(packId));
    if (!Directory.Exists(directory))
        return null;

    return Directory.EnumerateFiles(directory, "cover.*")
        .FirstOrDefault(path => RepositoryCheckSecurity.IsAllowedCoverExtension(Path.GetExtension(path)));
}

static void ExtractCover(string packagePath, string coverEntryName, string destinationPath)
{
    using var archive = ZipFile.OpenRead(packagePath);
    var entry = archive.Entries.First(item => NormalizeZipPath(item.FullName).Equals(coverEntryName, StringComparison.OrdinalIgnoreCase));
    using var input = entry.Open();
    using var output = File.Create(destinationPath);
    input.CopyTo(output);
}

static string BuildSiblingResourceValue(string packageUrl, string siblingFileName)
{
    if (Uri.TryCreate(packageUrl, UriKind.Absolute, out var uri))
        return new Uri(uri, siblingFileName).ToString();

    var normalized = (packageUrl ?? string.Empty).Replace('\\', '/');
    var slash = normalized.LastIndexOf('/');
    return slash >= 0
        ? $"{normalized[..(slash + 1)]}{siblingFileName}"
        : siblingFileName;
}

static string BuildGeneratedReadme(JsonObject packJson, PackageInspection inspection)
{
    var name = FirstNonEmpty(GetJsonString(packJson, "Name"), GetJsonString(packJson, "Id"), "未命名音效包");
    var author = FirstNonEmpty(GetJsonString(packJson, "Author"), "未知作者");
    var description = FirstNonEmpty(GetJsonString(packJson, "Description"), $"{name} community sound pack.");
    return $"""
           {name}

           作者：{author}

           {description}

           内容：
           - 分组：{inspection.GroupCount}
           - 规则：{inspection.RuleCount}
           - 音效：{inspection.SoundCount}
           """.Trim();
}

static bool TryGetRawRepositoryRelativePath(Uri uri, out string relativePath)
{
    relativePath = string.Empty;
    var path = uri.AbsolutePath.Replace('\\', '/');
    foreach (var marker in new[] { "/raw/master/", "/raw/main/" })
    {
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            continue;

        relativePath = Uri.UnescapeDataString(path[(index + marker.Length)..]);
        return relativePath.Length > 0;
    }

    return false;
}

static void AddMissingStringFailure(JsonObject obj, string label, string property, List<string> failures)
{
    if (string.IsNullOrWhiteSpace(GetJsonString(obj, property)))
        failures.Add($"community pack '{label}' {property} is required.");
}

static string FirstNonEmpty(params string?[] values)
    => values.Select(value => (value ?? string.Empty).Trim()).FirstOrDefault(value => value.Length > 0) ?? string.Empty;

static IReadOnlyList<string> FirstNonEmptyList(params IEnumerable<string>?[] values)
{
    foreach (var value in values)
    {
        var normalized = NormalizeStringList(value);
        if (normalized.Count > 0)
            return normalized;
    }

    return [];
}

static List<string> NormalizeStringList(IEnumerable<string>? values)
    => (values ?? [])
        .Select(value => (value ?? string.Empty).Trim())
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

static string NormalizeZipPath(string path)
    => (path ?? string.Empty).Replace('\\', '/').TrimStart('/');

static bool HasJsonProperty(JsonObject obj, string property)
    => FindJsonPropertyName(obj, property) != null;

static string? FindJsonPropertyName(JsonObject obj, string property)
    => obj.Select(item => item.Key)
        .FirstOrDefault(key => key.Equals(property, StringComparison.OrdinalIgnoreCase));

static JsonArray? GetJsonArray(JsonObject obj, string property)
    => FindJsonPropertyName(obj, property) is { } key && obj[key] is JsonArray array ? array : null;

static JsonObject? GetJsonObject(JsonObject obj, string property)
    => FindJsonPropertyName(obj, property) is { } key && obj[key] is JsonObject child ? child : null;

static string GetJsonString(JsonObject obj, string property)
{
    if (FindJsonPropertyName(obj, property) is not { } key)
        return string.Empty;

    try
    {
        return obj[key]?.GetValue<string>()?.Trim() ?? string.Empty;
    }
    catch
    {
        return obj[key]?.ToJsonString().Trim('"') ?? string.Empty;
    }
}

static int GetJsonInt(JsonObject obj, string property)
{
    if (FindJsonPropertyName(obj, property) is not { } key)
        return 0;

    try
    {
        return obj[key]?.GetValue<int>() ?? 0;
    }
    catch
    {
        try
        {
            return (int)(obj[key]?.GetValue<long>() ?? 0);
        }
        catch
        {
            return 0;
        }
    }
}

static long GetJsonLong(JsonObject obj, string property)
{
    if (FindJsonPropertyName(obj, property) is not { } key)
        return 0;

    try
    {
        return obj[key]?.GetValue<long>() ?? 0;
    }
    catch
    {
        return 0;
    }
}

static bool SetJsonStringIfMissing(JsonObject obj, string property, string value, List<string> fixes, string label)
{
    if (HasJsonProperty(obj, property) && !string.IsNullOrWhiteSpace(GetJsonString(obj, property)))
        return false;

    return SetJsonString(obj, property, value, fixes, label);
}

static bool SetJsonNumberIfMissing(JsonObject obj, string property, long value, List<string> fixes, string label)
{
    if (HasJsonProperty(obj, property))
        return false;

    return SetJsonNumber(obj, property, value, fixes, label);
}

static bool SetJsonBoolIfMissing(JsonObject obj, string property, bool value, List<string> fixes, string label)
{
    if (HasJsonProperty(obj, property))
        return false;

    var key = FindJsonPropertyName(obj, property) ?? property;
    obj[key] = JsonValue.Create(value);
    fixes.Add($"{label} set");
    return true;
}

static bool SetJsonArrayIfMissing(JsonObject obj, string property, IEnumerable<string> values, List<string> fixes, string label)
{
    if (HasJsonProperty(obj, property))
        return false;

    SetJsonArray(obj, property, ToJsonArray(values), fixes, label);
    return true;
}

static bool SetJsonArrayIfMissingOrEmpty(JsonObject obj, string property, IEnumerable<string> values, List<string> fixes, string label)
{
    var array = GetJsonArray(obj, property);
    if (array is { Count: > 0 })
        return false;

    SetJsonArray(obj, property, ToJsonArray(values), fixes, label);
    return true;
}

static bool SetJsonString(JsonObject obj, string property, string value, List<string> fixes, string label)
{
    var key = FindJsonPropertyName(obj, property) ?? property;
    value ??= string.Empty;
    if (HasJsonProperty(obj, property) && GetJsonString(obj, property).Equals(value, StringComparison.Ordinal))
        return false;

    obj[key] = JsonValue.Create(value);
    fixes.Add($"{label} set");
    return true;
}

static bool SetJsonNumber(JsonObject obj, string property, long value, List<string> fixes, string label)
{
    var key = FindJsonPropertyName(obj, property) ?? property;
    if (HasJsonProperty(obj, property) && GetJsonLong(obj, property) == value)
        return false;

    obj[key] = JsonValue.Create(value);
    fixes.Add($"{label} set");
    return true;
}

static void SetJsonArray(JsonObject obj, string property, JsonArray value, List<string> fixes, string label)
{
    var key = FindJsonPropertyName(obj, property) ?? property;
    obj[key] = value;
    fixes.Add($"{label} set");
}

static JsonArray ToJsonArray(IEnumerable<string> values)
{
    var array = new JsonArray();
    foreach (var value in NormalizeStringList(values))
        array.Add(value);

    return array;
}

static string GetProperty(IReadOnlyDictionary<string, string> values, string name)
    => values.TryGetValue(name, out var value) ? value : string.Empty;

static void AddMismatch(List<string> failures, string label, string expected, string actual)
{
    if (!expected.Equals(actual, StringComparison.Ordinal))
        failures.Add($"{label} mismatch: '{expected}' != '{actual}'.");
}

static string SafeFileName(string value)
{
    var safe = string.IsNullOrWhiteSpace(value) ? "resource" : value.Trim();
    foreach (var invalid in Path.GetInvalidFileNameChars())
        safe = safe.Replace(invalid, '_');

    return safe.Replace('/', '_').Replace('\\', '_');
}

static void TryDeleteDirectory(string path)
{
    try
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }
    catch
    {
        // Best-effort cleanup for downloaded check artifacts.
    }
}

internal sealed record CheckOptions(
    string RepoRoot,
    string CommunityIndexPath,
    string CommunityIndexUrl,
    bool SkipCommunityIndex,
    bool Fix)
{
    public static CheckOptions Parse(IReadOnlyList<string> args)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var communityIndexPath = string.Empty;
        var communityIndexUrl = string.Empty;
        var skipCommunityIndex = false;
        var fix = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.Equals("--repo-root", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                repoRoot = args[++i];
            else if (arg.Equals("--community-index-path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                communityIndexPath = args[++i];
            else if (arg.Equals("--community-index-url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                communityIndexUrl = args[++i];
            else if (arg.Equals("--skip-community-index", StringComparison.OrdinalIgnoreCase))
                skipCommunityIndex = true;
            else if (arg.Equals("--fix", StringComparison.OrdinalIgnoreCase))
                fix = true;
            else
                throw new ArgumentException($"Unknown or incomplete argument: {arg}");
        }

        return new CheckOptions(repoRoot, communityIndexPath, communityIndexUrl, skipCommunityIndex, fix);
    }
}

internal sealed record IndexSource(string Json, string? BaseDirectory, Uri? BaseUri);

internal sealed record MaterializedFile(string Path, long Length, string Sha256, bool IsTemporary);

internal sealed record PackageInspection(
    int GroupCount,
    int RuleCount,
    int SoundCount,
    IReadOnlyList<string> TriggerTypes,
    CommunitySubmissionManifest? Manifest,
    string Readme,
    string CoverEntryName);

internal sealed class CommunitySubmissionManifest
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string PackageVersion { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public string Category { get; set; } = string.Empty;

    public List<string> GameModes { get; set; } = [];

    public List<string> Jobs { get; set; } = [];

    public List<string> TriggerTypes { get; set; } = [];

    public string CompatiblePluginVersion { get; set; } = string.Empty;

    public string License { get; set; } = string.Empty;

    public string ContentWarning { get; set; } = string.Empty;

    public string Readme { get; set; } = string.Empty;

    public string CoverEntryName { get; set; } = string.Empty;
}

internal static class RepositoryCheckJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

internal sealed class CommunityPackIndex
{
    public int Version { get; set; }

    public string UpdatedAt { get; set; } = string.Empty;

    public List<CommunityPackInfo> Packs { get; set; } = [];
}

internal sealed class CommunityPackInfo
{
    public string Id { get; set; } = string.Empty;

    public string PackageUrl { get; set; } = string.Empty;

    public string SourcePackageUrl { get; set; } = string.Empty;

    public string CoverUrl { get; set; } = string.Empty;

    public string ReadmeUrl { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public string CoverSha256 { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public long DownloadCount { get; set; }

    public int GroupCount { get; set; }

    public int RuleCount { get; set; }

    public int SoundCount { get; set; }

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
}

internal static class RepositoryCheckSecurity
{
    public const int MaxEntryCount = 512;
    public const int MaxSoundCount = 256;
    public const long MaxPackageBytes = 100L * 1024L * 1024L;
    public const long MaxUncompressedBytes = 200L * 1024L * 1024L;
    public const long MaxTotalSoundBytes = 150L * 1024L * 1024L;
    public const long MaxSingleSoundBytes = 50L * 1024L * 1024L;
    public const long MaxJsonEntryBytes = 5L * 1024L * 1024L;
    public const long MaxReadmeBytes = 1L * 1024L * 1024L;
    public const long MaxArchiveCoverBytes = 2L * 1024L * 1024L;
    public const long MaxRemoteCoverBytes = 5L * 1024L * 1024L;

    private static readonly string[] AllowedSoundExtensions = [".mp3", ".wav", ".ogg"];
    private static readonly string[] AllowedCoverExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    public static void ValidateArchive(ZipArchive archive)
    {
        var entries = archive.Entries
            .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .ToArray();
        if (entries.Length > MaxEntryCount)
            throw new InvalidOperationException($"file count {entries.Length} exceeds {MaxEntryCount}.");

        long totalBytes = 0;
        long soundBytes = 0;
        var soundCount = 0;
        foreach (var entry in entries)
        {
            var path = NormalizeZipPath(entry.FullName);
            if (!IsSafeZipPath(path))
                throw new InvalidOperationException($"unsafe zip path: {entry.FullName}");

            totalBytes += Math.Max(0, entry.Length);
            if (totalBytes > MaxUncompressedBytes)
                throw new InvalidOperationException($"uncompressed size exceeds {FormatBytes(MaxUncompressedBytes)}.");

            ValidateEntry(path, entry, ref soundCount, ref soundBytes);
        }
    }

    public static bool IsSha256Text(string value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Trim().Length == 64
           && value.Trim().All(Uri.IsHexDigit);

    public static bool IsAllowedCoverExtension(string extension)
        => AllowedCoverExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    public static string FormatBytes(long value)
    {
        if (value >= 1024L * 1024L)
            return $"{value / 1024f / 1024f:0.0} MB";
        if (value >= 1024L)
            return $"{value / 1024f:0.0} KB";

        return $"{value} B";
    }

    private static void ValidateEntry(string path, ZipArchiveEntry entry, ref int soundCount, ref long soundBytes)
    {
        if (IsKnownTextEntry(path))
        {
            ValidateEntrySize(entry, MaxJsonEntryBytes);
            return;
        }

        if (path.Equals("README.txt", StringComparison.OrdinalIgnoreCase))
        {
            ValidateEntrySize(entry, MaxReadmeBytes);
            return;
        }

        if (path.StartsWith("sounds/", StringComparison.OrdinalIgnoreCase))
        {
            ValidateSoundEntry(path, entry);
            soundCount++;
            if (soundCount > MaxSoundCount)
                throw new InvalidOperationException($"sound count exceeds {MaxSoundCount}.");

            soundBytes += entry.Length;
            if (soundBytes > MaxTotalSoundBytes)
                throw new InvalidOperationException($"sound total size exceeds {FormatBytes(MaxTotalSoundBytes)}.");
            return;
        }

        if (path.StartsWith("covers/", StringComparison.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(path);
            if (!AllowedCoverExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"unsupported cover extension: {entry.FullName}");
            ValidateEntrySize(entry, MaxArchiveCoverBytes);
            return;
        }

        throw new InvalidOperationException($"unexpected file in .sfxpack: {entry.FullName}");
    }

    private static void ValidateSoundEntry(string path, ZipArchiveEntry entry)
    {
        var extension = Path.GetExtension(path);
        if (!AllowedSoundExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"unsupported sound extension: {entry.FullName}");
        if (entry.Length <= 0)
            throw new InvalidOperationException($"empty sound file: {entry.FullName}");
        ValidateEntrySize(entry, MaxSingleSoundBytes);
        if (!HasExpectedSoundHeader(entry, extension))
            throw new InvalidOperationException($"sound header does not match extension: {entry.FullName}");
    }

    private static bool HasExpectedSoundHeader(ZipArchiveEntry entry, string extension)
    {
        Span<byte> header = stackalloc byte[16];
        using var stream = entry.Open();
        var read = 0;
        while (read < header.Length)
        {
            var count = stream.Read(header[read..]);
            if (count == 0)
                break;

            read += count;
        }

        return extension.ToLowerInvariant() switch
        {
            ".wav" => read >= 12
                      && header[0] == (byte)'R'
                      && header[1] == (byte)'I'
                      && header[2] == (byte)'F'
                      && header[3] == (byte)'F'
                      && header[8] == (byte)'W'
                      && header[9] == (byte)'A'
                      && header[10] == (byte)'V'
                      && header[11] == (byte)'E',
            ".ogg" => read >= 4
                      && header[0] == (byte)'O'
                      && header[1] == (byte)'g'
                      && header[2] == (byte)'g'
                      && header[3] == (byte)'S',
            ".mp3" => read >= 3
                      && ((header[0] == (byte)'I' && header[1] == (byte)'D' && header[2] == (byte)'3')
                          || (read >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)),
            _ => false
        };
    }

    private static string NormalizeZipPath(string path)
        => (path ?? string.Empty).Replace('\\', '/').TrimStart('/');

    private static bool IsSafeZipPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains(':'))
            return false;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts.All(part => part != "..");
    }

    private static bool IsKnownTextEntry(string path)
        => path.Equals("profile.json", StringComparison.OrdinalIgnoreCase)
           || path.Equals("sound-library.json", StringComparison.OrdinalIgnoreCase)
           || path.Equals("submission.json", StringComparison.OrdinalIgnoreCase);

    private static void ValidateEntrySize(ZipArchiveEntry entry, long maxBytes)
    {
        if (entry.Length > maxBytes)
            throw new InvalidOperationException($"{entry.FullName} exceeds {FormatBytes(maxBytes)}.");
    }
}
