using System;
using System.Collections.Generic;
using System.Linq;

namespace AllTimeSoundTrigger.ConfigurationModels;

[Serializable]
public sealed class SoundLibraryConfiguration
{
    public List<SoundLibraryEntry> Entries { get; set; } = [];

    public SoundLibraryEntry? FindById(string? soundId)
    {
        var normalizedId = (soundId ?? string.Empty).Trim();
        if (normalizedId.Length == 0)
            return null;

        return Entries.FirstOrDefault(entry => string.Equals(entry.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
    }

    public bool Normalize()
    {
        var changed = false;
        Entries ??= [];
        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            if (Entries[i] == null)
            {
                Entries.RemoveAt(i);
                changed = true;
                continue;
            }

            changed |= Entries[i].Normalize();
        }

        return changed;
    }
}
