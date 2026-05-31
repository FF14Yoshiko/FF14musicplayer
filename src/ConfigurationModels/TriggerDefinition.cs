using System;

namespace AllTimeSoundTrigger.ConfigurationModels;

[Serializable]
public sealed class TriggerDefinition
{
    public string Type { get; set; } = "EventType";

    public string EventType { get; set; } = string.Empty;

    public string ActorName { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;

    public string SkillNameContains { get; set; } = string.Empty;

    public string ItemNameContains { get; set; } = string.Empty;

    public int TerritoryType { get; set; }

    public int MapId { get; set; }

    public int ClassJobId { get; set; }

    public string JobNameContains { get; set; } = string.Empty;

    public int StatusId { get; set; }

    public string StatusNameContains { get; set; } = string.Empty;

    public int HpPercentBelow { get; set; } = 30;

    public bool? LocalPlayerOnly { get; set; }
}
