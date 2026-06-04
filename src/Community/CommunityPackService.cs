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
    private readonly Dictionary<string, string> installedVersions = new(StringComparer.OrdinalIgnoreCase);
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

        return Path.Combine(CoverDirectory, $"{MakeSafeFileName(pack.Id)}{extension}");
    }

    public async Task<string> EnsureCoverCachedAsync(CommunityPackInfo pack, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(pack.CoverUrl))
            return string.Empty;

        var path = GetCoverCachePath(pack);
        if (File.Exists(path))
            return path;

        Directory.CreateDirectory(CoverDirectory);
        await DownloadFileAsync(pack.CoverUrl, path, MaxCoverBytes, cancellationToken).ConfigureAwait(false);
        return path;
    }

    public async Task<string> DownloadPackageAsync(CommunityPackInfo pack, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(pack.PackageUrl))
            throw new InvalidOperationException("这个音效包缺少下载地址。");
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
            return installedVersions.TryGetValue(pack.Id, out var version)
                && version.Equals(pack.Version, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void MarkInstalled(CommunityPackInfo pack)
    {
        lock (gate)
            installedVersions[pack.Id] = pack.Version;

        SaveInstalled();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
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

    private static async Task<bool> VerifyPackageHashAsync(string path, CommunityPackInfo pack, CancellationToken cancellationToken)
    {
        var expected = (pack.Sha256 ?? string.Empty).Trim();
        if (expected.Length == 0)
            return true;

        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        return hash.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private void LoadInstalled()
    {
        if (!File.Exists(InstalledCachePath))
            return;

        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(InstalledCachePath),
                SerializerOptions);
            if (values == null)
                return;

            installedVersions.Clear();
            foreach (var item in values)
            {
                var id = (item.Key ?? string.Empty).Trim();
                var version = (item.Value ?? string.Empty).Trim();
                if (id.Length > 0)
                    installedVersions[id] = version;
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
        Dictionary<string, string> snapshot;
        lock (gate)
            snapshot = new Dictionary<string, string>(installedVersions, StringComparer.OrdinalIgnoreCase);

        File.WriteAllText(InstalledCachePath, JsonSerializer.Serialize(snapshot, SerializerOptions));
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(CommunityPackService));
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
