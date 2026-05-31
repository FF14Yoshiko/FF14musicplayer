using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Windows.Forms;
using AllTimeSoundTrigger.Audio;
using AllTimeSoundTrigger.ConfigurationModels;
using AllTimeSoundTrigger.Services;
using AllTimeSoundTrigger.Utilities;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AllTimeSoundTrigger.UI;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly EventLogService eventLogService;
    private readonly AudioPlaybackService audioPlaybackService;
    private readonly ProfileStorageService profileStorageService;
    private readonly SfxPackService sfxPackService;
    private readonly GameDataLookupService gameDataLookupService;
    private readonly Configuration configuration;
    private readonly Action reloadRules;
    private static readonly string TutorialPath = Path.Combine(AppContext.BaseDirectory, "docs", "tutorial.html");
    private string windowMessage = string.Empty;
    private string newProfileName = string.Empty;
    private string newGroupName = string.Empty;
    private string newSoundId = string.Empty;
    private string newSoundName = string.Empty;
    private string newSoundPath = string.Empty;
    private float newSoundVolume = 1f;
    private int newSoundPriority;
    private string soundLibraryMessage = string.Empty;

    public MainWindow(
        EventLogService eventLogService,
        AudioPlaybackService audioPlaybackService,
        ProfileStorageService profileStorageService,
        SfxPackService sfxPackService,
        GameDataLookupService gameDataLookupService,
        Configuration configuration,
        Action reloadRules)
        : base($"{Plugin.DisplayName}##AllTimeSoundTriggerMainWindow")
    {
        this.eventLogService = eventLogService;
        this.audioPlaybackService = audioPlaybackService;
        this.profileStorageService = profileStorageService;
        this.sfxPackService = sfxPackService;
        this.gameDataLookupService = gameDataLookupService;
        this.configuration = configuration;
        this.reloadRules = reloadRules;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(720, 460),
            MaximumSize = new Vector2(1600, 1200)
        };
    }

    public override void Draw()
    {
        ImGui.TextColored(new Vector4(0.30f, 0.78f, 1f, 1f), Plugin.DisplayName);
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "本地音效规则引擎");
        ImGui.SameLine();
        DrawActiveProfileQuickSwitch();
        ImGui.SameLine();
        if (ImGui.Button("查看插件教程"))
            OpenTutorial();
        if (!string.IsNullOrWhiteSpace(windowMessage))
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), windowMessage);
        ImGui.Separator();

        if (!ImGui.BeginTabBar("##AllTimeSoundTriggerTabs"))
            return;

        if (ImGui.BeginTabItem("规则"))
        {
            DrawRulesTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("音效库"))
        {
            DrawSoundLibraryTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("方案"))
        {
            DrawProfilesTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("分享包"))
        {
            DrawPackageTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("日志"))
        {
            DrawEventLogTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    public void Dispose()
    {
    }

    private void OpenTutorial()
    {
        try
        {
            if (!File.Exists(TutorialPath))
            {
                windowMessage = $"未找到教程文件：{TutorialPath}";
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = TutorialPath,
                UseShellExecute = true
            });
            windowMessage = "已打开插件教程。";
        }
        catch (Exception ex)
        {
            windowMessage = $"打开教程失败：{ex.Message}";
        }
    }

    private void DrawRulesTab()
    {
        DrawRulesEditorTab();
    }

    private void DrawSoundLibraryTab()
    {
        ImGui.Spacing();
        ImGui.Text($"正在播放：{audioPlaybackService.ActiveCount}");
        ImGui.SameLine();
        if (ImGui.Button("停止全部播放"))
        {
            audioPlaybackService.StopAll();
            soundLibraryMessage = "已停止全部音效。";
        }

        var masterVolume = configuration.Audio.MasterVolume;
        if (ImGui.SliderFloat("主音量", ref masterVolume, 0f, 1f))
        {
            configuration.Audio.MasterVolume = masterVolume;
            audioPlaybackService.RefreshActiveVolumes();
            configuration.Save();
        }

        var maxConcurrent = configuration.Audio.MaxConcurrentSounds;
        if (ImGui.InputInt("最大并发", ref maxConcurrent))
        {
            configuration.Audio.MaxConcurrentSounds = Math.Clamp(maxConcurrent, 1, 16);
            configuration.Save();
        }

        ImGui.Separator();
        if (ImGui.Button("导入音效文件..."))
            OpenSoundImportDialog();

        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), $"自动复制到：{sfxPackService.ManagedSoundDirectory}");

        ImGui.Separator();
        if (ImGui.CollapsingHeader("高级：手动添加路径"))
        {
            DrawInputText("SoundId##NewSoundId", newSoundId, 120, value => newSoundId = value);
            DrawInputText("名称##NewSoundName", newSoundName, 120, value => newSoundName = value);
            DrawInputText("文件路径##NewSoundPath", newSoundPath, 520, value => newSoundPath = value);

            ImGui.SetNextItemWidth(220f);
            ImGui.SliderFloat("音量##NewSoundVolume", ref newSoundVolume, 0f, 1f);
            newSoundVolume = Math.Clamp(newSoundVolume, 0f, 1f);

            ImGui.SetNextItemWidth(120f);
            if (ImGui.InputInt("优先级##NewSoundPriority", ref newSoundPriority))
                newSoundPriority = Math.Clamp(newSoundPriority, -100, 100);

            if (ImGui.Button("添加音效"))
                AddSoundLibraryEntry();
        }

        if (!string.IsNullOrWhiteSpace(soundLibraryMessage))
        {
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), soundLibraryMessage);
        }

        ImGui.Separator();
        DrawSoundLibraryEntries();
    }

    private void DrawEventLogTab()
    {
        ImGui.Spacing();
        if (ImGui.Button("清空日志"))
            eventLogService.Clear();

        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "在游戏聊天日志出现“你使用了 XX 技能”时，这里会新增一行。");
        ImGui.Separator();

        var entries = eventLogService.Snapshot();
        if (entries.Length == 0)
        {
            ImGui.Text("暂无事件。");
            return;
        }

        if (!ImGui.BeginTable("##EventLogTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("时间", ImGuiTableColumnFlags.WidthFixed, 88f);
        ImGui.TableSetupColumn("级别", ImGuiTableColumnFlags.WidthFixed, 72f);
        ImGui.TableSetupColumn("类型", ImGuiTableColumnFlags.WidthFixed, 110f);
        ImGui.TableSetupColumn("消息", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("原始文本", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var entry in entries)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(entry.Timestamp.ToString("HH:mm:ss"));
            ImGui.TableNextColumn();
            ImGui.Text(entry.Level);
            ImGui.TableNextColumn();
            ImGui.Text(entry.EventType);
            ImGui.TableNextColumn();
            ImGui.TextWrapped(entry.Message);
            ImGui.TableNextColumn();
            ImGui.TextWrapped(entry.RawText);
        }

        ImGui.EndTable();
    }

    private void OpenSoundImportDialog()
    {
        try
        {
            using var dialog = new OpenFileDialog
            {
                Title = "导入音效文件",
                Filter = "音效文件 (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg|所有文件 (*.*)|*.*",
                Multiselect = true,
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != DialogResult.OK || dialog.FileNames.Length == 0)
            {
                soundLibraryMessage = "已取消导入。";
                return;
            }

            ImportSoundFiles(dialog.FileNames);
        }
        catch (Exception ex)
        {
            soundLibraryMessage = $"导入失败：{ex.Message}";
        }
    }

    private void ImportSoundFiles(IReadOnlyList<string> filePaths)
    {
        var imported = 0;
        foreach (var sourcePath in filePaths)
        {
            var copiedPath = sfxPackService.CopySoundIntoManagedDirectory(sourcePath);
            var name = Path.GetFileNameWithoutExtension(copiedPath);
            var soundId = BuildUniqueSoundId(BuildSoundId(string.Empty, name, copiedPath));
            var entry = new SoundLibraryEntry
            {
                Id = soundId,
                Name = name,
                FilePath = copiedPath,
                DefaultVolume = 1f,
                Priority = 0,
                InterruptLowerPriority = true
            };
            entry.Normalize();
            configuration.SoundLibrary.Entries.Add(entry);
            imported++;
        }

        configuration.Save();
        reloadRules();
        soundLibraryMessage = $"已导入 {imported} 个音效。";
    }

    private void AddSoundLibraryEntry()
    {
        var filePath = FilePathText.Normalize(newSoundPath);
        if (filePath.Length == 0)
        {
            soundLibraryMessage = "请先填写文件路径。";
            return;
        }

        var soundId = BuildSoundId(newSoundId, newSoundName, filePath);
        if (configuration.SoundLibrary.Entries.Exists(entry => string.Equals(entry.Id, soundId, StringComparison.OrdinalIgnoreCase)))
        {
            soundLibraryMessage = "这个 SoundId 已经在音效库里。";
            return;
        }

        var name = newSoundName.Trim();
        if (name.Length == 0)
            name = Path.GetFileNameWithoutExtension(filePath);
        if (name.Length == 0)
            name = "未命名音效";

        var entry = new SoundLibraryEntry
        {
            Id = soundId,
            Name = name,
            FilePath = filePath,
            DefaultVolume = newSoundVolume,
            Priority = Math.Clamp(newSoundPriority, -100, 100),
            InterruptLowerPriority = true
        };
        entry.Normalize();

        configuration.SoundLibrary.Entries.Add(entry);
        configuration.Save();
        reloadRules();
        soundLibraryMessage = File.Exists(filePath)
            ? $"已添加：{entry.Name} / {entry.Id}"
            : $"已保存路径，文件当前不存在：{entry.Name} / {entry.Id}";
        newSoundId = string.Empty;
        newSoundName = string.Empty;
        newSoundPath = string.Empty;
        newSoundVolume = 1f;
        newSoundPriority = 0;
    }

    private void DrawSoundLibraryEntries()
    {
        var entries = configuration.SoundLibrary.Entries;
        if (entries.Count == 0)
        {
            ImGui.Text("音效库为空。");
            return;
        }

        if (!ImGui.BeginTable("##SoundLibraryTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("名称 / SoundId", ImGuiTableColumnFlags.WidthFixed, 190f);
        ImGui.TableSetupColumn("状态", ImGuiTableColumnFlags.WidthFixed, 70f);
        ImGui.TableSetupColumn("音量", ImGuiTableColumnFlags.WidthFixed, 80f);
        ImGui.TableSetupColumn("优先级", ImGuiTableColumnFlags.WidthFixed, 72f);
        ImGui.TableSetupColumn("路径", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 190f);
        ImGui.TableHeadersRow();

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            ImGui.PushID(entry.Id);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Name);
            ImGui.TextDisabled(entry.Id);

            ImGui.TableNextColumn();
            ImGui.TextColored(File.Exists(entry.FilePath)
                ? new Vector4(0.48f, 0.90f, 0.62f, 1f)
                : new Vector4(1f, 0.55f, 0.30f, 1f), File.Exists(entry.FilePath) ? "可用" : "缺失");

            ImGui.TableNextColumn();
            ImGui.Text(entry.DefaultVolume.ToString("0.00"));

            ImGui.TableNextColumn();
            ImGui.Text(entry.Priority.ToString());

            ImGui.TableNextColumn();
            ImGui.TextWrapped(entry.FilePath);

            ImGui.TableNextColumn();
            if (ImGui.SmallButton("测试"))
                TestSoundLibraryEntry(entry);

            ImGui.SameLine();
            if (ImGui.SmallButton("复制ID"))
                ImGui.SetClipboardText(entry.Id);

            if (ImGui.SmallButton("绑定技能"))
                AttachSoundToSkillRule(entry);

            ImGui.SameLine();
            if (ImGui.SmallButton("删除"))
            {
                var removedName = entry.Name;
                entries.RemoveAt(i);
                configuration.Save();
                reloadRules();
                soundLibraryMessage = $"已删除：{removedName}";
                ImGui.PopID();
                break;
            }

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void AttachSoundToSkillRule(SoundLibraryEntry entry)
    {
        var rule = profileStorageService.GetActiveRules().FirstOrDefault(item =>
            item.Trigger != null && item.Trigger.Type.Equals("SkillUsed", StringComparison.OrdinalIgnoreCase));

        if (rule == null)
        {
            rule = new ConfigurationModels.RuleDefinition
            {
                Name = $"技能触发：{entry.Name}",
                Enabled = true,
                CooldownSeconds = 0.5,
                Trigger = new ConfigurationModels.TriggerDefinition
                {
                    Type = "SkillUsed",
                    LocalPlayerOnly = false
                },
                Conditions = [],
                Actions = []
            };
            profileStorageService.GetOrCreateDefaultGroup().Rules.Add(rule);
        }

        rule.Actions ??= [];
        if (rule.Actions.Exists(action =>
                string.Equals(action.Type, "Sound", StringComparison.OrdinalIgnoreCase)
                && string.Equals(action.SoundId, entry.Id, StringComparison.OrdinalIgnoreCase)))
        {
            soundLibraryMessage = $"技能规则已绑定：{entry.Id}";
            return;
        }

        rule.Actions.Add(new ConfigurationModels.ActionDefinition
        {
            Type = "Sound",
            SoundId = entry.Id,
            Volume = 1f,
            Priority = entry.Priority,
            InterruptLowerPriority = entry.InterruptLowerPriority
        });

        SaveRules($"已绑定到技能规则：{entry.Name}");
        soundLibraryMessage = $"已绑定到技能规则：{entry.Name}";
    }

    private void TestSoundLibraryEntry(SoundLibraryEntry entry)
    {
        var played = audioPlaybackService.Play(new AudioPlaybackRequest
        {
            FilePath = entry.FilePath,
            Volume = entry.DefaultVolume,
            Priority = entry.Priority,
            InterruptLowerPriority = entry.InterruptLowerPriority
        });

        soundLibraryMessage = played ? $"正在测试：{entry.Name}" : $"测试失败：{entry.Name}";
    }

    private static string BuildSoundId(string requestedId, string requestedName, string filePath)
    {
        var soundId = requestedId.Trim();
        if (soundId.Length > 0)
            return soundId;

        soundId = requestedName.Trim();
        if (soundId.Length > 0)
            return soundId;

        soundId = Path.GetFileNameWithoutExtension(filePath.Trim());
        return soundId.Length > 0 ? soundId : Guid.NewGuid().ToString("N");
    }

    private string BuildUniqueSoundId(string requestedId)
    {
        var baseId = string.IsNullOrWhiteSpace(requestedId) ? Guid.NewGuid().ToString("N") : requestedId.Trim();
        var candidate = baseId;
        var index = 2;
        while (configuration.SoundLibrary.Entries.Exists(entry => string.Equals(entry.Id, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseId}_{index}";
            index++;
        }

        return candidate;
    }

    private static bool DrawInputText(string label, string currentValue, uint maxLength, Action<string> setValue)
    {
        var value = currentValue ?? string.Empty;
        var encoded = Encoding.UTF8.GetBytes(value);
        var buffer = new byte[Math.Max((int)maxLength + 1, encoded.Length + 8)];
        Array.Copy(encoded, buffer, Math.Min(encoded.Length, buffer.Length - 1));

        ImGui.SetNextItemWidth(520f);
        if (!ImGui.InputText(label, buffer))
            return false;

        var zeroIndex = Array.IndexOf(buffer, (byte)0);
        if (zeroIndex < 0)
            zeroIndex = buffer.Length;

        setValue(Encoding.UTF8.GetString(buffer, 0, zeroIndex).Trim());
        return true;
    }
}
