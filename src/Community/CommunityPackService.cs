using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AllTimeSoundTrigger.Utilities;
using Dalamud.Plugin.Services;

namespace AllTimeSoundTrigger.Community;

public sealed class CommunityPackService : IDisposable
{
    public const string DefaultIndexUrl = "https://gitee.com/aikyan931023/ffxiv-sfx-community/raw/master/index.json";

    private const long MaxPackageBytes = 100L * 1024L * 1024L;
    private const long MaxCoverBytes = 5L * 1024L * 1024L;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IPluginLog log;
    private readonly HttpClient httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(45)
    };
    private readonly object gate = new();
    private readonly Dictionary<string, CommunityInstalledPack> installedPacks = new(StringComparer.OrdinalIgnoreCase);
    private List<CommunityPackInfo> packs = [];
    private bool disposed;

    public CommunityPackService(string rootDirectory, IPluginLog log)
    {
        this.log = log;
        CacheDirectory = Path.Combine(rootDirectory, "community-cache");
        CoverDirectory = Path.Combine(CacheDirectory, "covers");
        DownloadDirectory = Path.Combine(CacheDirectory, "downloads");
        IndexCachePath = Path.Combine(CacheDirectory, "index.json");
        InstalledCachePath = Path.Combine(CacheDirectory, "installed.json");

        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(CoverDirectory);
        Directory.CreateDirectory(DownloadDirectory);
        CleanupTemporaryFiles();
        LoadInstalled();
        TryLoadCachedIndex();
    }

    public string CacheDirectory { get; }

    public string CoverDirectory { get; }

    public string DownloadDirectory { get; }

    public string IndexCachePath { get; }

    public string InstalledCachePath { get; }

    public IReadOnlyList<CommunityPackInfo> Packs
    {
        get
        {
            lock (gate)
                return packs.ToArray();
        }
    }

    public IReadOnlyList<CommunityInstalledPack> InstalledPacks
    {
        get
        {
            lock (gate)
                return installedPacks.Values.Select(CloneInstalledPack).ToArray();
        }
    }

    public CommunityRefreshResult TryLoadCachedIndex()
    {
        if (!File.Exists(IndexCachePath))
            return new CommunityRefreshResult(0, string.Empty, true);

        using var stream = File.OpenRead(IndexCachePath);
        var index = JsonSerializer.Deserialize<CommunityPackIndex>(stream, SerializerOptions)
            ?? new CommunityPackIndex();
        index.Normalize();

        lock (gate)
            packs = index.Packs;

        return new CommunityRefreshResult(index.Packs.Count, index.UpdatedAt, true);
    }

    public async Task<CommunityRefreshResult> RefreshIndexAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var json = await httpClient.GetStringAsync(DefaultIndexUrl, cancellationToken).ConfigureAwait(false);
        var index = JsonSerializer.Deserialize<CommunityPackIndex>(json, SerializerOptions)
            ?? throw new InvalidOperationException("社区索引无法读取。");
        index.Normalize();

        Directory.CreateDirectory(CacheDirectory);
        await File.WriteAllTextAsync(
            IndexCachePath,
            JsonSerializer.Serialize(index, SerializerOptions),
            cancellationToken).ConfigureAwait(false);

        lock (gate)
            packs = index.Packs;

        log.Information("[AllTimeSoundTrigger] Community index refreshed: {PackCount} packs.", index.Packs.Count);
        return new CommunityRefreshResult(index.Packs.Count, index.UpdatedAt, false);
    }

    public string GetCoverCachePath(CommunityPackInfo pack)
    {
        var extension = ".png";
        if (Uri.TryCreate(pack.CoverUrl, UriKind.Absolute, out var uri))
        {
            var candidate = Path.GetExtension(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length <= 8)
                extension = candidate;
        }

        var hashSuffix = (pack.CoverSha256 ?? string.Empty).Trim();
        if (hashSuffix.Length > 12)
            hashSuffix = hashSuffix[..12];

        var fileName = hashSuffix.Length == 0
            ? MakeSafeFileName(pack.Id)
            : $"{MakeSafeFileName(pack.Id)}_{MakeSafeFileName(hashSuffix)}";
        return Path.Combine(CoverDirectory, $"{fileName}{extension}");
    }

    public async Task<string> EnsureCoverCachedAsync(CommunityPackInfo pack, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(pack.CoverUrl))
            return string.Empty;

        var path = GetCoverCachePath(pack);
        if (File.Exists(path))
        {
            if (await VerifyCoverHashAsync(path, pack, cancellationToken).ConfigureAwait(false))
                return path;

            File.Delete(path);
        }

        Directory.CreateDirectory(CoverDirectory);
        await DownloadFileAsync(pack.CoverUrl, path, MaxCoverBytes, cancellationToken).ConfigureAwait(false);
        if (!await VerifyCoverHashAsync(path, pack, cancellationToken).ConfigureAwait(false))
        {
            File.Delete(path);
            throw new InvalidOperationException("封面校验失败，已删除下载文件。");
        }

        return path;
    }

    public async Task<string> DownloadPackageAsync(CommunityPackInfo pack, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(pack.PackageUrl))
            throw new InvalidOperationException("这个音效包缺少下载地址。");
        if (string.IsNullOrWhiteSpace(pack.Sha256))
            throw new InvalidOperationException("这个社区音效包缺少 Sha256 校验值，已拒绝下载。");
        if (pack.SizeBytes > MaxPackageBytes)
            throw new InvalidOperationException("这个音效包超过 100MB，已拒绝下载。");

        Directory.CreateDirectory(DownloadDirectory);
        var fileName = $"{MakeSafeFileName(pack.Id)}_{MakeSafeFileName(pack.Version)}.sfxpack";
        var path = Path.Combine(DownloadDirectory, fileName);

        if (File.Exists(path) && await VerifyPackageHashAsync(path, pack, cancellationToken).ConfigureAwait(false))
            return path;

        await DownloadFileAsync(pack.PackageUrl, path, MaxPackageBytes, cancellationToken).ConfigureAwait(false);
        if (pack.SizeBytes > 0)
        {
            var length = new FileInfo(path).Length;
            if (length != pack.SizeBytes)
                throw new InvalidOperationException($"下载大小不一致：期望 {pack.SizeBytes} 字节，实际 {length} 字节。");
        }

        if (!await VerifyPackageHashAsync(path, pack, cancellationToken).ConfigureAwait(false))
        {
            File.Delete(path);
            throw new InvalidOperationException("音效包校验失败，已删除下载文件。");
        }

        return path;
    }

    public bool IsInstalled(CommunityPackInfo pack)
    {
        lock (gate)
        {
            return installedPacks.TryGetValue(pack.Id, out var installed)
                && installed.Version.Equals(pack.Version, StringComparison.OrdinalIgnoreCase);
        }
    }

    public CommunityInstalledPack? GetInstalledPack(CommunityPackInfo pack)
        => GetInstalledPack(pack.Id);

    public CommunityInstalledPack? GetInstalledPack(string packId)
    {
        var id = (packId ?? string.Empty).Trim();
        if (id.Length == 0)
            return null;

        lock (gate)
            return installedPacks.TryGetValue(id, out var installed)
                ? CloneInstalledPack(installed)
                : null;
    }

    public void MarkInstalled(
        CommunityPackInfo pack,
        IReadOnlyCollection<string> groupIds,
        IReadOnlyCollection<string> soundIds,
        IReadOnlyCollection<string> importDirectories)
    {
        var installed = new CommunityInstalledPack
        {
            Id = pack.Id,
            Version = pack.Version,
            Name = pack.Name,
            GroupIds = groupIds.ToList(),
            SoundIds = soundIds.ToList(),
            ImportDirectories = importDirectories.ToList(),
            InstalledAt = DateTimeOffset.Now
        };
        installed.Normalize();

        lock (gate)
            installedPacks[installed.Id] = installed;

        SaveInstalled();
    }

    public void UnmarkInstalled(string packId)
    {
        var id = (packId ?? string.Empty).Trim();
        if (id.Length == 0)
            return;

        lock (gate)
            installedPacks.Remove(id);

        SaveInstalled();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        CleanupTemporaryFiles();
        httpClient.Dispose();
    }

    private async Task DownloadFileAsync(string url, string destinationPath, long maxBytes, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maxBytes)
            throw new InvalidOperationException($"文件超过限制：{FormatBytes(contentLength.Value)}。");

        var tempPath = $"{destinationPath}.tmp";
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = File.Create(tempPath))
            {
                var buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                        break;

                    total += read;
                    if (total > maxBytes)
                        throw new InvalidOperationException($"文件超过限制：{FormatBytes(maxBytes)}。");

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(tempPath, destinationPath, true);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    private static async Task<bool> VerifyPackageHashAsync(string path, CommunityPackInfo pack, CancellationToken cancellationToken)
    {
        var expected = (pack.Sha256 ?? string.Empty).Trim();
        if (expected.Length == 0)
            throw new InvalidOperationException("这个社区音效包缺少 Sha256 校验值。");
        if (!IsSha256Text(expected))
            throw new InvalidOperationException("这个社区音效包的 Sha256 格式不正确。");

        return await VerifySha256Async(path, expected, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> VerifyCoverHashAsync(string path, CommunityPackInfo pack, CancellationToken cancellationToken)
        => await VerifySha256Async(path, pack.CoverSha256, cancellationToken).ConfigureAwait(false);

    private static async Task<bool> VerifySha256Async(string path, string expectedSha256, CancellationToken cancellationToken)
    {
        var expected = (expectedSha256 ?? string.Empty).Trim();
        if (expected.Length == 0)
            return true;
        if (!IsSha256Text(expected))
            return false;

        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        return hash.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSha256Text(string value)
        => value.Length == 64 && value.All(Uri.IsHexDigit);

    private void LoadInstalled()
    {
        if (!File.Exists(InstalledCachePath))
            return;

        try
        {
            var json = File.ReadAllText(InstalledCachePath);
            using var document = JsonDocument.Parse(json);
            installedPacks.Clear();

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(nameof(CommunityInstalledPackStore.Packs), out _))
            {
                var store = JsonSerializer.Deserialize<CommunityInstalledPackStore>(json, SerializerOptions)
                    ?? new CommunityInstalledPackStore();
                store.Normalize();
                foreach (var pack in store.Packs)
                    installedPacks[pack.Id] = pack;
                return;
            }

            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json, SerializerOptions);
            if (values == null)
                return;
            foreach (var item in values)
            {
                var id = (item.Key ?? string.Empty).Trim();
                var version = (item.Value ?? string.Empty).Trim();
                if (id.Length > 0)
                {
                    installedPacks[id] = new CommunityInstalledPack
                    {
                        Id = id,
                        Name = id,
                        Version = version
                    };
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[AllTimeSoundTrigger] Community installed cache could not be read.");
        }
    }

    private void SaveInstalled()
    {
        Directory.CreateDirectory(CacheDirectory);
        CommunityInstalledPackStore snapshot;
        lock (gate)
        {
            snapshot = new CommunityInstalledPackStore
            {
                Packs = installedPacks.Values.Select(CloneInstalledPack).ToList()
            };
        }

        snapshot.Normalize();

        File.WriteAllText(InstalledCachePath, JsonSerializer.Serialize(snapshot, SerializerOptions));
    }

    private static CommunityInstalledPack CloneInstalledPack(CommunityInstalledPack source)
    {
        var clone = new CommunityInstalledPack
        {
            Id = source.Id,
            Version = source.Version,
            Name = source.Name,
            GroupIds = source.GroupIds.ToList(),
            SoundIds = source.SoundIds.ToList(),
            ImportDirectories = source.ImportDirectories.ToList(),
            InstalledAt = source.InstalledAt
        };
        clone.Normalize();
        return clone;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(CommunityPackService));
    }

    private void CleanupTemporaryFiles()
    {
        TryDeleteTemporaryFiles(CoverDirectory);
        TryDeleteTemporaryFiles(DownloadDirectory);
    }

    private static void TryDeleteTemporaryFiles(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return;

            foreach (var path in Directory.EnumerateFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly))
                TryDeleteFile(path);
        }
        catch
        {
            // Best-effort cleanup only.
        }
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
            // Best-effort cleanup only.
        }
    }

    private static string MakeSafeFileName(string value)
    {
        var safe = FilePathText.Normalize(value);
        if (safe.Length == 0)
            safe = "community-pack";

        foreach (var invalid in Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalid, '_');

        return safe;
    }

    private static string FormatBytes(long value)
    {
        if (value >= 1024L * 1024L)
            return $"{value / 1024f / 1024f:0.0} MB";
        if (value >= 1024L)
            return $"{value / 1024f:0.0} KB";

        return $"{value} B";
    }
}
