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

public sealed partial class SfxPackService
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

    public bool TryDeleteImportedDirectory(string directoryPath)
    {
        var normalizedPath = FilePathText.Normalize(directoryPath);
        if (normalizedPath.Length == 0 || !Directory.Exists(normalizedPath))
            return false;

        var root = Path.GetFullPath(ImportSoundDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(normalizedPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
            || !fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Directory.Delete(fullPath, true);
        return true;
    }



}
