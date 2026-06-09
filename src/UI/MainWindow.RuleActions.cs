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
}
