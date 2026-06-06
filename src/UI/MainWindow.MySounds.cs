using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AllTimeSoundTrigger.Audio;
using AllTimeSoundTrigger.ConfigurationModels;
using Dalamud.Bindings.ImGui;

namespace AllTimeSoundTrigger.UI;

public sealed partial class MainWindow
{
    private static readonly (string Type, string Label, string Hint)[] WizardTriggerChoices =
    [
        ("SkillUsed", "使用指定技能时", "适合技能语音、技能提示、整活音效"),
        ("Kill", "击杀敌人时", "适合击杀播报和高光语音"),
        ("ItemAcquired", "获得物品时", "适合掉落、采集、奖励提醒"),
        ("MapChanged", "切换地图时", "适合进图氛围音和地图提醒"),
        ("CombatEntered", "进入战斗时", "适合开怪提示"),
        ("CombatExited", "脱离战斗时", "适合战斗结束提示"),
        ("HpLow", "血量过低时", "适合危险提醒"),
        ("StatusGained", "获得 Buff 时", "适合 Buff 持续音或语音"),
        ("StatusLost", "Buff 消失时", "适合状态结束提醒")
    ];

    private static readonly string[] WizardStepNames = ["什么时候响", "具体是谁 / 什么", "播放什么", "保存到哪里"];

    private bool ruleWizardOpen;
    private int ruleWizardStep;
    private TriggerDefinition ruleWizardTrigger = new()
    {
        Type = "SkillUsed",
        LocalPlayerOnly = true
    };

    private string ruleWizardName = string.Empty;
    private string ruleWizardGroupId = string.Empty;
    private string ruleWizardNewGroupName = string.Empty;
    private bool ruleWizardCreateGroup;
    private bool ruleWizardStopOnStatusLost;
    private readonly List<string> ruleWizardSoundIds = [];

