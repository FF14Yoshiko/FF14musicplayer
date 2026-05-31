using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.Actions;

namespace AllTimeSoundTrigger.Rules;

public sealed class Rule
{
    public Rule(
        string id,
        double cooldownSeconds,
        ITrigger trigger,
        IEnumerable<ICondition>? conditions,
        IEnumerable<IAction> actions)
    {
        Id = id;
        CooldownSeconds = Math.Max(0, cooldownSeconds);
        Trigger = trigger;
        Conditions = new List<ICondition>(conditions ?? []);
        Actions = new List<IAction>(actions);
    }

    public string Id { get; }

    public double CooldownSeconds { get; }

    public ITrigger Trigger { get; }

    public IReadOnlyList<ICondition> Conditions { get; }

    public IReadOnlyList<IAction> Actions { get; }
}
