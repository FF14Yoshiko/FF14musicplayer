using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AllTimeSoundTrigger.ConfigurationModels;
using AllTimeSoundTrigger.Utilities;

namespace AllTimeSoundTrigger.Services;

public sealed partial class SfxPackService
{
    private const float LoudVolumeWarningThreshold = 0.95f;
    private const long LargeSingleSoundWarningBytes = 10L * 1024L * 1024L;
    private const long LargeTotalSoundWarningBytes = 30L * 1024L * 1024L;

    public SfxPackSubmissionPreflightResult ValidateSubmissionPreflight(
        ProfileDefinition sourceProfile,
        IReadOnlyCollection<string> selectedGroupIds,
        IReadOnlyCollection<string> selectedRuleIds,
        SoundLibraryConfiguration soundLibrary,
        string coverPath)
    {
        var issues = new List<SfxPackSubmissionPreflightIssue>();
        var exportProfile = CreateSelectedProfile(sourceProfile, selectedGroupIds, selectedRuleIds);
        var selectedRules = exportProfile.Groups
            .SelectMany(group => group.Rules.Select(rule => (Group: group, Rule: rule)))
            .ToArray();
        if (selectedRules.Length == 0)
        {
            AddIssue(issues, SfxPackSubmissionPreflightSeverity.Error, "没有可投稿规则", "请至少选择一条规则后再生成投稿包。");
            return new SfxPackSubmissionPreflightResult(0, 0, 0, issues);
        }

        var stopKeys = selectedRules
            .SelectMany(item => item.Rule.Actions)
            .Where(action => action.Type.Equals("StopSound", StringComparison.OrdinalIgnoreCase))
            .Select(action => NormalizeKey(action.PlaybackKey))
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exportedSoundPaths = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var soundActionCount = 0;

        foreach (var item in selectedRules)
        {
            AddTriggerPreflightIssues(issues, item.Group.Name, item.Rule);

            foreach (var action in item.Rule.Actions)
            {
                if (!action.Type.Equals("Sound", StringComparison.OrdinalIgnoreCase))
                    continue;

                soundActionCount++;
                AddLoopIssues(issues, item.Group.Name, item.Rule, action, stopKeys);
                var soundReferences = ResolveSoundReferences(action, soundLibrary);
                if (soundReferences.Count == 0)
                {
                    AddRuleIssue(
                        issues,
                        SfxPackSubmissionPreflightSeverity.Error,
                        "音频缺失",
                        item.Group.Name,
                        item.Rule.Name,
                        "这个动作没有选择音效，也没有填写音频文件路径。");
                    continue;
                }

                foreach (var soundRef in soundReferences)
                    AddSoundReferenceIssues(issues, item.Group.Name, item.Rule.Name, soundRef, exportedSoundPaths);

                AddVolumeIssues(issues, item.Group.Name, item.Rule.Name, soundReferences);
            }
        }

        if (soundActionCount == 0)
            AddIssue(issues, SfxPackSubmissionPreflightSeverity.Error, "没有音效动作", "投稿包里没有会播放音频的动作，社区审核通常不会收录纯日志规则。");

        AddSoundSizeIssues(issues, exportedSoundPaths);
        AddCoverIssues(issues, coverPath);

        return new SfxPackSubmissionPreflightResult(
            selectedRules.Length,
            exportedSoundPaths.Count,
            exportedSoundPaths.Values.Sum(),
            issues);
    }

    private static string BuildSubmissionPreflightFailedMessage(SfxPackSubmissionPreflightResult preflight)
    {
        var firstError = preflight.Issues.FirstOrDefault(issue => issue.Severity == SfxPackSubmissionPreflightSeverity.Error);
        return firstError == null
            ? "投稿前自检未通过。"
            : $"投稿前自检未通过：{firstError.Title}。";
    }

    private static void AddSoundReferenceIssues(
        List<SfxPackSubmissionPreflightIssue> issues,
        string groupName,
        string ruleName,
        SoundExportReference soundRef,
        IDictionary<string, long> exportedSoundPaths)
    {
        if (string.IsNullOrWhiteSpace(soundRef.SourcePath) || !File.Exists(soundRef.SourcePath))
        {
            AddRuleIssue(
                issues,
                SfxPackSubmissionPreflightSeverity.Error,
                "音频缺失",
                groupName,
                ruleName,
                $"找不到音效「{soundRef.DisplayName}」对应的文件，请先在音效库里修好路径。");
            return;
        }

        var extension = Path.GetExtension(soundRef.SourcePath);
        if (!SfxPackSecurity.AllowedSoundExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            AddRuleIssue(
                issues,
                SfxPackSubmissionPreflightSeverity.Error,
                "音频格式不支持",
                groupName,
                ruleName,
                $"「{Path.GetFileName(soundRef.SourcePath)}」不是 mp3、wav 或 ogg，安装时会被安全校验拒绝。");
        }

        var fullPath = Path.GetFullPath(soundRef.SourcePath);
        if (exportedSoundPaths.ContainsKey(fullPath))
            return;

        var length = new FileInfo(fullPath).Length;
        exportedSoundPaths[fullPath] = length;
        if (length > SfxPackSecurity.MaxSingleSoundBytes)
        {
            AddRuleIssue(
                issues,
                SfxPackSubmissionPreflightSeverity.Error,
                "单个音频过大",
                groupName,
                ruleName,
                $"「{Path.GetFileName(soundRef.SourcePath)}」大小为 {FormatPreflightBytes(length)}，超过单文件限制 {FormatPreflightBytes(SfxPackSecurity.MaxSingleSoundBytes)}。");
        }
        else if (length > LargeSingleSoundWarningBytes)
        {
            AddRuleIssue(
                issues,
                SfxPackSubmissionPreflightSeverity.Warning,
                "单个音频偏大",
                groupName,
                ruleName,
                $"「{Path.GetFileName(soundRef.SourcePath)}」大小为 {FormatPreflightBytes(length)}，建议压缩后再投稿。");
        }
    }