    private void DrawMySoundsTab()
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.30f, 0.78f, 1f, 1f), "我的音效");
        ImGui.SameLine();
        if (ImGui.Button("新建规则向导"))
            StartRuleWizard();

        ImGui.SameLine();
        if (ImGui.Button("去音效库导入音效"))
            RequestTab("library");

        if (ruleWizardOpen)
        {
            ImGui.Separator();
            DrawRuleWizard();
        }

        ImGui.Separator();
        DrawMySoundGroups();

        ImGui.Separator();
        if (ImGui.CollapsingHeader("投稿中心"))
            DrawCommunitySubmissionPanel();
    }

    private void DrawMySoundGroups()
    {
        var profile = profileStorageService.ActiveProfile;
        profile.Normalize();
        if (!profile.EnumerateRules().Any())
        {
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "现在还没有规则。");
            if (ImGui.Button("用向导创建第一条规则"))
                StartRuleWizard();
            return;
        }

        foreach (var group in profile.Groups.ToArray())
        {
            var groupState = group.Enabled ? string.Empty : "（已停用）";
            if (!ImGui.CollapsingHeader($"{group.Name}{groupState} ({group.Rules.Count})##MyGroup{group.Id}", ImGuiTreeNodeFlags.DefaultOpen))
                continue;

            var groupEnabled = group.Enabled;
            if (ImGui.SmallButton(groupEnabled ? $"停用分组##MyDisableGroup{group.Id}" : $"启用分组##MyEnableGroup{group.Id}"))
            {
                group.Enabled = !groupEnabled;
                SaveRules(group.Enabled ? $"已启用分组：{group.Name}" : $"已停用分组：{group.Name}");
            }

            ImGui.SameLine();
            if (ImGui.SmallButton($"新建规则到这个分组##MyAddRule{group.Id}"))
            {
                ruleWizardGroupId = group.Id;
                StartRuleWizard();
                ruleWizardGroupId = group.Id;
            }

            ImGui.SameLine();
            var deleteGroup = ImGui.SmallButton($"删除分组##MyDeleteGroup{group.Id}");
            if (deleteGroup)
            {
                DeleteGroup(group);
                break;
            }

            if (!group.Enabled)
                ImGui.TextColored(new Vector4(1f, 0.78f, 0.30f, 1f), "这个分组已停用，里面的规则暂时不会触发。");

            if (group.Rules.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "这个分组里还没有规则。");
                continue;
            }

            foreach (var rule in group.Rules)
                DrawRuleSummaryCard(group, rule);

            DrawGroupSoundVolumeControls(group);
        }
    }

    private void DrawRuleSummaryCard(RuleGroupDefinition group, RuleDefinition rule)
    {
        ImGui.PushID($"MyRule{rule.Id}");
        if (ImGui.BeginChild("##RuleCard", new Vector2(0, 92f), true))
        {
            var enabled = rule.Enabled;
            if (ImGui.Checkbox("##RuleEnabled", ref enabled))
            {
                rule.Enabled = enabled;
                SaveRules(enabled ? $"已启用：{rule.Name}" : $"已停用：{rule.Name}");
            }

            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.92f, 0.94f, 0.96f, 1f), rule.Name);
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), rule.Enabled ? "启用" : "停用");

            ImGui.TextWrapped($"什么时候响：{FormatTriggerSummary(rule.Trigger)}");
            ImGui.TextWrapped($"播放什么：{FormatActionSummary(rule.Actions)}");
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), $"所在分组：{group.Name}");
        }

        ImGui.EndChild();
        ImGui.PopID();
    }

    private void DrawGroupSoundVolumeControls(RuleGroupDefinition group)
    {
        var entries = GetGroupSoundLibraryEntries(group);
        if (entries.Count == 0)
            return;

        ImGui.Spacing();
        if (!ImGui.TreeNodeEx($"这个分组的音效音量 ({entries.Count})##GroupSoundVolumes{group.Id}", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        foreach (var entry in entries)
        {
            ImGui.PushID($"GroupSoundVolume{group.Id}{entry.Id}");
            ImGui.TextUnformatted(entry.Name);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(170f);
            var volume = entry.DefaultVolume;
            if (ImGui.SliderFloat("##Volume", ref volume, 0f, 1f, "%.2f"))
            {
                entry.DefaultVolume = Math.Clamp(volume, 0f, 1f);
                audioPlaybackService.RefreshActiveVolumeForFile(entry.FilePath, entry.DefaultVolume);
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                entry.Normalize();
                configuration.Save();
                reloadRules();
                windowMessage = $"已更新音效音量：{entry.Name} / {entry.DefaultVolume:0.00}";
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("测试"))
                TestGroupSound(entry);

            ImGui.PopID();
        }

        ImGui.TreePop();
    }

    private IReadOnlyList<SoundLibraryEntry> GetGroupSoundLibraryEntries(RuleGroupDefinition group)
    {
        var soundIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in group.Rules.SelectMany(rule => rule.Actions ?? []))
        {
            foreach (var soundId in GetSelectedSoundIds(action))
                soundIds.Add(soundId);
        }

        return configuration.SoundLibrary.Entries
            .Where(entry => soundIds.Contains(entry.Id))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void TestGroupSound(SoundLibraryEntry entry)
    {
        var played = audioPlaybackService.Play(new AudioPlaybackRequest
        {
            FilePath = entry.FilePath,
            Volume = entry.DefaultVolume,
            Priority = entry.Priority,
            InterruptLowerPriority = entry.InterruptLowerPriority
        });

        windowMessage = played ? $"正在测试：{entry.Name}" : $"测试失败：{entry.Name}";
    }

    private void StartRuleWizard()
    {
        ruleWizardOpen = true;
        ruleWizardStep = 0;
        ruleWizardTrigger = new TriggerDefinition
        {
            Type = "SkillUsed",
            LocalPlayerOnly = true
        };
        ruleWizardName = string.Empty;
        ruleWizardCreateGroup = false;
        ruleWizardStopOnStatusLost = false;
        ruleWizardNewGroupName = string.Empty;
        ruleWizardGroupId = profileStorageService.ActiveProfile.Groups.FirstOrDefault()?.Id ?? string.Empty;
        ruleWizardSoundIds.Clear();
    }

    private void DrawRuleWizard()
    {
        ImGui.TextColored(new Vector4(0.30f, 0.78f, 1f, 1f), $"新建规则向导：第 {ruleWizardStep + 1} / 4 步 - {WizardStepNames[Math.Clamp(ruleWizardStep, 0, WizardStepNames.Length - 1)]}");

        if (ruleWizardStep == 0)
            DrawWizardTriggerChoice();
        else if (ruleWizardStep == 1)
            DrawWizardTriggerDetails();
        else if (ruleWizardStep == 2)
            DrawWizardSoundChoice();
        else
            DrawWizardSaveStep();

        ImGui.Separator();
        if (ruleWizardStep > 0)
        {
            if (ImGui.Button("上一步"))
                ruleWizardStep--;
            ImGui.SameLine();
        }

        if (ruleWizardStep < 3)
        {
            var canAdvance = CanAdvanceRuleWizard();
            ImGui.BeginDisabled(!canAdvance);
            if (ImGui.Button("下一步"))
            {
                ruleWizardName = string.IsNullOrWhiteSpace(ruleWizardName) ? BuildWizardRuleName() : ruleWizardName;
                ruleWizardStep++;
            }
            ImGui.EndDisabled();

            if (!canAdvance)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1f, 0.55f, 0.30f, 1f), "请先选择至少一个音效。");
            }
        }
        else
        {
            ImGui.BeginDisabled(ruleWizardSoundIds.Count == 0);
            if (ImGui.Button("完成并保存"))
                FinishRuleWizard();
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        if (ImGui.Button("取消"))
            ruleWizardOpen = false;
    }

    private void DrawWizardTriggerChoice()
    {
        ImGui.Text("你想什么时候播放音效？");
        foreach (var choice in WizardTriggerChoices)
        {
            if (ImGui.Button($"{choice.Label}##WizardTrigger{choice.Type}", new Vector2(180f, 34f)))
            {
                SetTriggerType(ruleWizardTrigger, choice.Type);
                ruleWizardStopOnStatusLost = false;
                ruleWizardName = BuildWizardRuleName();
                ruleWizardStep = 1;
            }

            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), choice.Hint);
        }
    }

    private void DrawWizardTriggerDetails()
    {
        ImGui.Text("具体是谁 / 什么？");

        if (ruleWizardTrigger.Type.Equals("SkillUsed", StringComparison.OrdinalIgnoreCase))
        {
            DrawSkillNameSearch(ruleWizardTrigger);
            var localOnly = ruleWizardTrigger.LocalPlayerOnly ?? true;
            if (ImGui.Checkbox("只检测我自己的技能##WizardSkillLocalOnly", ref localOnly))
                ruleWizardTrigger.LocalPlayerOnly = localOnly;
        }
        else if (ruleWizardTrigger.Type.Equals("Kill", StringComparison.OrdinalIgnoreCase))
        {
            DrawInputText("目标名包含，可空##WizardKillTarget", ruleWizardTrigger.TargetName, 120, value => ruleWizardTrigger.TargetName = value, 260f);
            var localOnly = ruleWizardTrigger.LocalPlayerOnly ?? true;
            if (ImGui.Checkbox("只检测我自己的击杀##WizardKillLocalOnly", ref localOnly))
                ruleWizardTrigger.LocalPlayerOnly = localOnly;
        }
        else if (ruleWizardTrigger.Type.Equals("ItemAcquired", StringComparison.OrdinalIgnoreCase))
        {
            DrawInputText("物品名包含##WizardItemName", ruleWizardTrigger.ItemNameContains, 160, value => ruleWizardTrigger.ItemNameContains = value, 320f);
        }
        else if (ruleWizardTrigger.Type.Equals("MapChanged", StringComparison.OrdinalIgnoreCase))
        {
            DrawMapSelector(ruleWizardTrigger);
        }
        else if (ruleWizardTrigger.Type.Equals("HpLow", StringComparison.OrdinalIgnoreCase))
        {
            DrawPercentInputInt("低于等于 HP%##WizardHpLow", ruleWizardTrigger.HpPercentBelow, value => ruleWizardTrigger.HpPercentBelow = value);
        }
        else if (ruleWizardTrigger.Type.Equals("StatusGained", StringComparison.OrdinalIgnoreCase)
                 || ruleWizardTrigger.Type.Equals("StatusLost", StringComparison.OrdinalIgnoreCase))
        {
            DrawNonNegativeInputInt("Buff ID，0=任意##WizardStatusId", ruleWizardTrigger.StatusId, value => ruleWizardTrigger.StatusId = value);
            DrawInputText("Buff 名包含##WizardStatusName", ruleWizardTrigger.StatusNameContains, 120, value => ruleWizardTrigger.StatusNameContains = value, 260f);
            if (ruleWizardTrigger.Type.Equals("StatusGained", StringComparison.OrdinalIgnoreCase))
            {
                var stopOnLost = ruleWizardStopOnStatusLost;
                if (ImGui.Checkbox("Buff 在时循环播放，Buff 消失自动停止##WizardStopOnStatusLost", ref stopOnLost))
                    ruleWizardStopOnStatusLost = stopOnLost;
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "这个触发不需要额外设置。");
        }
    }

    private void DrawWizardSoundChoice()
    {
        ImGui.Text("播放什么？可以选多个，触发时会随机播放其中一个。");
        var entries = configuration.SoundLibrary.Entries;
        if (entries.Count == 0)
        {
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.30f, 1f), "音效库还是空的。");
            if (ImGui.Button("去音效库导入音效"))
                RequestTab("library");
            return;
        }

        ImGui.SetNextItemWidth(320f);
        if (ImGui.BeginCombo("添加音效##WizardSoundCombo", "选择音效"))
        {
            foreach (var entry in entries)
            {
                var selected = ruleWizardSoundIds.Contains(entry.Id, StringComparer.OrdinalIgnoreCase);
                if (ImGui.Selectable($"{entry.Name}##WizardSound{entry.Id}", selected) && !selected)
                    ruleWizardSoundIds.Add(entry.Id);
            }

            ImGui.EndCombo();
        }

        for (var i = 0; i < ruleWizardSoundIds.Count; i++)
        {
            var soundId = ruleWizardSoundIds[i];
            var entry = configuration.SoundLibrary.FindById(soundId);
            ImGui.TextUnformatted(entry?.Name ?? soundId);
            ImGui.SameLine();
            if (ImGui.SmallButton($"移除##WizardRemoveSound{i}"))
            {
                ruleWizardSoundIds.RemoveAt(i);
                break;
            }
        }
    }

    private void DrawWizardSaveStep()
    {
        if (string.IsNullOrWhiteSpace(ruleWizardName))
            ruleWizardName = BuildWizardRuleName();

        DrawInputText("规则名##WizardRuleName", ruleWizardName, 160, value => ruleWizardName = value, 360f);

        var createGroup = ruleWizardCreateGroup;
        if (ImGui.Checkbox("保存时新建分组##WizardCreateGroup", ref createGroup))
            ruleWizardCreateGroup = createGroup;

        if (ruleWizardCreateGroup)
            DrawInputText("新分组名##WizardNewGroup", ruleWizardNewGroupName, 120, value => ruleWizardNewGroupName = value, 260f);
        else
            DrawWizardGroupCombo();

        ImGui.TextWrapped($"完成后：{BuildWizardRuleName()}，播放 {ruleWizardSoundIds.Count} 个音效。");
    }

    private bool CanAdvanceRuleWizard()
        => ruleWizardStep != 2 || ruleWizardSoundIds.Count > 0;

    private void DrawWizardGroupCombo()
    {
        var groups = profileStorageService.ActiveProfile.Groups;
        var current = groups.FirstOrDefault(group => group.Id.Equals(ruleWizardGroupId, StringComparison.Ordinal))
            ?? groups.FirstOrDefault();
        if (current == null)
            return;

        ruleWizardGroupId = current.Id;
        ImGui.SetNextItemWidth(260f);
        if (!ImGui.BeginCombo("保存到分组##WizardGroup", current.Name))
            return;

        foreach (var group in groups)
        {
            var selected = group.Id.Equals(ruleWizardGroupId, StringComparison.Ordinal);
            if (ImGui.Selectable($"{group.Name}##WizardGroup{group.Id}", selected))
                ruleWizardGroupId = group.Id;

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private void FinishRuleWizard()
    {
        var group = GetRuleWizardTargetGroup();
        var rule = new RuleDefinition
        {
            Name = string.IsNullOrWhiteSpace(ruleWizardName) ? BuildWizardRuleName() : ruleWizardName,
            Enabled = true,
            CooldownSeconds = 0.5,
            Trigger = CloneTrigger(ruleWizardTrigger),
            Conditions = [],
            Actions =
            [
                new ActionDefinition
                {
                    Type = "Sound",
                    SoundId = ruleWizardSoundIds[0],
                    SoundIds = ruleWizardSoundIds.Count > 1 ? ruleWizardSoundIds.ToList() : [],
                    Volume = 1f,
                    Loop = ruleWizardStopOnStatusLost && ruleWizardTrigger.Type.Equals("StatusGained", StringComparison.OrdinalIgnoreCase),
                    StopOnStatusLost = ruleWizardStopOnStatusLost && ruleWizardTrigger.Type.Equals("StatusGained", StringComparison.OrdinalIgnoreCase),
                    InterruptLowerPriority = true
                }
            ]
        };

        group.Rules.Add(rule);
        selectedGroupId = group.Id;
        selectedRuleId = rule.Id;
        ruleWizardOpen = false;
        SaveRules($"已创建规则：{rule.Name}");
    }

    private RuleGroupDefinition GetRuleWizardTargetGroup()
    {
        if (ruleWizardCreateGroup)
        {
            var group = new RuleGroupDefinition
            {
                Name = string.IsNullOrWhiteSpace(ruleWizardNewGroupName) ? "新分组" : ruleWizardNewGroupName,
                Rules = []
            };
            profileStorageService.ActiveProfile.Groups.Add(group);
            return group;
        }

        return profileStorageService.ActiveProfile.Groups.FirstOrDefault(group => group.Id.Equals(ruleWizardGroupId, StringComparison.Ordinal))
            ?? profileStorageService.ActiveProfile.GetOrCreateDefaultGroup();
    }

    private string BuildWizardRuleName()
        => $"播放音效：{FormatTriggerSummary(ruleWizardTrigger)}";

    private static TriggerDefinition CloneTrigger(TriggerDefinition source)
        => new()
        {
            Type = source.Type,
            EventType = source.EventType,
            ActorName = source.ActorName,
            TargetName = source.TargetName,
            SkillNameContains = source.SkillNameContains,
            ItemNameContains = source.ItemNameContains,
            TerritoryType = source.TerritoryType,
            MapId = source.MapId,
            ClassJobId = source.ClassJobId,
            JobNameContains = source.JobNameContains,
            StatusId = source.StatusId,
            StatusNameContains = source.StatusNameContains,
            HpPercentBelow = source.HpPercentBelow,
            LocalPlayerOnly = source.LocalPlayerOnly
        };
}
