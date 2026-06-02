using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.ConfigurationModels;
using AllTimeSoundTrigger.Rules.Triggers;

namespace AllTimeSoundTrigger.Rules;

public sealed class TriggerFactory
{
    private readonly Dictionary<string, Func<TriggerDefinition, ITrigger>> registrations = new(StringComparer.OrdinalIgnoreCase);

    public TriggerFactory()
    {
        Register("EventType", definition => new EventTypeTrigger(definition.EventType));
        Register("CombatEntered", _ => new CombatEnteredTrigger());
        Register("CombatExited", _ => new CombatExitedTrigger());
        Register("HpChanged", _ => new HpChangedTrigger());
        Register("HpLow", definition => new HpLowTrigger(definition.HpPercentBelow));
        Register("ItemAcquired", definition => new ItemAcquiredTrigger(
            definition.ActorName,
            definition.ItemNameContains,
            definition.LocalPlayerOnly ?? true));
        Register("JobChanged", definition => new JobChangedTrigger(
            definition.ClassJobId,
            definition.JobNameContains));
        Register("Kill", definition => new KillTrigger(
            definition.ActorName,
            definition.TargetName,
            definition.LocalPlayerOnly ?? false));
        Register("LocalPlayerDefeated", _ => new EventTypeTrigger("LocalPlayerDefeated"));
        Register("MapChanged", definition => new MapChangedTrigger(
            definition.TerritoryType,
            definition.MapId));
        Register("StatusGained", definition => new StatusGainedTrigger(
            definition.StatusId,
            definition.StatusNameContains));
        Register("StatusLost", definition => new StatusLostTrigger(
            definition.StatusId,
            definition.StatusNameContains));
        Register("SkillUsed", definition => new SkillUsedTrigger(
            definition.ActorName,
            definition.SkillNameContains,
            definition.LocalPlayerOnly ?? false));
    }

    public void Register(string type, Func<TriggerDefinition, ITrigger> factory)
    {
        registrations[type] = factory;
    }

    public ITrigger Create(TriggerDefinition definition)
    {
        var type = string.IsNullOrWhiteSpace(definition.Type) ? "EventType" : definition.Type.Trim();
        if (!registrations.TryGetValue(type, out var factory))
            throw new InvalidOperationException($"未知触发器类型：{type}");

        return factory(definition);
    }
}
