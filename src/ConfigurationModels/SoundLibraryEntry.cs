using System;
using System.IO;
using AllTimeSoundTrigger.Utilities;

namespace AllTimeSoundTrigger.ConfigurationModels;

[Serializable]
public sealed class SoundLibraryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public float DefaultVolume { get; set; } = 1f;

    public int Priority { get; set; }

    public bool InterruptLowerPriority { get; set; } = true;

    public bool Normalize()
    {
        var changed = false;
        var trimmedId = (Id ?? string.Empty).Trim();
        if (trimmedId.Length == 0)
            trimmedId = (Name ?? string.Empty).Trim();
        if (trimmedId.Length == 0)
            trimmedId = Path.GetFileNameWithoutExtension((FilePath ?? string.Empty).Trim());
        if (trimmedId.Length == 0)
            trimmedId = Guid.NewGuid().ToString("N");
        if (Id != trimmedId)
        {
            Id = trimmedId;
            changed = true;
        }

        var trimmedName = (Name ?? string.Empty).Trim();
        if (trimmedName.Length == 0)
            trimmedName = Path.GetFileNameWithoutExtension((FilePath ?? string.Empty).Trim());
        if (trimmedName.Length == 0)
            trimmedName = "未命名音效";
        if (Name != trimmedName)
        {
            Name = trimmedName;
            changed = true;
        }

        var trimmedPath = FilePathText.Normalize(FilePath);
        if (FilePath != trimmedPath)
        {
            FilePath = trimmedPath;
            changed = true;
        }

        var clampedVolume = Math.Clamp(DefaultVolume, 0f, 1f);
        if (Math.Abs(DefaultVolume - clampedVolume) > 0.0001f)
        {
            DefaultVolume = clampedVolume;
            changed = true;
        }

        var clampedPriority = Math.Clamp(Priority, -100, 100);
        if (Priority != clampedPriority)
        {
            Priority = clampedPriority;
            changed = true;
        }

        return changed;
    }
}
