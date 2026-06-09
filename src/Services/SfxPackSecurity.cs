using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace AllTimeSoundTrigger.Services;

internal static class SfxPackSecurity
{
    public const int MaxEntryCount = 512;
    public const int MaxSoundCount = 256;
    public const long MaxUncompressedBytes = 200L * 1024L * 1024L;
    public const long MaxTotalSoundBytes = 150L * 1024L * 1024L;
    public const long MaxSingleSoundBytes = 50L * 1024L * 1024L;
    public const long MaxJsonEntryBytes = 5L * 1024L * 1024L;
    public const long MaxReadmeBytes = 1L * 1024L * 1024L;
    public const long MaxCoverBytes = 2L * 1024L * 1024L;

    public static readonly string[] AllowedSoundExtensions = [".mp3", ".wav", ".ogg"];
    public static readonly string[] AllowedCoverExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    public static void ValidateArchive(ZipArchive archive)
    {
        var entries = archive.Entries
            .Where(entry => !IsDirectory(entry))
            .ToArray();
        if (entries.Length > MaxEntryCount)
            throw new InvalidOperationException($"分享包文件数量过多：{entries.Length}/{MaxEntryCount}。");

        long totalBytes = 0;
        long soundBytes = 0;
        var soundCount = 0;
        foreach (var entry in entries)
        {
            var path = NormalizeZipPath(entry.FullName);
            if (!IsSafeZipPath(path))
                throw new InvalidOperationException($"分享包里包含非法路径：{entry.FullName}");

            totalBytes += Math.Max(0, entry.Length);
            if (totalBytes > MaxUncompressedBytes)
                throw new InvalidOperationException($"分享包解压后过大：超过 {FormatBytes(MaxUncompressedBytes)}。");

            ValidateEntry(path, entry, ref soundCount, ref soundBytes);
        }
    }

    public static long ExtractToFileLimited(ZipArchiveEntry entry, string destinationPath, long maxBytes)
    {
        var tempPath = $"{destinationPath}.tmp";
        try
        {
            long total = 0;
            using (var input = entry.Open())
            using (var output = File.Create(tempPath))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var read = input.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                        break;

                    total += read;
                    if (total > maxBytes)
                        throw new InvalidOperationException($"解压文件超过限制：{entry.FullName}");

                    output.Write(buffer, 0, read);
                }
            }

            File.Move(tempPath, destinationPath, true);
            return total;
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    public static string NormalizeZipPath(string path)
        => (path ?? string.Empty).Replace('\\', '/').TrimStart('/');

    public static bool IsSafeZipPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains(':'))
            return false;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts.All(part => part != "..");
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
                throw new InvalidOperationException($"分享包音效数量过多：{soundCount}/{MaxSoundCount}。");

            soundBytes += entry.Length;
            if (soundBytes > MaxTotalSoundBytes)
                throw new InvalidOperationException($"分享包音效总体积过大：超过 {FormatBytes(MaxTotalSoundBytes)}。");
            return;
        }

        if (path.StartsWith("covers/", StringComparison.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(path);
            if (!AllowedCoverExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"封面格式不支持：{entry.FullName}");
            ValidateEntrySize(entry, MaxCoverBytes);
            return;
        }

        throw new InvalidOperationException($"分享包内含不允许的文件：{entry.FullName}");
    }

    private static void ValidateSoundEntry(string path, ZipArchiveEntry entry)
    {
        var extension = Path.GetExtension(path);
        if (!AllowedSoundExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"音效文件格式不支持：{entry.FullName}");
        if (entry.Length <= 0)
            throw new InvalidOperationException($"音效文件为空：{entry.FullName}");
        ValidateEntrySize(entry, MaxSingleSoundBytes);
        if (!HasExpectedSoundHeader(entry, extension))
            throw new InvalidOperationException($"音效文件头不匹配扩展名：{entry.FullName}");
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

    private static bool IsKnownTextEntry(string path)
        => path.Equals("profile.json", StringComparison.OrdinalIgnoreCase)
           || path.Equals("sound-library.json", StringComparison.OrdinalIgnoreCase)
           || path.Equals("submission.json", StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectory(ZipArchiveEntry entry)
        => entry.FullName.EndsWith("/", StringComparison.Ordinal);

    private static void ValidateEntrySize(ZipArchiveEntry entry, long maxBytes)
    {
        if (entry.Length > maxBytes)
            throw new InvalidOperationException($"文件超过限制：{entry.FullName} / {FormatBytes(entry.Length)}。");
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
            // Best-effort cleanup for failed extraction.
        }
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
