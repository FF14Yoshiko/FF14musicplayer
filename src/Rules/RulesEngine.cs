using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AllTimeSoundTrigger.Actions;
using AllTimeSoundTrigger.Core;

namespace AllTimeSoundTrigger.Rules;

public sealed record RulesEngineRuntimeSnapshot(
    int RuleCount,
    int EventIndexBucketCount,
    int GlobalRuleCount,
    string LastEventType,
    double LastEventElapsedMilliseconds,
    int LastCandidateRuleCount,
    int LastMatchedRuleCount,
    int LastTriggeredRuleCount,
    int LastCooldownSkippedCount,
    DateTime? LastEventAt);

public sealed class RulesEngine
{
    private readonly object gate = new();
    private readonly Dictionary<string, DateTime> lastTriggeredAt = new(StringComparer.Ordinal);
    private Rule[] rules = [];
    private Rule[] globalRules = [];
    private Dictionary<string, Rule[]> rulesByEventType = new(StringComparer.OrdinalIgnoreCase);
    private RulesEngineRuntimeSnapshot runtimeSnapshot = new(
        0,
        0,
        0,
        string.Empty,
        0,
        0,
        0,
        0,
        0,
        null);

    public IReadOnlyList<Rule> SnapshotRules()
    {
        lock (gate)
            return rules.ToArray();
    }

    public RulesEngineRuntimeSnapshot SnapshotRuntime()
    {
        lock (gate)
            return runtimeSnapshot;
    }

    public void ReplaceRules(IEnumerable<Rule> newRules)
    {
        var snapshot = newRules.ToArray();
        var global = new List<Rule>();
        var indexed = new Dictionary<string, List<Rule>>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in snapshot)
        {
            if (rule.Trigger is not IEventIndexedTrigger indexedTrigger || indexedTrigger.EventTypes.Count == 0)
            {
                global.Add(rule);
                continue;
            }

            foreach (var eventType in indexedTrigger.EventTypes
                         .Select(type => (type ?? string.Empty).Trim())
                         .Where(type => type.Length > 0)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!indexed.TryGetValue(eventType, out var bucket))
                {
                    bucket = [];
                    indexed[eventType] = bucket;
                }

                bucket.Add(rule);
            }
        }

        var nextIndex = indexed.ToDictionary(
            item => item.Key,
            item => item.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);

        lock (gate)
        {
            rules = snapshot;
            globalRules = global.ToArray();
            rulesByEventType = nextIndex;
            runtimeSnapshot = runtimeSnapshot with
            {
                RuleCount = snapshot.Length,
                EventIndexBucketCount = nextIndex.Count,
                GlobalRuleCount = globalRules.Length
            };
        }
    }

    public void HandleEvent(GameEvent gameEvent)
    {
        var startedAt = Stopwatch.GetTimestamp();
        Rule[] globalSnapshot;
        Rule[] eventSnapshot;
        lock (gate)
        {
            globalSnapshot = globalRules;
            eventSnapshot = rulesByEventType.TryGetValue(gameEvent.EventType, out var eventRules)
                ? eventRules
                : [];
        }

        var counters = HandleCandidateRules(globalSnapshot, gameEvent)
            + HandleCandidateRules(eventSnapshot, gameEvent);
        var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

        lock (gate)
        {
            runtimeSnapshot = runtimeSnapshot with
            {
                LastEventType = gameEvent.EventType,
                LastEventElapsedMilliseconds = elapsedMilliseconds,
                LastCandidateRuleCount = counters.CandidateRuleCount,
                LastMatchedRuleCount = counters.MatchedRuleCount,
                LastTriggeredRuleCount = counters.TriggeredRuleCount,
                LastCooldownSkippedCount = counters.CooldownSkippedCount,
                LastEventAt = gameEvent.Timestamp
            };
        }
    }

    private RuleRuntimeCounters HandleCandidateRules(IReadOnlyList<Rule> candidates, GameEvent gameEvent)
    {
        var matchedRuleCount = 0;
        var triggeredRuleCount = 0;
        var cooldownSkippedCount = 0;

        foreach (var rule in candidates)
        {
            if (!rule.Trigger.IsMatch(gameEvent))
                continue;

            matchedRuleCount++;

            if (rule.Conditions.Any(condition => !condition.Check()))
                continue;

            if (IsInCooldown(rule, gameEvent.Timestamp))
            {
                cooldownSkippedCount++;
                continue;
            }

            MarkTriggered(rule, gameEvent.Timestamp);
            triggeredRuleCount++;
            foreach (var action in rule.Actions)
            {
                if (action is IContextualAction contextualAction)
                    contextualAction.Execute(gameEvent);
                else
                    action.Execute();
            }
        }

        return new RuleRuntimeCounters(candidates.Count, matchedRuleCount, triggeredRuleCount, cooldownSkippedCount);
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

    private readonly record struct RuleRuntimeCounters(
        int CandidateRuleCount,
        int MatchedRuleCount,
        int TriggeredRuleCount,
        int CooldownSkippedCount)
    {
        public static RuleRuntimeCounters operator +(RuleRuntimeCounters left, RuleRuntimeCounters right)
            => new(
                left.CandidateRuleCount + right.CandidateRuleCount,
                left.MatchedRuleCount + right.MatchedRuleCount,
                left.TriggeredRuleCount + right.TriggeredRuleCount,
                left.CooldownSkippedCount + right.CooldownSkippedCount);
    }
}
