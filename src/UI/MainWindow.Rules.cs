using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using AllTimeSoundTrigger.ConfigurationModels;
using AllTimeSoundTrigger.Services;
using AllTimeSoundTrigger.Utilities;
using Dalamud.Bindings.ImGui;

namespace AllTimeSoundTrigger.UI;

public sealed partial class MainWindow
{
    private static readonly string[] TriggerTypes =
    [
        "SkillUsed",
        "ItemAcquired",
        "MapChanged",
        "CombatEntered",
        "CombatExited",
        "HpChanged",
        "HpLow",
        "LocalPlayerDefeated",
        "JobChanged",
        "StatusGained",
        "StatusLost",
        "Kill",
        "EventType"
    ];
    private static readonly string[] ConditionTypes = ["Always"];
    private static readonly string[] ActionTypes = ["Log", "Sound", "StopSound"];

    private string? selectedRuleId;
    private string? selectedGroupId;
    private readonly HashSet<string> exportGroupIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> exportRuleIds = new(StringComparer.Ordinal);
    private string rulesEditorMessage = string.Empty;
    private string exportPackagePath = string.Empty;
    private string exportReadme = string.Empty;
    private string importPackagePath = string.Empty;
    private string mapSearchText = string.Empty;
    private SfxPackPreview? importPreview;

