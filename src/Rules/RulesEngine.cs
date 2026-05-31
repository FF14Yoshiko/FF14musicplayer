using System;
using System.Collections.Generic;
using System.Linq;
using AllTimeSoundTrigger.Actions;
using AllTimeSoundTrigger.Core;

namespace AllTimeSoundTrigger.Rules;

public sealed class RulesEngine
{
    private readonly object gate = new();
    private List<Rule> rules = new();
    private readonly Dictionary<string, DateTime> lastTriggeredAt = new(StringComparer.Ordinal);

    public IReadOnlyList<Rule> SnapshotRules()
    {
        lock (gate)
            return rules.ToArray();
    }

    public void ReplaceRules(IEnumerable<Rule> newRules)
    {
        lock (gate)
            rules = newRules.ToList();
    }

    public void HandleEvent(GameEvent gameEvent)
    {
        Rule[] snapshot;
        lock (gate)
            snapshot = rules.ToArray();

        foreach (var rule in snapshot)
        {
            if (!rule.Trigger.IsMatch(gameEvent))
                continue;

            if (rule.Conditions.Any(condition => !condition.Check()))
                continue;

            if (IsInCooldown(rule, gameEvent.Timestamp))
                continue;

            MarkTriggered(rule, gameEvent.Timestamp);
            foreach (var action in rule.Actions)
            {
                if (action is IContextualAction contextualAction)
                    contextualAction.Execute(gameEvent);
                else
                    action.Execute();
            }
        }
    }

    private bool IsInCooldown(Rule rule, DateTime now)
    {
        if (rule.CooldownSeconds <= 0)
            return false;

        lock (gate)
        {
            return lastTriggeredAt.TryGetValue(rule.Id, out var last)
                && (now - last).TotalSeconds < rule.CooldownSeconds;
        }
    }

    private void MarkTriggered(Rule rule, DateTime now)
    {
        if (rule.CooldownSeconds <= 0)
            return;

        lock (gate)
            lastTriggeredAt[rule.Id] = now;
    }
}
