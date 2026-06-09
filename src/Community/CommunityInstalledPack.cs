using System;
using System.Collections.Generic;
using System.Linq;

namespace AllTimeSoundTrigger.Community;

public sealed class CommunityInstalledPack
{
    public string Id { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string> GroupIds { get; set; } = [];

    public List<string> SoundIds { get; set; } = [];

    public List<string> ImportDirectories { get; set; } = [];

    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.Now;

    public void Normalize()
    {
        Id = (Id ?? string.Empty).Trim();
        Version = (Version ?? string.Empty).Trim();
        Name = (Name ?? string.Empty).Trim();
        GroupIds = NormalizeList(GroupIds, StringComparer.Ordinal);
        SoundIds = NormalizeList(SoundIds, StringComparer.OrdinalIgnoreCase);
        ImportDirectories = NormalizeList(ImportDirectories, StringComparer.OrdinalIgnoreCase);
        if (Name.Length == 0)
            Name = Id;
    }

    private static List<string> NormalizeList(IEnumerable<string>? values, IEqualityComparer<string> comparer)
        => (values ?? [])
            .Select(value => (value ?? string.Empty).Trim())
            .Where(value => value.Length > 0)
            .Distinct(comparer)
            .ToList();
}

public sealed class CommunityInstalledPackStore
{
    public int Version { get; set; } = 1;

    public List<CommunityInstalledPack> Packs { get; set; } = [];

    public void Normalize()
    {
        if (Version <= 0)
            Version = 1;

        Packs ??= [];
        for (var i = Packs.Count - 1; i >= 0; i--)
        {
            if (Packs[i] == null)
            {
                Packs.RemoveAt(i);
                continue;
            }

            Packs[i].Normalize();
            if (string.IsNullOrWhiteSpace(Packs[i].Id))
                Packs.RemoveAt(i);
        }

        Packs = Packs
            .GroupBy(pack => pack.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