    private static void AddVolumeIssues(
        List<SfxPackSubmissionPreflightIssue> issues,
        string groupName,
        string ruleName,
        IReadOnlyList<SoundExportReference> soundReferences)
    {
        foreach (var soundRef in soundReferences.Where(soundRef => soundRef.DefaultVolume >= LoudVolumeWarningThreshold))
        {
            AddRuleIssue(
                issues,
                SfxPackSubmissionPreflightSeverity.Warning,
                "音量可能过大",
                groupName,
                ruleName,
                $"音效「{soundRef.Name}」音量为 {soundRef.DefaultVolume:P0}，建议先降到 80% 左右，避免安装后吓到轻度用户。");
        }
    }

    private static void AddLoopIssues(
        List<SfxPackSubmissionPreflightIssue> issues,
        string groupName,
        RuleDefinition rule,
        ActionDefinition action,
        IReadOnlySet<string> stopKeys)
    {
        if (!action.Loop && !action.StopOnStatusLost)
            return;

        if (action.StopOnStatusLost)
        {
            if (!rule.Trigger.Type.Equals("StatusGained", StringComparison.OrdinalIgnoreCase))
            {
                AddRuleIssue(
                    issues,
                    SfxPackSubmissionPreflightSeverity.Error,
                    "循环停止条件无效",
                    groupName,
                    rule.Name,
                    "勾选了“Buff 消失时自动停止”，但触发器不是“获得 Buff”。");
            }

            return;
        }

        var playbackKey = NormalizeKey(action.PlaybackKey);
        if (playbackKey.Length == 0)
        {
            AddRuleIssue(
                issues,
                SfxPackSubmissionPreflightSeverity.Error,
                "循环音效没有停止条件",
                groupName,
                rule.Name,
                "循环播放需要填写播放标识，并提供对应的“停止指定音效”规则。");
            return;
        }

        if (!stopKeys.Contains(playbackKey))
        {
            AddRuleIssue(
                issues,
                SfxPackSubmissionPreflightSeverity.Error,
                "循环音效没有停止条件",
                groupName,
                rule.Name,
                $"播放标识「{action.PlaybackKey}」没有在本投稿包内找到对应的“停止指定音效”动作。");
        }
    }

    private static void AddTriggerPreflightIssues(
        List<SfxPackSubmissionPreflightIssue> issues,
        string groupName,
        RuleDefinition rule)
    {
        var trigger = rule.Trigger;
        var type = NormalizeKey(trigger.Type);
        switch (type)
        {
            case "skillused" when string.IsNullOrWhiteSpace(trigger.SkillNameContains):
                AddBroadTriggerIssue(issues, groupName, rule.Name, "没有填写技能名，会匹配所有可见技能。");
                break;
            case "statusgained":
            case "statuslost":
                if (trigger.StatusId <= 0 && string.IsNullOrWhiteSpace(trigger.StatusNameContains))
                    AddBroadTriggerIssue(issues, groupName, rule.Name, "没有填写 Buff ID 或名称，会匹配所有 Buff。");
                break;
            case "itemacquired" when string.IsNullOrWhiteSpace(trigger.ItemNameContains):
                AddBroadTriggerIssue(issues, groupName, rule.Name, "没有填写物品名，会匹配所有获得物品事件。");
                break;
            case "mapchanged" when trigger.TerritoryType <= 0 && trigger.MapId <= 0:
                AddBroadTriggerIssue(issues, groupName, rule.Name, "没有指定地图，会在每次切换地图时触发。");
                break;
            case "jobchanged" when trigger.ClassJobId <= 0 && string.IsNullOrWhiteSpace(trigger.JobNameContains):
                AddBroadTriggerIssue(issues, groupName, rule.Name, "没有指定职业，会在每次切换职业时触发。");
                break;
            case "eventtype" when string.IsNullOrWhiteSpace(trigger.EventType):
                AddBroadTriggerIssue(issues, groupName, rule.Name, "没有填写原始事件类型，触发范围不明确。");
                break;
            case "kill" when string.IsNullOrWhiteSpace(trigger.TargetName) && string.IsNullOrWhiteSpace(trigger.ActorName):
                AddBroadTriggerIssue(issues, groupName, rule.Name, "没有填写击杀者或目标，会匹配大量击杀事件。");
                break;
            case "hpchanged":
                AddBroadTriggerIssue(issues, groupName, rule.Name, "血量变化非常频繁，建议改成低血量阈值或增加冷却。");
                break;
            case "hplow" when trigger.HpPercentBelow > 80:
                AddBroadTriggerIssue(issues, groupName, rule.Name, $"低血量阈值是 {trigger.HpPercentBelow}%，可能过于频繁。");
                break;
        }
    }

