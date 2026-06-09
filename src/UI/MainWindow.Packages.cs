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
}
