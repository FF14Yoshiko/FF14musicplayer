using System;
using System.Collections.Generic;

namespace AllTimeSoundTrigger.ConfigurationModels;

[Serializable]
public sealed class AutoSwitchConfiguration
{
    public bool Enabled { get; set; }

    public string TargetProfileId { get; set; } = string.Empty;

    public string FallbackProfileId { get; set; } = string.Empty;

    public List<int> TerritoryTypes { get; set; } = [];

    public void Normalize()
    {
        TargetProfileId = (TargetProfileId ?? string.Empty).Trim();
        FallbackProfileId = (FallbackProfileId ?? string.Empty).Trim();
        TerritoryTypes ??= [];
        TerritoryTypes.RemoveAll(id => id <= 0);
    }
}