    private void DrawRulesEditorTab()
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.30f, 0.78f, 1f, 1f), $"当前方案：{profileStorageService.ActiveProfile.Name}");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), $"配置版本：{configuration.Version}");

        if (ImGui.Button("新增规则"))
            AddRuleDefinition();

        ImGui.SameLine();
        if (ImGui.Button("保存并重载规则"))
            SaveRules("规则已保存并重载。");

        ImGui.SameLine();
        if (ImGui.Button("重载当前配置"))
        {
            reloadRules();
            rulesEditorMessage = "已从当前配置重载规则。";
        }

        if (!string.IsNullOrWhiteSpace(rulesEditorMessage))
        {
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), rulesEditorMessage);
        }

        ImGui.Separator();
        DrawGroupToolbar();
        ImGui.Separator();
        DrawRuleSelector();
        ImGui.Separator();

        var selectedRule = GetSelectedRule();
        if (selectedRule == null)
        {
            ImGui.Text("当前没有规则。");
            return;
        }

        DrawRuleEditor(selectedRule);
    }


    private void DrawGroupToolbar()
    {
        ImGui.Text("分组");
        DrawInputText("新分组名##NewGroupName", newGroupName, 120, value => newGroupName = value);
        ImGui.SameLine();
        if (ImGui.Button("新增分组"))
        {
            var group = new RuleGroupDefinition
            {
                Name = string.IsNullOrWhiteSpace(newGroupName) ? "新分组" : newGroupName,
                Rules = []
            };
            profileStorageService.ActiveProfile.Groups.Add(group);
            selectedGroupId = group.Id;
            newGroupName = string.Empty;
            SaveRules($"已新增分组：{group.Name}");
        }
    }


    private void DrawRuleSelector()
    {
        var profile = profileStorageService.ActiveProfile;
        profile.Normalize();

        if (profile.Groups.Count == 0)
        {
            ImGui.Text("当前方案里没有分组。");
            return;
        }

        foreach (var group in profile.Groups.ToArray())
        {
            ImGui.PushID(group.Id);
            selectedGroupId ??= group.Id;

            var groupState = group.Enabled ? string.Empty : "（已停用）";
            var open = ImGui.TreeNodeEx(
                $"{group.Name}{groupState} ({group.Rules.Count})##GroupTree",
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanFullWidth);

            ImGui.Indent(22f);
            DrawInputText("分组名##GroupName", group.Name, 120, value => group.Name = value);

            var groupEnabled = group.Enabled;
            if (ImGui.Checkbox("分组启用##GroupEnabled", ref groupEnabled))
            {
                group.Enabled = groupEnabled;
                SaveRules(group.Enabled ? $"已启用分组：{group.Name}" : $"已停用分组：{group.Name}");
            }

            if (ImGui.SmallButton("选中分组"))
                selectedGroupId = group.Id;

            ImGui.SameLine();
            if (ImGui.SmallButton("新增规则"))
            {
                selectedGroupId = group.Id;
                AddRuleDefinition(group);
            }

            ImGui.SameLine();
            var deleteGroup = ImGui.SmallButton("删除分组");

            var exportGroup = exportGroupIds.Contains(group.Id);
            if (ImGui.Checkbox("分享分组##ExportGroup", ref exportGroup))
            {
                if (exportGroup)
                {
                    exportGroupIds.Add(group.Id);
                    foreach (var rule in group.Rules)
                        exportRuleIds.Remove(rule.Id);
                }
                else
                {
                    exportGroupIds.Remove(group.Id);
                }
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("导出此分组 .sfxpack"))
            {
                exportGroupIds.Clear();
                exportRuleIds.Clear();
                exportGroupIds.Add(group.Id);
                exportPackagePath = sfxPackService.BuildDefaultExportPath(group.Name);
                ExportSelectedPackage();
            }

            ImGui.Unindent(22f);

            if (deleteGroup)
            {
                if (open)
                    ImGui.TreePop();
                ImGui.PopID();
                DeleteGroup(group);
                break;
            }

            if (open)
            {
                DrawRuleTable(group);
                ImGui.TreePop();
            }

            ImGui.PopID();
        }
    }

    private void DrawRuleTable(RuleGroupDefinition group)
    {
        if (group.Rules.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "这个分组里还没有规则。");
            return;
        }

        if (!ImGui.BeginTable($"##RulesEditorRuleTable{group.Id}", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("分享", ImGuiTableColumnFlags.WidthFixed, 58f);
        ImGui.TableSetupColumn("规则", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("状态", ImGuiTableColumnFlags.WidthFixed, 58f);
        ImGui.TableSetupColumn("触发器", ImGuiTableColumnFlags.WidthFixed, 90f);
        ImGui.TableSetupColumn("条件", ImGuiTableColumnFlags.WidthFixed, 58f);
        ImGui.TableSetupColumn("动作", ImGuiTableColumnFlags.WidthFixed, 58f);
        ImGui.TableHeadersRow();

        foreach (var rule in group.Rules)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (exportGroupIds.Contains(group.Id))
            {
                ImGui.Text("分组");
            }
            else
            {
                var exportRule = exportRuleIds.Contains(rule.Id);
                if (ImGui.Checkbox($"##ExportRule{rule.Id}", ref exportRule))
                {
                    if (exportRule)
                        exportRuleIds.Add(rule.Id);
                    else
                        exportRuleIds.Remove(rule.Id);
                }
            }

            ImGui.TableNextColumn();
            var selected = string.Equals(selectedRuleId, rule.Id, StringComparison.Ordinal);
            var displayName = string.IsNullOrWhiteSpace(rule.Name) ? "(未命名规则)" : rule.Name;
            if (ImGui.Selectable($"{displayName}##RuleSelect{rule.Id}", selected))
            {
                selectedRuleId = rule.Id;
                selectedGroupId = group.Id;
            }

            ImGui.TableNextColumn();
            ImGui.Text(rule.Enabled ? "启用" : "停用");
            ImGui.TableNextColumn();
            ImGui.Text(FormatTypeName(rule.Trigger?.Type ?? "-"));
            ImGui.TableNextColumn();
            ImGui.Text((rule.Conditions?.Count ?? 0).ToString());
            ImGui.TableNextColumn();
            ImGui.Text((rule.Actions?.Count ?? 0).ToString());
        }

        ImGui.EndTable();
    }

    private void DrawRuleEditor(RuleDefinition rule)
    {
        ImGui.TextColored(new Vector4(0.30f, 0.78f, 1f, 1f), "编辑规则");
        DrawInputText("名称##RuleName", rule.Name, 180, value => rule.Name = value);

        var enabled = rule.Enabled;
        if (ImGui.Checkbox("启用##RuleEnabled", ref enabled))
            rule.Enabled = enabled;

        var cooldown = (float)rule.CooldownSeconds;
        ImGui.SetNextItemWidth(140f);
        if (ImGui.InputFloat("冷却秒##RuleCooldown", ref cooldown, 0.1f, 1f, "%.2f"))
            rule.CooldownSeconds = Math.Max(0f, cooldown);

        if (ImGui.Button("删除当前规则"))
        {
            DeleteRule(rule);
            return;
        }

        ImGui.Separator();
        if (ImGui.CollapsingHeader("什么时候触发", ImGuiTreeNodeFlags.DefaultOpen))
            DrawExtendedTriggerEditor(rule);

        if (ImGui.CollapsingHeader("附加条件"))
            DrawConditionEditor(rule);

        if (ImGui.CollapsingHeader("播放什么 / 做什么", ImGuiTreeNodeFlags.DefaultOpen))
            DrawActionEditor(rule);
    }


    private void DrawConditionEditor(RuleDefinition rule)
    {
        rule.Conditions ??= [];

        ImGui.Text("条件");
        if (ImGui.Button("添加 Always 条件"))
            rule.Conditions.Add(new ConditionDefinition { Type = "Always" });

        if (rule.Conditions.Count == 0)
        {
            ImGui.Text("无条件限制。");
            return;
        }

        for (var i = 0; i < rule.Conditions.Count; i++)
        {
            var condition = rule.Conditions[i];
            ImGui.PushID($"Condition{i}");
            DrawStringCombo("类型", condition.Type, ConditionTypes, value => condition.Type = value);
            ImGui.SameLine();
            if (ImGui.SmallButton("删除"))
            {
                rule.Conditions.RemoveAt(i);
                ImGui.PopID();
                break;
            }

            ImGui.PopID();
        }
    }


    private RuleDefinition? GetSelectedRule()
    {
        var activeRules = profileStorageService.GetActiveRules();
        if (activeRules.Count == 0)
            return null;

        var selected = activeRules.FirstOrDefault(rule => string.Equals(rule.Id, selectedRuleId, StringComparison.Ordinal));
        if (selected != null)
            return selected;

        selected = activeRules[0];
        selectedRuleId = selected.Id;
        return selected;
    }

    private void AddRuleDefinition()
    {
        AddRuleDefinition(GetSelectedGroupOrDefault());
    }

    private void AddRuleDefinition(RuleGroupDefinition group)
    {
        var rule = new RuleDefinition
        {
            Name = "新规则",
            Enabled = true,
            CooldownSeconds = 0.5,
            Trigger = new TriggerDefinition
            {
                Type = "SkillUsed",
                LocalPlayerOnly = false
            },
            Conditions = [],
            Actions = CreateDefaultRuleActions()
        };

        group.Rules.Add(rule);
        selectedGroupId = group.Id;
        selectedRuleId = rule.Id;
        SaveRules("已新增规则。");
    }

    private void DeleteRule(RuleDefinition rule)
    {
        foreach (var group in profileStorageService.ActiveProfile.Groups)
            group.Rules.Remove(rule);

        exportRuleIds.Remove(rule.Id);
        var remainingRules = profileStorageService.GetActiveRules();
        selectedRuleId = remainingRules.Count > 0 ? remainingRules[0].Id : null;
        SaveRules("已删除规则。");
    }

    private void SaveRules(string message)
    {
        NormalizeRuleDefinitionsForUi();
        profileStorageService.SaveActiveProfile();
        reloadRules();
        rulesEditorMessage = message;
    }

    private void NormalizeRuleDefinitionsForUi()
    {
        profileStorageService.ActiveProfile.Normalize();
    }

    private RuleGroupDefinition GetSelectedGroupOrDefault()
    {
        var profile = profileStorageService.ActiveProfile;
        var selectedGroup = profile.Groups.FirstOrDefault(group => string.Equals(group.Id, selectedGroupId, StringComparison.Ordinal));
        if (selectedGroup != null)
            return selectedGroup;

        selectedGroup = profile.GetOrCreateDefaultGroup();
        selectedGroupId = selectedGroup.Id;
        return selectedGroup;
    }

    private void DeleteGroup(RuleGroupDefinition group)
    {
        var profile = profileStorageService.ActiveProfile;
        var deletedGroupName = group.Name;
        var deletedRuleIds = group.Rules
            .Select(rule => rule.Id)
            .ToHashSet(StringComparer.Ordinal);
        var candidateSoundIds = CollectRuleSoundIds(group.Rules);
        var candidateFilePaths = CollectRuleFilePaths(group.Rules);
        var deletedRuleCount = group.Rules.Count;

        if (profile.Groups.Count <= 1)
        {
            group.Rules.Clear();
            group.Name = "默认分组";
            group.Normalize("默认分组");
            selectedGroupId = group.Id;
        }
        else
        {
            profile.Groups.Remove(group);
            selectedGroupId = profile.Groups.FirstOrDefault()?.Id;
        }

        foreach (var ruleId in deletedRuleIds)
            exportRuleIds.Remove(ruleId);
        exportGroupIds.Remove(group.Id);

        if (selectedRuleId != null && deletedRuleIds.Contains(selectedRuleId))
            selectedRuleId = profile.EnumerateRules().FirstOrDefault()?.Id;

        var removedSounds = DeleteUnusedSounds(candidateSoundIds, candidateFilePaths);
        SaveRules(BuildDeleteGroupMessage(deletedGroupName, deletedRuleCount, removedSounds.SoundEntries, removedSounds.Files));
    }

    private (int SoundEntries, int Files) DeleteUnusedSounds(
        IReadOnlyCollection<string> candidateSoundIds,
        IReadOnlyCollection<string> candidateFilePaths)
    {
        var remainingRules = profileStorageService.ActiveProfile.EnumerateRules().ToArray();
        var remainingSoundIds = CollectRuleSoundIds(remainingRules);
        var remainingFilePaths = CollectRuleFilePaths(remainingRules);
        var removedSoundEntries = 0;
        var removedFiles = 0;

        foreach (var soundId in candidateSoundIds.Where(soundId => !remainingSoundIds.Contains(soundId)))
        {
            var entry = configuration.SoundLibrary.FindById(soundId);
            if (entry == null)
                continue;

            var path = entry.FilePath;
            configuration.SoundLibrary.Entries.Remove(entry);
            removedSoundEntries++;
            if (DeleteManagedSoundFileIfUnused(path))
                removedFiles++;
        }

        foreach (var filePath in candidateFilePaths.Where(filePath => !remainingFilePaths.Contains(filePath)))
        {
            if (DeleteManagedSoundFileIfUnused(filePath))
                removedFiles++;
        }

        if (removedSoundEntries > 0 || removedFiles > 0)
            configuration.Save();

        return (removedSoundEntries, removedFiles);
    }

    private HashSet<string> CollectRuleSoundIds(IEnumerable<RuleDefinition> rules)
    {
        var soundIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in rules.SelectMany(rule => rule.Actions ?? []))
        {
            foreach (var soundId in GetSelectedSoundIds(action))
                soundIds.Add(soundId);
        }

        return soundIds;
    }

    private static HashSet<string> CollectRuleFilePaths(IEnumerable<RuleDefinition> rules)
    {
        var filePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in rules.SelectMany(rule => rule.Actions ?? []))
        {
            var filePath = FilePathText.Normalize(action.FilePath);
            if (filePath.Length > 0)
                filePaths.Add(filePath);

            foreach (var item in action.FilePaths ?? [])
            {
                var normalized = FilePathText.Normalize(item);
                if (normalized.Length > 0)
                    filePaths.Add(normalized);
            }
        }

        return filePaths;
    }

    private static string BuildDeleteGroupMessage(string groupName, int deletedRules, int deletedSounds, int deletedFiles)
    {
        var parts = new List<string>
        {
            $"已彻底删除分组：{groupName}",
            $"规则 {deletedRules} 条"
        };

        if (deletedSounds > 0)
            parts.Add($"音效 {deletedSounds} 个");
        if (deletedFiles > 0)
            parts.Add($"本地文件 {deletedFiles} 个");

        return string.Join("，", parts) + "。";
    }

    private ActionDefinition CreateDefaultSoundAction()
    {
        var firstSound = configuration.SoundLibrary.Entries.Count > 0
            ? configuration.SoundLibrary.Entries[0]
            : null;

        return new ActionDefinition
        {
            Type = "Sound",
            SoundId = firstSound?.Id ?? string.Empty,
            Volume = 1f,
            Priority = 0,
            InterruptLowerPriority = true
        };
    }

    private List<ActionDefinition> CreateDefaultRuleActions()
    {
        if (configuration.SoundLibrary.Entries.Count > 0)
            return [CreateDefaultSoundAction()];

        return
        [
            new ActionDefinition
            {
                Type = "Log",
                Message = "规则命中。"
            }
        ];
    }

    private static void SetTriggerType(TriggerDefinition trigger, string type)
    {
        trigger.Type = type;
        ResetTriggerFields(trigger);
        if (type.Equals("SkillUsed", StringComparison.OrdinalIgnoreCase))
        {
            trigger.LocalPlayerOnly ??= false;
            return;
        }

        if (type.Equals("Kill", StringComparison.OrdinalIgnoreCase))
        {
            trigger.LocalPlayerOnly = true;
            return;
        }

        if (type.Equals("ItemAcquired", StringComparison.OrdinalIgnoreCase))
        {
            trigger.LocalPlayerOnly = true;
            return;
        }

        if (type.Equals("HpLow", StringComparison.OrdinalIgnoreCase))
        {
            trigger.HpPercentBelow = 30;
            return;
        }

        if (type.Equals("EventType", StringComparison.OrdinalIgnoreCase))
            trigger.EventType = "SkillUsed";
    }

    private static void ResetTriggerFields(TriggerDefinition trigger)
    {
        trigger.EventType = string.Empty;
        trigger.ActorName = string.Empty;
        trigger.TargetName = string.Empty;
        trigger.SkillNameContains = string.Empty;
        trigger.ItemNameContains = string.Empty;
        trigger.TerritoryType = 0;
        trigger.MapId = 0;
        trigger.ClassJobId = 0;
        trigger.JobNameContains = string.Empty;
        trigger.StatusId = 0;
        trigger.StatusNameContains = string.Empty;
        trigger.HpPercentBelow = 30;
        trigger.LocalPlayerOnly = null;
    }

    private void SetActionType(ActionDefinition action, string type)
    {
        action.Type = type;
        if (type.Equals("Sound", StringComparison.OrdinalIgnoreCase))
        {
            if (GetSelectedSoundIds(action).Count == 0 && configuration.SoundLibrary.Entries.Count > 0)
                SetSelectedSoundIds(action, [configuration.SoundLibrary.Entries[0].Id]);
            return;
        }

        action.SoundId = string.Empty;
        action.SoundIds = [];
        action.FilePath = string.Empty;
        action.FilePaths = [];
        action.Loop = false;
        action.StopOnStatusLost = false;
        if (type.Equals("StopSound", StringComparison.OrdinalIgnoreCase))
        {
            action.Message = string.Empty;
            return;
        }

        action.PlaybackKey = string.Empty;
        if (string.IsNullOrWhiteSpace(action.Message))
            action.Message = "规则命中。";
    }

    private static void DrawNonNegativeInputInt(string label, int currentValue, Action<int> setValue)
    {
        var value = Math.Max(0, currentValue);
        ImGui.SetNextItemWidth(140f);
        if (ImGui.InputInt(label, ref value))
            setValue(Math.Max(0, value));
    }

    private static void DrawPercentInputInt(string label, int currentValue, Action<int> setValue)
    {
        var value = Math.Clamp(currentValue <= 0 ? 30 : currentValue, 1, 100);
        ImGui.SetNextItemWidth(140f);
        if (ImGui.InputInt(label, ref value))
            setValue(Math.Clamp(value, 1, 100));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        var kb = bytes / 1024d;
        if (kb < 1024)
            return $"{kb:0.0} KB";

        return $"{kb / 1024d:0.0} MB";
    }

    private static string FormatTypeName(string type)
        => type switch
        {
            "SkillUsed" => "使用技能",
            "ItemAcquired" => "获得物品",
            "MapChanged" => "切换地图",
            "CombatEntered" => "进入战斗",
            "CombatExited" => "脱离战斗",
            "HpChanged" => "血量变化",
            "HpLow" => "血量过低",
            "LocalPlayerDefeated" => "本机玩家被击倒",
            "JobChanged" => "切换职业",
            "StatusGained" => "获得状态/Buff",
            "StatusLost" => "状态/Buff 消失",
            "Kill" => "击杀目标",
            "EventType" => "原始事件类型",
            "Always" => "总是",
            "Log" => "记录日志",
            "Sound" => "播放音效",
            "StopSound" => "停止指定音效",
            _ => type
        };

    private static bool DrawStringCombo(string label, string currentValue, string[] options, Action<string> setValue)
    {
        var current = string.IsNullOrWhiteSpace(currentValue) ? options[0] : currentValue.Trim();
        var changed = false;

        ImGui.SetNextItemWidth(220f);
        if (!ImGui.BeginCombo(label, FormatTypeName(current)))
            return false;

        foreach (var option in options)
        {
            var selected = option.Equals(current, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable($"{FormatTypeName(option)}##{option}", selected))
            {
                setValue(option);
                changed = true;
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
        return changed;
    }
}
