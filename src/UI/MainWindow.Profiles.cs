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
}
