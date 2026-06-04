using System;
using System.Collections.Generic;

namespace AllTimeSoundTrigger.ConfigurationModels;

[Serializable]
public sealed class RuleDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public double CooldownSeconds { get; set; }

    public TriggerDefinition Trigger { get; set; } = new();

    public List<ConditionDefinition> Conditions { get; set; } = [];

    public List<ActionDefinition> Actions { get; set; } = [];

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(Name))
            Name = "未命名规则";

        Trigger ??= new TriggerDefinition();
        Conditions ??= [];
        Actions ??= [];
        CooldownSeconds = Math.Max(0, CooldownSeconds);

        foreach (var action in Actions)
        {
            action.SoundIds ??= [];
            action.FilePaths ??= [];
            if (action.StopOnStatusLost)
                action.Loop = true;
        }
    }

    public static RuleDefinition CreateDefaultSkillLogRule()
        => new()
        {
            Id = "debug-visible-skill-used",
            Name = "调试：可见角色使用技能",
            Enabled = true,
            CooldownSeconds = 0.5,
            Trigger = new TriggerDefinition
            {
                Type = "SkillUsed",
                LocalPlayerOnly = false
            },
            Conditions = [],
            Actions =
            [
                new ActionDefinition
                {
                    Type = "Log",
                    Message = "规则命中：检测到可见技能使用事件。"
                }
            ]
        };
}
