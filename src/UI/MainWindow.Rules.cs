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

    private void DrawProfilesTab()
    {
        ImGui.Spacing();
        DrawProfileToolbar();
        ImGui.Separator();
        DrawAutoSwitchEditor();
    }

    private void DrawProfileToolbar()
    {
        var activeProfile = profileStorageService.ActiveProfile;
        ImGui.Text("方案");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(240f);
        if (ImGui.BeginCombo("当前方案##ActiveProfile", activeProfile.Name))
        {
            foreach (var profile in profileStorageService.Profiles)
            {
                var selected = profile.Id.Equals(activeProfile.Id, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable($"{profile.Name}##Profile{profile.Id}", selected))
                {
                    if (profileStorageService.SwitchProfile(profile.Id, configuration))
                    {
                        selectedRuleId = null;
                        selectedGroupId = null;
                        exportGroupIds.Clear();
                        exportRuleIds.Clear();
                        exportPackagePath = string.Empty;
                        importPreview = null;
                        reloadRules();
                        rulesEditorMessage = $"已切换方案：{profile.Name}";
                    }
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("新建空方案##CreateProfileQuick"))
            CreateAndSwitchProfile(string.Empty);

        DrawInputText("新方案名##NewProfileName", newProfileName, 120, value => newProfileName = value);
        ImGui.SameLine();
        if (ImGui.Button("新增方案"))
            CreateAndSwitchProfile(newProfileName);

        ImGui.SameLine();
        if (ImGui.Button("删除当前方案"))
        {
            var deletedName = activeProfile.Name;
            if (profileStorageService.DeleteProfile(activeProfile.Id, configuration))
            {
                selectedRuleId = null;
                selectedGroupId = null;
                exportGroupIds.Clear();
                exportRuleIds.Clear();
                exportPackagePath = string.Empty;
                importPreview = null;
                reloadRules();
                rulesEditorMessage = $"已删除方案：{deletedName}";
            }
            else
            {
                rulesEditorMessage = "至少需要保留一个方案。";
            }
        }

        ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), $"方案目录：{profileStorageService.RootDirectory}");
    }

    private void DrawActiveProfileQuickSwitch()
    {
        var activeProfile = profileStorageService.ActiveProfile;
        ImGui.SetNextItemWidth(220f);
        if (!ImGui.BeginCombo("当前方案##TopActiveProfile", activeProfile.Name))
            return;

        foreach (var profile in profileStorageService.Profiles)
        {
            var selected = profile.Id.Equals(activeProfile.Id, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable($"{profile.Name}##TopProfile{profile.Id}", selected))
            {
                if (profileStorageService.SwitchProfile(profile.Id, configuration))
                {
                    selectedRuleId = null;
                    selectedGroupId = null;
                    exportGroupIds.Clear();
                    exportRuleIds.Clear();
                    exportPackagePath = string.Empty;
                    importPreview = null;
                    reloadRules();
                    rulesEditorMessage = $"已切换方案：{profile.Name}";
                }
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private void CreateAndSwitchProfile(string requestedName)
    {
        var profile = profileStorageService.CreateProfile(requestedName);
        profileStorageService.SwitchProfile(profile.Id, configuration);
        selectedRuleId = null;
        selectedGroupId = null;
        exportGroupIds.Clear();
        exportRuleIds.Clear();
        exportPackagePath = string.Empty;
        importPreview = null;
        newProfileName = string.Empty;
        reloadRules();
        rulesEditorMessage = $"已新增方案：{profile.Name}";
    }

    private void DrawAutoSwitchEditor()
    {
        var autoSwitch = configuration.AutoSwitch;
        var enabled = autoSwitch.Enabled;
        if (ImGui.Checkbox("启用地图自动切换方案##AutoSwitchEnabled", ref enabled))
        {
            autoSwitch.Enabled = enabled;
            configuration.Save();
        }

        if (!autoSwitch.Enabled)
            return;

        DrawProfileIdCombo("进入匹配地图切到##AutoSwitchTarget", autoSwitch.TargetProfileId, value =>
        {
            autoSwitch.TargetProfileId = value;
            configuration.Save();
        });
        DrawProfileIdCombo("离开后切回##AutoSwitchFallback", autoSwitch.FallbackProfileId, value =>
        {
            autoSwitch.FallbackProfileId = value;
            configuration.Save();
        });

        DrawInputText("自动切换地图搜索##AutoSwitchMapSearch", mapSearchText, 120, value => mapSearchText = value);
        ImGui.SetNextItemWidth(320f);
        if (ImGui.BeginCombo("添加匹配地图##AutoSwitchAddMap", "选择地图"))
        {
            foreach (var option in gameDataLookupService.SearchMaps(mapSearchText))
            {
                if (ImGui.Selectable($"{option.Name} ({option.TerritoryType})##AutoSwitchMap{option.TerritoryType}", false)
                    && !autoSwitch.TerritoryTypes.Contains((int)option.TerritoryType))
                {
                    autoSwitch.TerritoryTypes.Add((int)option.TerritoryType);
                    configuration.Save();
                }
            }

            ImGui.EndCombo();
        }

        for (var i = 0; i < autoSwitch.TerritoryTypes.Count; i++)
        {
            var territoryType = autoSwitch.TerritoryTypes[i];
            var option = gameDataLookupService.FindMap((uint)territoryType, 0);
            ImGui.Text(option == null ? $"地图 {territoryType}" : $"{option.Name} ({territoryType})");
            ImGui.SameLine();
            if (ImGui.SmallButton($"移除##AutoSwitchRemove{territoryType}"))
            {
                autoSwitch.TerritoryTypes.RemoveAt(i);
                configuration.Save();
                break;
            }
        }
    }

    private void DrawProfileIdCombo(string label, string currentProfileId, Action<string> setValue)
    {
        var current = profileStorageService.Profiles.FirstOrDefault(profile => profile.Id.Equals(currentProfileId, StringComparison.OrdinalIgnoreCase));
        ImGui.SetNextItemWidth(220f);
        if (!ImGui.BeginCombo(label, current?.Name ?? "未选择"))
            return;

        foreach (var profile in profileStorageService.Profiles)
        {
            var selected = profile.Id.Equals(currentProfileId, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable($"{profile.Name}##AutoSwitchProfile{label}{profile.Id}", selected))
                setValue(profile.Id);

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
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

    private void DrawPackageTab()
    {
        ImGui.Spacing();
        DrawPackageToolbar();
    }

    private void DrawPackageToolbar()
    {
        if (string.IsNullOrWhiteSpace(exportPackagePath))
            exportPackagePath = sfxPackService.BuildDefaultExportPath(profileStorageService.ActiveProfile.Name);

        ImGui.Text("导入/导出分享包");
        ImGui.TextColored(new Vector4(0.30f, 0.78f, 1f, 1f), $"当前方案：{profileStorageService.ActiveProfile.Name}");
        ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), BuildExportSelectionSummary());

        if (ImGui.SmallButton("选择整个方案"))
            SelectEntireCurrentProfileForExport();
        ImGui.SameLine();
        if (ImGui.SmallButton("清空分享选择"))
        {
            exportGroupIds.Clear();
            exportRuleIds.Clear();
        }

        DrawExportSelectionTree();

        ImGui.Separator();
        DrawInputText("导出路径##ExportSfxPackPath", exportPackagePath, 520, value => exportPackagePath = value);
        ImGui.SameLine();
        if (ImGui.Button("选择导出位置"))
            OpenExportPackageDialog();

        DrawInputText("README##ExportSfxPackReadme", exportReadme, 500, value => exportReadme = value);
        if (ImGui.Button("导出所选内容"))
            ExportSelectedPackage();
        ImGui.SameLine();
        if (ImGui.Button("导出整个方案"))
        {
            SelectEntireCurrentProfileForExport();
            exportPackagePath = sfxPackService.BuildDefaultExportPath(profileStorageService.ActiveProfile.Name);
            ExportSelectedPackage();
        }

        ImGui.Separator();
        DrawInputText("导入包路径##ImportSfxPackPath", importPackagePath, 520, value => importPackagePath = value);
        ImGui.SameLine();
        if (ImGui.Button("选择导入包"))
            OpenImportPackageDialog();

        if (ImGui.Button("预览导入包"))
            PreviewImportPackage();

        if (importPreview == null)
            return;

        ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f),
            $"包名：{importPreview.ProfileName} / 分组 {importPreview.GroupCount} / 规则 {importPreview.RuleCount} / 音效 {importPreview.SoundCount} / {FormatBytes(importPreview.TotalSoundBytes)}");
        if (!string.IsNullOrWhiteSpace(importPreview.Readme))
            ImGui.TextWrapped(importPreview.Readme);

        if (ImGui.Button("导入到当前方案"))
            ImportPackageIntoCurrentProfile();

        ImGui.SameLine();
        if (ImGui.Button("导入为新方案"))
            ImportPackageAsNewProfile();
    }

    private void DrawExportSelectionTree()
    {
        if (ImGui.BeginChild("##ExportSelectionTree", new Vector2(0, 220f), true))
        {
            foreach (var group in profileStorageService.ActiveProfile.Groups)
            {
                ImGui.PushID($"ExportGroup{group.Id}");
                var groupSelected = exportGroupIds.Contains(group.Id);
                if (ImGui.Checkbox($"整个分组：{group.Name} ({group.Rules.Count})##Group", ref groupSelected))
                {
                    if (groupSelected)
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

                var open = ImGui.TreeNodeEx("单条规则##Rules", ImGuiTreeNodeFlags.DefaultOpen);
                if (open)
                {
                    ImGui.Indent(18f);
                    foreach (var rule in group.Rules)
                    {
                        var ruleSelected = exportRuleIds.Contains(rule.Id);
                        if (groupSelected)
                        {
                            var disabledSelected = true;
                            ImGui.BeginDisabled();
                            ImGui.Checkbox($"{rule.Name}##Rule{rule.Id}", ref disabledSelected);
                            ImGui.EndDisabled();
                        }
                        else if (ImGui.Checkbox($"{rule.Name}##Rule{rule.Id}", ref ruleSelected))
                        {
                            if (ruleSelected)
                                exportRuleIds.Add(rule.Id);
                            else
                                exportRuleIds.Remove(rule.Id);
                        }
                    }

                    ImGui.Unindent(18f);
                    ImGui.TreePop();
                }

                ImGui.PopID();
            }
        }

        ImGui.EndChild();
    }

    private void SelectEntireCurrentProfileForExport()
    {
        exportGroupIds.Clear();
        exportRuleIds.Clear();
        foreach (var group in profileStorageService.ActiveProfile.Groups)
            exportGroupIds.Add(group.Id);
    }

    private string BuildExportSelectionSummary()
    {
        var selectedGroups = exportGroupIds.Count;
        var selectedRules = profileStorageService.ActiveProfile.Groups
            .SelectMany(group => exportGroupIds.Contains(group.Id)
                ? group.Rules
                : group.Rules.Where(rule => exportRuleIds.Contains(rule.Id)))
            .Count();

        return selectedRules == 0
            ? "尚未选择分享内容。"
            : $"已选择：分组 {selectedGroups} / 规则 {selectedRules}";
    }

    private void OpenExportPackageDialog()
    {
        try
        {
            using var dialog = new SaveFileDialog
            {
                Title = "导出音效分享包",
                Filter = "音效分享包 (*.sfxpack)|*.sfxpack|所有文件 (*.*)|*.*",
                DefaultExt = "sfxpack",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = sfxPackService.ExportDirectory,
                FileName = $"{profileStorageService.ActiveProfile.Name}.sfxpack"
            };

            if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.FileName))
                exportPackagePath = dialog.FileName;
        }
        catch (Exception ex)
        {
            rulesEditorMessage = $"打开导出窗口失败：{ex.Message}";
        }
    }

    private void OpenImportPackageDialog()
    {
        try
        {
            using var dialog = new OpenFileDialog
            {
                Title = "导入音效分享包",
                Filter = "音效分享包 (*.sfxpack)|*.sfxpack|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                InitialDirectory = sfxPackService.ExportDirectory
            };

            if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.FileName))
            {
                importPackagePath = dialog.FileName;
                PreviewImportPackage();
            }
        }
        catch (Exception ex)
        {
            rulesEditorMessage = $"打开导入窗口失败：{ex.Message}";
        }
    }

    private void ExportSelectedPackage()
    {
        try
        {
            var result = sfxPackService.Export(
                profileStorageService.ActiveProfile,
                exportGroupIds,
                exportRuleIds,
                configuration.SoundLibrary,
                exportPackagePath,
                exportReadme);

            rulesEditorMessage = result.Success
                ? $"{result.Message} 分组 {result.GroupCount} / 规则 {result.RuleCount} / 音效 {result.SoundCount}：{result.PackagePath}"
                : result.Message;
        }
        catch (Exception ex)
        {
            rulesEditorMessage = $"导出失败：{ex.Message}";
        }
    }

    private void PreviewImportPackage()
    {
        try
        {
            importPreview = sfxPackService.Preview(importPackagePath);
            rulesEditorMessage = "导入包预览已加载。";
        }
        catch (Exception ex)
        {
            importPreview = null;
            rulesEditorMessage = $"预览失败：{ex.Message}";
        }
    }

    private void ImportPackageIntoCurrentProfile()
    {
        try
        {
            var importedProfile = sfxPackService.Import(importPackagePath, configuration.SoundLibrary);
            configuration.Save();
            profileStorageService.ActiveProfile.Groups.AddRange(importedProfile.Groups);
            selectedRuleId = importedProfile.EnumerateRules().FirstOrDefault()?.Id;
            selectedGroupId = importedProfile.Groups.FirstOrDefault()?.Id;
            importPreview = null;
            SaveRules($"已导入到当前方案：{importedProfile.Name}");
        }
        catch (Exception ex)
        {
            rulesEditorMessage = $"导入失败：{ex.Message}";
        }
    }

    private void ImportPackageAsNewProfile()
    {
        try
        {
            var importedProfile = sfxPackService.Import(importPackagePath, configuration.SoundLibrary);
            configuration.Save();
            var newProfile = profileStorageService.CreateProfile(importedProfile.Name);
            newProfile.Groups = importedProfile.Groups;
            profileStorageService.SwitchProfile(newProfile.Id, configuration);
            profileStorageService.SaveActiveProfile();
            selectedRuleId = newProfile.EnumerateRules().FirstOrDefault()?.Id;
            selectedGroupId = newProfile.Groups.FirstOrDefault()?.Id;
            exportGroupIds.Clear();
            exportRuleIds.Clear();
            exportPackagePath = string.Empty;
            importPreview = null;
            reloadRules();
            rulesEditorMessage = $"已导入为新方案：{newProfile.Name}";
        }
        catch (Exception ex)
        {
            rulesEditorMessage = $"导入失败：{ex.Message}";
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

    private void DrawTriggerEditor(RuleDefinition rule)
    {
        rule.Trigger ??= new TriggerDefinition();
        var trigger = rule.Trigger;

        ImGui.Text("触发器");
        if (DrawStringCombo("类型##TriggerType", trigger.Type, TriggerTypes, value => SetTriggerType(trigger, value)))
            rulesEditorMessage = "触发器类型已更新，保存后生效。";

        if (trigger.Type.Equals("SkillUsed", StringComparison.OrdinalIgnoreCase))
        {
            DrawInputText("角色名包含##TriggerActor", trigger.ActorName, 120, value => trigger.ActorName = value);
            DrawSkillNameSearch(trigger);

            var localOnly = trigger.LocalPlayerOnly ?? false;
            if (ImGui.Checkbox("仅本机角色##TriggerLocalOnly", ref localOnly))
                trigger.LocalPlayerOnly = localOnly;
        }
        else if (trigger.Type.Equals("Kill", StringComparison.OrdinalIgnoreCase))
        {
            DrawInputText("击杀者包含##TriggerKillActor", trigger.ActorName, 120, value => trigger.ActorName = value);
            DrawInputText("目标名包含##TriggerKillTarget", trigger.TargetName, 120, value => trigger.TargetName = value);

            var localOnly = trigger.LocalPlayerOnly ?? true;
            if (ImGui.Checkbox("仅本机击杀##TriggerLocalKillOnly", ref localOnly))
                trigger.LocalPlayerOnly = localOnly;
        }
        else
        {
            DrawInputText("事件类型##TriggerEventType", trigger.EventType, 120, value => trigger.EventType = value);
        }
    }

    private void DrawSkillNameSearch(TriggerDefinition trigger)
    {
        DrawInputText("技能名搜索##TriggerSkill", trigger.SkillNameContains, 120, value => trigger.SkillNameContains = value);

        var options = gameDataLookupService.SearchActions(trigger.SkillNameContains);
        if (options.Count == 0)
            return;

        ImGui.SetNextItemWidth(320f);
        if (!ImGui.BeginCombo("选择技能全称##TriggerSkillPick", "选择匹配技能"))
            return;

        foreach (var option in options)
        {
            if (ImGui.Selectable($"{option.Name}##Action{option.Id}", false))
                trigger.SkillNameContains = option.Name;
        }

        ImGui.EndCombo();
    }

    private void DrawMapSelector(TriggerDefinition trigger)
    {
        DrawInputText("地图名搜索##TriggerMapSearch", mapSearchText, 120, value => mapSearchText = value);

        var currentMap = gameDataLookupService.FindMap((uint)Math.Max(0, trigger.TerritoryType), (uint)Math.Max(0, trigger.MapId));
        var preview = currentMap == null
            ? "任意地图"
            : $"{currentMap.Name} ({currentMap.TerritoryType})";

        ImGui.SetNextItemWidth(320f);
        if (!ImGui.BeginCombo("指定地图##TriggerMapPick", preview))
            return;

        if (ImGui.Selectable("任意地图##AnyMap", trigger.TerritoryType == 0 && trigger.MapId == 0))
        {
            trigger.TerritoryType = 0;
            trigger.MapId = 0;
        }

        foreach (var option in gameDataLookupService.SearchMaps(mapSearchText))
        {
            var selected = trigger.TerritoryType == option.TerritoryType && trigger.MapId == option.MapId;
            if (ImGui.Selectable($"{option.Name} ({option.TerritoryType})##Map{option.TerritoryType}-{option.MapId}", selected))
            {
                trigger.TerritoryType = (int)option.TerritoryType;
                trigger.MapId = (int)option.MapId;
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private void DrawExtendedTriggerEditor(RuleDefinition rule)
    {
        rule.Trigger ??= new TriggerDefinition();
        var trigger = rule.Trigger;

        ImGui.Text("触发器");
        if (DrawStringCombo("类型##TriggerType", trigger.Type, TriggerTypes, value => SetTriggerType(trigger, value)))
            rulesEditorMessage = "触发器类型已更新，保存后生效。";

        if (trigger.Type.Equals("SkillUsed", StringComparison.OrdinalIgnoreCase))
        {
            DrawInputText("角色名包含##TriggerActor", trigger.ActorName, 120, value => trigger.ActorName = value);
            DrawSkillNameSearch(trigger);

            var localOnly = trigger.LocalPlayerOnly ?? false;
            if (ImGui.Checkbox("仅本机角色##TriggerLocalOnly", ref localOnly))
                trigger.LocalPlayerOnly = localOnly;
        }
        else if (trigger.Type.Equals("ItemAcquired", StringComparison.OrdinalIgnoreCase))
        {
            DrawInputText("物品名包含##TriggerItemName", trigger.ItemNameContains, 160, value => trigger.ItemNameContains = value);
            DrawInputText("获得者包含##TriggerItemActor", trigger.ActorName, 120, value => trigger.ActorName = value);

            var localOnly = trigger.LocalPlayerOnly ?? true;
            if (ImGui.Checkbox("仅本机玩家获得##TriggerItemLocalOnly", ref localOnly))
                trigger.LocalPlayerOnly = localOnly;
        }
        else if (trigger.Type.Equals("MapChanged", StringComparison.OrdinalIgnoreCase))
        {
            DrawMapSelector(trigger);
        }
        else if (trigger.Type.Equals("CombatEntered", StringComparison.OrdinalIgnoreCase)
                 || trigger.Type.Equals("CombatExited", StringComparison.OrdinalIgnoreCase))
        {
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "此触发器无需额外参数。");
        }
        else if (trigger.Type.Equals("HpChanged", StringComparison.OrdinalIgnoreCase))
        {
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "HP 数值或最大 HP 变化时触发。");
        }
        else if (trigger.Type.Equals("HpLow", StringComparison.OrdinalIgnoreCase))
        {
            DrawPercentInputInt("低于等于 HP%##TriggerHpLowPercent", trigger.HpPercentBelow, value => trigger.HpPercentBelow = value);
        }
        else if (trigger.Type.Equals("LocalPlayerDefeated", StringComparison.OrdinalIgnoreCase))
        {
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "本机玩家 HP 从大于 0 变为 0 时触发。");
        }
        else if (trigger.Type.Equals("JobChanged", StringComparison.OrdinalIgnoreCase))
        {
            DrawNonNegativeInputInt("指定职业 ID，0=任意##TriggerClassJobId", trigger.ClassJobId, value => trigger.ClassJobId = value);
            DrawInputText("职业名包含##TriggerJobName", trigger.JobNameContains, 120, value => trigger.JobNameContains = value);
        }
        else if (trigger.Type.Equals("StatusGained", StringComparison.OrdinalIgnoreCase)
                 || trigger.Type.Equals("StatusLost", StringComparison.OrdinalIgnoreCase))
        {
            DrawNonNegativeInputInt("状态/Buff ID，0=任意##TriggerStatusId", trigger.StatusId, value => trigger.StatusId = value);
            DrawInputText("状态/Buff 名包含##TriggerStatusName", trigger.StatusNameContains, 120, value => trigger.StatusNameContains = value);
        }
        else if (trigger.Type.Equals("Kill", StringComparison.OrdinalIgnoreCase))
        {
            DrawInputText("击杀者包含##TriggerKillActor", trigger.ActorName, 120, value => trigger.ActorName = value);
            DrawInputText("目标名包含##TriggerKillTarget", trigger.TargetName, 120, value => trigger.TargetName = value);

            var localOnly = trigger.LocalPlayerOnly ?? true;
            if (ImGui.Checkbox("仅本机击杀##TriggerLocalKillOnly", ref localOnly))
                trigger.LocalPlayerOnly = localOnly;
        }
        else
        {
            DrawInputText("事件类型##TriggerEventType", trigger.EventType, 120, value => trigger.EventType = value);
        }
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

    private void DrawActionEditor(RuleDefinition rule)
    {
        rule.Actions ??= [];

        ImGui.Text("动作");
        if (ImGui.Button("添加 Log 动作"))
            rule.Actions.Add(new ActionDefinition { Type = "Log", Message = "规则命中。" });

        ImGui.SameLine();
        if (ImGui.Button("添加 Sound 动作"))
            rule.Actions.Add(CreateDefaultSoundAction());

        ImGui.SameLine();
        if (ImGui.Button("添加停止音效动作"))
            rule.Actions.Add(new ActionDefinition { Type = "StopSound" });

        if (rule.Actions.Count == 0)
        {
            ImGui.Text("当前规则没有动作。");
            return;
        }

        for (var i = 0; i < rule.Actions.Count; i++)
        {
            var action = rule.Actions[i];
            ImGui.PushID($"Action{i}");
            ImGui.Text($"动作 #{i + 1}");
            if (DrawStringCombo("类型", action.Type, ActionTypes, value => SetActionType(action, value)))
                rulesEditorMessage = "动作类型已更新，保存后生效。";

            if (action.Type.Equals("Sound", StringComparison.OrdinalIgnoreCase))
                DrawSoundActionEditor(action, i);
            else if (action.Type.Equals("StopSound", StringComparison.OrdinalIgnoreCase))
                DrawStopSoundActionEditor(action);
            else
                DrawInputText("日志消息##ActionMessage", action.Message, 220, value => action.Message = value);

            if (ImGui.SmallButton("删除动作"))
            {
                rule.Actions.RemoveAt(i);
                ImGui.PopID();
                break;
            }

            ImGui.Separator();
            ImGui.PopID();
        }
    }

    private void DrawSoundActionEditor(ActionDefinition action, int index)
    {
        var entries = configuration.SoundLibrary.Entries;
        if (entries.Count > 0)
        {
            var selectedSoundIds = GetSelectedSoundIds(action);
            var preview = FormatSelectedSoundPreview(selectedSoundIds);

            ImGui.SetNextItemWidth(320f);
            if (ImGui.BeginCombo($"添加音效##ActionSoundId{index}", preview))
            {
                var directSelected = selectedSoundIds.Count == 0;
                if (ImGui.Selectable("直接路径##DirectSoundPath", directSelected))
                    SetSelectedSoundIds(action, []);
                if (directSelected)
                    ImGui.SetItemDefaultFocus();

                foreach (var entry in entries)
                {
                    var selected = selectedSoundIds.Contains(entry.Id, StringComparer.OrdinalIgnoreCase);
                    if (ImGui.Selectable($"{entry.Name} ({entry.Id})##Sound{entry.Id}", selected))
                    {
                        if (!selected)
                        {
                            selectedSoundIds.Add(entry.Id);
                            SetSelectedSoundIds(action, selectedSoundIds);
                        }

                        action.FilePath = string.Empty;
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
        }

        DrawSoundPlaybackOptions(action);

        var selectedIds = GetSelectedSoundIds(action);
        if (selectedIds.Count > 0)
        {
            var missingIds = selectedIds
                .Where(soundId => configuration.SoundLibrary.FindById(soundId) == null)
                .ToList();
            if (missingIds.Count > 0)
            {
                ImGui.TextColored(new Vector4(1f, 0.55f, 0.30f, 1f), $"找不到 SoundId：{string.Join(", ", missingIds)}");
                DrawInputText("SoundId##MissingSoundId", action.SoundId, 120, value =>
                {
                    action.SoundId = value;
                    action.SoundIds = [];
                });
                if (ImGui.SmallButton("改用直接路径"))
                    SetSelectedSoundIds(action, []);
                if (GetSelectedSoundIds(action).Count > 0)
                    return;
            }
            else
            {
                DrawSelectedSoundList(action, selectedIds);
                return;
            }
        }

        DrawInputText("文件路径##ActionFilePath", action.FilePath, 520, value => action.FilePath = value);

        var volume = action.Volume;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("音量##ActionVolume", ref volume, 0f, 1f))
            action.Volume = Math.Clamp(volume, 0f, 1f);

        var priority = action.Priority;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("优先级##ActionPriority", ref priority))
            action.Priority = Math.Clamp(priority, -100, 100);

        var interrupt = action.InterruptLowerPriority;
        if (ImGui.Checkbox("可打断低优先级音效##ActionInterrupt", ref interrupt))
            action.InterruptLowerPriority = interrupt;
    }

    private List<string> GetSelectedSoundIds(ActionDefinition action)
    {
        var ids = new List<string>();
        if (action.SoundIds != null)
        {
            ids.AddRange(action.SoundIds
                .Select(item => (item ?? string.Empty).Trim())
                .Where(item => item.Length > 0));
        }

        var legacySoundId = (action.SoundId ?? string.Empty).Trim();
        if (legacySoundId.Length > 0)
            ids.Insert(0, legacySoundId);

        return ids
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void SetSelectedSoundIds(ActionDefinition action, IReadOnlyList<string> soundIds)
    {
        var normalized = soundIds
            .Select(item => (item ?? string.Empty).Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        action.SoundId = normalized.Count > 0 ? normalized[0] : string.Empty;
        action.SoundIds = normalized.Count > 1 ? normalized : [];
        if (normalized.Count > 0)
        {
            action.FilePath = string.Empty;
            action.FilePaths = [];
        }
    }

    private string FormatSelectedSoundPreview(IReadOnlyList<string> soundIds)
    {
        if (soundIds.Count == 0)
            return "直接路径";

        if (soundIds.Count > 1)
            return $"随机播放 {soundIds.Count} 个音效";

        var entry = configuration.SoundLibrary.FindById(soundIds[0]);
        return entry == null
            ? $"缺失：{soundIds[0]}"
            : $"{entry.Name} ({entry.Id})";
    }

    private void DrawSelectedSoundList(ActionDefinition action, List<string> selectedIds)
    {
        ImGui.TextColored(
            new Vector4(0.70f, 0.72f, 0.76f, 1f),
            selectedIds.Count == 1 ? "触发时播放这个音效：" : "触发时从以下音效中随机播放一条：");

        for (var i = 0; i < selectedIds.Count; i++)
        {
            var soundId = selectedIds[i];
            var entry = configuration.SoundLibrary.FindById(soundId);
            ImGui.PushID($"SelectedSound{soundId}");
            ImGui.TextUnformatted(entry == null ? soundId : $"{entry.Name} ({entry.Id})");
            ImGui.SameLine();
            if (ImGui.SmallButton("移除"))
            {
                selectedIds.RemoveAt(i);
                SetSelectedSoundIds(action, selectedIds);
                ImGui.PopID();
                break;
            }

            ImGui.PopID();
        }
    }

    private static void DrawSoundPlaybackOptions(ActionDefinition action)
    {
        var loop = action.Loop;
        if (ImGui.Checkbox("循环播放##ActionLoop", ref loop))
            action.Loop = loop;

        var stopOnStatusLost = action.StopOnStatusLost;
        if (ImGui.Checkbox("Buff 消失时自动停止##ActionStopOnStatusLost", ref stopOnStatusLost))
        {
            action.StopOnStatusLost = stopOnStatusLost;
            if (stopOnStatusLost)
                action.Loop = true;
        }

        DrawInputText("播放标识##ActionPlaybackKey", action.PlaybackKey, 160, value => action.PlaybackKey = value);
        ImGui.TextColored(
            new Vector4(0.70f, 0.72f, 0.76f, 1f),
            action.StopOnStatusLost
                ? "用于“获得状态/Buff”触发器；可不填播放标识，Buff 消失会自动停止。"
                : "手动停止循环音效时，填写播放标识，并用“停止指定音效”动作填同一个标识。");
    }

    private static void DrawStopSoundActionEditor(ActionDefinition action)
    {
        DrawInputText("停止播放标识##StopPlaybackKey", action.PlaybackKey, 160, value => action.PlaybackKey = value);
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
