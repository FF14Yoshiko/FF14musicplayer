using System;
using System.Collections.Generic;
using System.Linq;

namespace AllTimeSoundTrigger.Community;

public sealed class CommunityPackIndex
{
    public int Version { get; set; } = 2;

    public string UpdatedAt { get; set; } = string.Empty;

    public List<CommunityPackInfo> Packs { get; set; } = [];

    public void Normalize()
    {
        if (Version <= 0)
            Version = 1;

        Packs ??= [];
        foreach (var pack in Packs)
            pack.Normalize();

        Packs = Packs
            .Where(pack => !string.IsNullOrWhiteSpace(pack.Id) && !string.IsNullOrWhiteSpace(pack.PackageUrl))
            .GroupBy(pack => pack.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
