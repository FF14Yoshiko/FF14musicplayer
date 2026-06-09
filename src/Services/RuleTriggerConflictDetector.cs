using System;
using System.Collections.Generic;
using System.Linq;
using AllTimeSoundTrigger.Community;
using AllTimeSoundTrigger.ConfigurationModels;

namespace AllTimeSoundTrigger.Services;

public static class RuleTriggerConflictDetector
{
    public static RuleTriggerConflictReport Detect(
        ProfileDefinition incomingProfile,
        ProfileDefinition existingProfile,
        IReadOnlyList<CommunityInstalledPack> installedPacks,
        string excludedPackId = "",
        int maxConflicts = 8)
    {
        if (incomingProfile == null || existingProfile == null)
            return RuleTriggerConflictReport.Empty;

        maxConflicts = Math.Max(1, maxConflicts);
        var excludedGroupIds = BuildExcludedGroupIds(installedPacks, excludedPackId);
        var ownerByGroupId = BuildCommunityOwnerMap(installedPacks, excludedPackId);
        var existingSignatures = EnumerateEnabledRules(existingProfile, excludedGroupIds, ownerByGroupId)
            .SelectMany(rule => BuildTriggerSignatures(rule.Rule.Trigger)
                .Select(signature => new IndexedTriggerSignature(signature, rule)))
            .GroupBy(item => item.Signature.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var conflicts = new List<RuleTriggerConflict>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var total = 0;

        foreach (var incoming in EnumerateEnabledRules(
                     incomingProfile,
                     new HashSet<string>(StringComparer.Ordinal),
                     new Dictionary<string, CommunityRuleSource>(StringComparer.Ordinal)))
        {
            foreach (var incomingSignature in BuildTriggerSignatures(incoming.Rule.Trigger))
            {
                if (!existingSignatures.TryGetValue(incomingSignature.Category, out var candidates))
                    continue;

                foreach (var existing in candidates)
                {
                    if (!CanTriggerTogether(incomingSignature, existing.Signature))
                        continue;

                    var dedupeKey = $"{incoming.Rule.Id}|{existing.Rule.Rule.Id}|{incomingSignature.Category}|{incomingSignature.Key}|{existing.Signature.Key}";
                    if (!seen.Add(dedupeKey))
                        continue;

                    total++;
                    if (conflicts.Count >= maxConflicts)
                        continue;

                    conflicts.Add(new RuleTriggerConflict(
                        existing.Rule.SourceName,
                        existing.Rule.SourceIsCommunityPack,
                        existing.Rule.Group.Name,
                        existing.Rule.Rule.Name,
                        incoming.Rule.Name,
                        ChooseConflictDescription(incomingSignature, existing.Signature),
                        incomingSignature.Reason));
                }
            }
        }

        return new RuleTriggerConflictReport(total, conflicts, total > conflicts.Count);
    }

    private static IEnumerable<IndexedRuleDefinition> EnumerateEnabledRules(
        ProfileDefinition profile,
        IReadOnlySet<string> excludedGroupIds,
        IReadOnlyDictionary<string, CommunityRuleSource> ownerByGroupId)
    {
        foreach (var group in profile.Groups ?? [])
        {
            if (!group.Enabled || excludedGroupIds.Contains(group.Id))
                continue;

            var source = ownerByGroupId.TryGetValue(group.Id, out var owner)
                ? owner
                : new CommunityRuleSource(group.Name, false);
            foreach (var rule in group.Rules ?? [])
            {
                if (!rule.Enabled)
                    continue;

                yield return new IndexedRuleDefinition(group, rule, source.Name, source.IsCommunityPack);
            }
        }
    }

    private static IReadOnlySet<string> BuildExcludedGroupIds(
        IReadOnlyList<CommunityInstalledPack> installedPacks,
        string excludedPackId)
    {
        if (string.IsNullOrWhiteSpace(excludedPackId))
            return new HashSet<string>(StringComparer.Ordinal);

        return installedPacks
            .Where(pack => pack.Id.Equals(excludedPackId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(pack => pack.GroupIds)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, CommunityRuleSource> BuildCommunityOwnerMap(
        IReadOnlyList<CommunityInstalledPack> installedPacks,
        string excludedPackId)
    {
        var owners = new Dictionary<string, CommunityRuleSource>(StringComparer.Ordinal);
        foreach (var pack in installedPacks)
        {
            if (!string.IsNullOrWhiteSpace(excludedPackId)
                && pack.Id.Equals(excludedPackId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var groupId in pack.GroupIds)
                owners[groupId] = new CommunityRuleSource(pack.Name, true);
        }

        return owners;
    }

    private static IEnumerable<TriggerSignature> BuildTriggerSignatures(TriggerDefinition? trigger)
    {
        if (trigger == null)
            yield break;

        switch (NormalizeKey(trigger.Type))
        {
            case "skillused":
            {
                var skill = TrimText(trigger.SkillNameContains);
                yield return CreateSignature(
                    "SkillUsed",
                    skill,
                    $"技能「{ValueOrAny(skill)}」",
                    "同一个技能");
                break;
            }
            case "statusgained":
            case "statuslost":
            {
                var gained = NormalizeKey(trigger.Type).Equals("statusgained", StringComparison.Ordinal);
                var status = BuildStatusKey(trigger);
                yield return new TriggerSignature(
                    gained ? "StatusGained" : "StatusLost",
                    status.Key,
                    status.IsBroad,
                    gained ? $"获得 Buff「{status.Label}」" : $"Buff 消失「{status.Label}」",
                    "同一个 Buff");
                break;
            }
            case "eventtype":
            {
                var eventType = TrimText(trigger.EventType);
                yield return CreateSignature(
                    "Event",
                    eventType,
                    $"事件「{ValueOrAny(eventType)}」",
                    "同一个事件");
                break;
            }
            case "localplayerdefeated":
                yield return new TriggerSignature("Event", "localplayerdefeated", false, "事件「本机玩家被击倒」", "同一个事件");
                break;
            case "combatentered":
                yield return new TriggerSignature("Event", "combatentered", false, "事件「进入战斗」", "同一个事件");
                break;
            case "combatexited":
                yield return new TriggerSignature("Event", "combatexited", false, "事件「脱离战斗」", "同一个事件");
                break;
            case "hpchanged":
                yield return new TriggerSignature("Event", "hpchanged", false, "事件「血量变化」", "同一个事件");
                break;
            case "hplow":
                yield return new TriggerSignature("HpLow", "*", true, $"血量低于 {Math.Clamp(trigger.HpPercentBelow, 1, 100)}%", "同一个血量阈值事件");
                break;
            case "mapchanged":
            {
                var map = BuildNumberKey("territory", trigger.TerritoryType, "map", trigger.MapId);
                yield return new TriggerSignature("MapChanged", map.Key, map.IsBroad, $"地图切换「{map.Label}」", "同一个地图事件");
                break;
            }
            case "jobchanged":
            {
                var job = BuildNumberOrTextKey("job", trigger.ClassJobId, trigger.JobNameContains);
                yield return new TriggerSignature("JobChanged", job.Key, job.IsBroad, $"职业切换「{job.Label}」", "同一个职业事件");
                break;
            }
            case "itemacquired":
            {
                var itemName = TrimText(trigger.ItemNameContains);
                yield return CreateSignature(
                    "ItemAcquired",
                    itemName,
                    $"获得物品「{ValueOrAny(itemName)}」",
                    "同一个物品事件");
                break;
            }
            case "kill":
            {
                var target = TrimText(trigger.TargetName);
                var actor = TrimText(trigger.ActorName);
                var key = target.Length > 0 ? target : actor;
                var label = target.Length > 0
                    ? $"击杀目标「{target}」"
                    : actor.Length > 0
                        ? $"击杀者「{actor}」"
                        : "击杀事件「任意」";
                yield return CreateSignature("Kill", key, label, "同一个击杀事件");
                break;
            }
            default:
            {
                var type = TrimText(trigger.Type);
                yield return CreateSignature(
                    $"Unknown:{NormalizeKey(type)}",
                    string.Empty,
                    string.IsNullOrWhiteSpace(type) ? "未知触发器" : $"触发器「{type}」",
                    "同一个触发器");
                break;
            }
        }
    }

    private static TriggerSignature CreateSignature(string category, string key, string description, string reason)
    {
        var normalizedKey = NormalizeKey(key);
        return new TriggerSignature(
            category,
            normalizedKey.Length == 0 ? "*" : normalizedKey,
            normalizedKey.Length == 0,
            description,
            reason);
    }

    private static bool CanTriggerTogether(TriggerSignature incoming, TriggerSignature existing)
        => incoming.Category.Equals(existing.Category, StringComparison.OrdinalIgnoreCase)
           && (incoming.IsBroad
               || existing.IsBroad
               || incoming.Key.Equals(existing.Key, StringComparison.OrdinalIgnoreCase));

    private static string ChooseConflictDescription(TriggerSignature incoming, TriggerSignature existing)
        => incoming.IsBroad && !existing.IsBroad ? existing.Description : incoming.Description;

    private static (string Key, string Label, bool IsBroad) BuildStatusKey(TriggerDefinition trigger)
    {
        if (trigger.StatusId > 0)
        {
            var name = TrimText(trigger.StatusNameContains);
            var label = name.Length > 0 ? $"#{trigger.StatusId} {name}" : $"#{trigger.StatusId}";
            return ($"id:{trigger.StatusId}", label, false);
        }

        var statusName = TrimText(trigger.StatusNameContains);
        return statusName.Length > 0
            ? ($"name:{NormalizeKey(statusName)}", statusName, false)
            : ("*", "任意", true);
    }

    private static (string Key, string Label, bool IsBroad) BuildNumberOrTextKey(string numberPrefix, int number, string text)
    {
        if (number > 0)
            return ($"{numberPrefix}:{number}", $"#{number}", false);

        var normalizedText = TrimText(text);
        return normalizedText.Length > 0
            ? ($"name:{NormalizeKey(normalizedText)}", normalizedText, false)
            : ("*", "任意", true);
    }

    private static (string Key, string Label, bool IsBroad) BuildNumberKey(
        string firstPrefix,
        int first,
        string secondPrefix,
        int second)
    {
        if (first > 0)
            return ($"{firstPrefix}:{first}", $"#{first}", false);
        if (second > 0)
            return ($"{secondPrefix}:{second}", $"#{second}", false);

        return ("*", "任意", true);
    }

    private static string ValueOrAny(string value)
        => string.IsNullOrWhiteSpace(value) ? "任意" : value;

    private static string TrimText(string? value)
        => (value ?? string.Empty).Trim();

    private static string NormalizeKey(string? value)
    {
        var trimmed = TrimText(value);
        if (trimmed.Length == 0)
            return string.Empty;

        return string.Join(' ', trimmed.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }

    private sealed record CommunityRuleSource(string Name, bool IsCommunityPack);

    private sealed record IndexedRuleDefinition(
        RuleGroupDefinition Group,
        RuleDefinition Rule,
        string SourceName,
        bool SourceIsCommunityPack);

    private sealed record IndexedTriggerSignature(TriggerSignature Signature, IndexedRuleDefinition Rule);

    private readonly record struct TriggerSignature(
        string Category,
        string Key,
        bool IsBroad,
        string Description,
        string Reason);
}

public sealed record RuleTriggerConflictReport(
    int TotalConflictCount,
    IReadOnlyList<RuleTriggerConflict> Conflicts,
    bool HasMoreConflicts)
{
    public static RuleTriggerConflictReport Empty { get; } = new(0, [], false);

    public bool HasConflicts => TotalConflictCount > 0;
}

public sealed record RuleTriggerConflict(
    string ExistingSourceName,
    bool ExistingSourceIsCommunityPack,
    string ExistingGroupName,
    string ExistingRuleName,
    string IncomingRuleName,
    string TriggerDescription,
    string Reason);
