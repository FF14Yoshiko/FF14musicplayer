using System;
using System.Collections.Generic;

namespace AllTimeSoundTrigger.ConfigurationModels;

[Serializable]
public sealed class RuleGroupDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public List<RuleDefinition> Rules { get; set; } = [];

    public void Normalize(string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(Name))
            Name = fallbackName;

        Rules ??= [];
        foreach (var rule in Rules)
            rule.Normalize();
    }
}
