using System;
using System.Collections.Generic;
using System.Linq;

namespace AllTimeSoundTrigger.ConfigurationModels;

[Serializable]
public sealed class ProfileDefinition
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public List<RuleGroupDefinition> Groups { get; set; } = [];

    public IEnumerable<RuleDefinition> EnumerateRules()
        => Groups.SelectMany(group => group.Rules);

    public IEnumerable<RuleDefinition> EnumerateRuntimeRules()
        => Groups
            .Where(group => group.Enabled)
            .SelectMany(group => group.Rules)
            .Where(rule => rule.Enabled);

    public RuleGroupDefinition GetOrCreateDefaultGroup()
    {
        Groups ??= [];
        if (Groups.Count == 0)
        {
            Groups.Add(new RuleGroupDefinition
            {
                Name = "默认分组",
                Rules = []
            });
        }

        return Groups[0];
    }

    public void Normalize()
    {
        if (Version != CurrentVersion)
            Version = CurrentVersion;

        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(Name))
            Name = "未命名方案";

        Groups ??= [];
        if (Groups.Count == 0)
            GetOrCreateDefaultGroup();

        for (var i = 0; i < Groups.Count; i++)
            Groups[i].Normalize(i == 0 ? "默认分组" : $"分组 {i + 1}");
    }

    public static ProfileDefinition CreateDefault(IEnumerable<RuleDefinition> rules)
        => new()
        {
            Id = "default",
            Name = "默认方案",
            Groups =
            [
                new RuleGroupDefinition
                {
                    Id = "default-group",
                    Name = "默认分组",
                    Rules = rules.ToList()
                }
            ]
        };
}