    private static void AddSoundSizeIssues(
        List<SfxPackSubmissionPreflightIssue> issues,
        IReadOnlyDictionary<string, long> exportedSoundPaths)
    {
        var soundCount = exportedSoundPaths.Count;
        var totalBytes = exportedSoundPaths.Values.Sum();
        if (soundCount > SfxPackSecurity.MaxSoundCount)
        {
            AddIssue(
                issues,
                SfxPackSubmissionPreflightSeverity.Error,
                "音效数量过多",
                $"当前会导出 {soundCount} 个音效，超过限制 {SfxPackSecurity.MaxSoundCount} 个。");
        }
        else if (soundCount > 100)
        {
            AddIssue(
                issues,
                SfxPackSubmissionPreflightSeverity.Warning,
                "音效数量偏多",
                $"当前会导出 {soundCount} 个音效，建议拆成多个小包，轻度玩家更容易选择。");
        }

        if (totalBytes > SfxPackSecurity.MaxTotalSoundBytes)
        {
            AddIssue(
                issues,
                SfxPackSubmissionPreflightSeverity.Error,
                "音频总体积过大",
                $"当前音频总大小约 {FormatPreflightBytes(totalBytes)}，超过限制 {FormatPreflightBytes(SfxPackSecurity.MaxTotalSoundBytes)}。");
        }
        else if (totalBytes > LargeTotalSoundWarningBytes)
        {
            AddIssue(
                issues,
                SfxPackSubmissionPreflightSeverity.Warning,
                "音频总体积偏大",
                $"当前音频总大小约 {FormatPreflightBytes(totalBytes)}，建议压缩或拆包。");
        }
    }

    private static void AddCoverIssues(List<SfxPackSubmissionPreflightIssue> issues, string coverPath)
    {
        var normalizedCoverPath = FilePathText.Normalize(coverPath);
        if (string.IsNullOrWhiteSpace(normalizedCoverPath))
        {
            AddIssue(
                issues,
                SfxPackSubmissionPreflightSeverity.Warning,
                "封面缺失",
                "建议给投稿包添加封面图，玩家在社区列表里会更容易理解这个包。");
            return;
        }

        if (!File.Exists(normalizedCoverPath))
        {
            AddIssue(issues, SfxPackSubmissionPreflightSeverity.Error, "封面文件不存在", "请重新选择封面图，或清空封面路径。");
            return;
        }

        var extension = Path.GetExtension(normalizedCoverPath);
        if (!AllowedCoverExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            AddIssue(issues, SfxPackSubmissionPreflightSeverity.Error, "封面格式不支持", "封面只支持 png、jpg、jpeg、webp。");
            return;
        }

        var length = new FileInfo(normalizedCoverPath).Length;
        if (length > MaxCoverBytes)
        {
            AddIssue(
                issues,
                SfxPackSubmissionPreflightSeverity.Error,
                "封面过大",
                $"封面大小为 {FormatPreflightBytes(length)}，超过限制 {FormatPreflightBytes(MaxCoverBytes)}。");
        }
    }

    private static void AddBroadTriggerIssue(
        List<SfxPackSubmissionPreflightIssue> issues,
        string groupName,
        string ruleName,
        string message)
        => AddRuleIssue(issues, SfxPackSubmissionPreflightSeverity.Warning, "触发器太宽泛", groupName, ruleName, message);

    private static void AddRuleIssue(
        List<SfxPackSubmissionPreflightIssue> issues,
        SfxPackSubmissionPreflightSeverity severity,
        string title,
        string groupName,
        string ruleName,
        string message)
        => AddIssue(issues, severity, title, $"分组「{groupName}」/ 规则「{ruleName}」：{message}");

    private static void AddIssue(
        List<SfxPackSubmissionPreflightIssue> issues,
        SfxPackSubmissionPreflightSeverity severity,
        string title,
        string message)
        => issues.Add(new SfxPackSubmissionPreflightIssue(severity, title, message));

    private static string NormalizeKey(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string FormatPreflightBytes(long value)
    {
        if (value >= 1024L * 1024L)
            return $"{value / 1024f / 1024f:0.0} MB";
        if (value >= 1024L)
            return $"{value / 1024f:0.0} KB";

        return $"{Math.Max(0, value)} B";
    }
}
