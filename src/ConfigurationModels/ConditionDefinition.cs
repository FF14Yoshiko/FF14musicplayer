using System;

namespace AllTimeSoundTrigger.ConfigurationModels;

[Serializable]
public sealed class ConditionDefinition
{
    public string Type { get; set; } = "Always";
}
