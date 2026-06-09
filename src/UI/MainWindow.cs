using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using AllTimeSoundTrigger.Audio;
using AllTimeSoundTrigger.Community;
using AllTimeSoundTrigger.ConfigurationModels;
using AllTimeSoundTrigger.Rules;
using AllTimeSoundTrigger.Services;
using AllTimeSoundTrigger.Utilities;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;

namespace AllTimeSoundTrigger.UI;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly EventLogService eventLogService;
    private readonly AudioPlaybackService audioPlaybackService;
    private readonly ProfileStorageService profileStorageService;
    private readonly SfxPackService sfxPackService;
    private readonly CommunityPackService communityPackService;
    private readonly GameDataLookupService gameDataLookupService;
    private readonly ITextureProvider textureProvider;
    private readonly Configuration configuration;
    private readonly Action reloadRules;
    private readonly Func<RulesEngineRuntimeSnapshot> getRulesEngineRuntimeSnapshot;
    private readonly string pluginDirectory;
    private string windowMessage = string.Empty;
    private string newProfileName = string.Empty;
    private string newGroupName = string.Empty;
    private string newSoundId = string.Empty;
    private string newSoundName = string.Empty;
    private string newSoundPath = string.Empty;
    private float newSoundVolume = 1f;
    private int newSoundPriority;
    private string soundLibraryMessage = string.Empty;
    private string requestedTab = string.Empty;

    public MainWindow(
        EventLogService eventLogService,
        AudioPlaybackService audioPlaybackService,
        ProfileStorageService profileStorageService,
        SfxPackService sfxPackService,
        CommunityPackService communityPackService,
        GameDataLookupService gameDataLookupService,
        ITextureProvider textureProvider,
        Configuration configuration,
        Action reloadRules,
        Func<RulesEngineRuntimeSnapshot> getRulesEngineRuntimeSnapshot,
        string pluginDirectory)
        : base($"{Plugin.DisplayName}##AllTimeSoundTriggerMainWindow")
    {
        this.eventLogService = eventLogService;
        this.audioPlaybackService = audioPlaybackService;
        this.profileStorageService = profileStorageService;
        this.sfxPackService = sfxPackService;
        this.communityPackService = communityPackService;
        this.gameDataLookupService = gameDataLookupService;
        this.textureProvider = textureProvider;
        this.configuration = configuration;
        this.reloadRules = reloadRules;
        this.getRulesEngineRuntimeSnapshot = getRulesEngineRuntimeSnapshot;
        this.pluginDirectory = pluginDirectory;
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
        DrawExperienceModeSelector();
        ImGui.SameLine();
        if (ImGui.Button("查看插件教程"))
            OpenTutorial();
        DrawAdvancedModeToggle();
        if (!string.IsNullOrWhiteSpace(windowMessage))
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), windowMessage);
        ImGui.Separator();

        if (!ImGui.BeginTabBar("##AllTimeSoundTriggerTabs"))
            return;

        if (!IsTabVisible(requestedTab))
            requestedTab = "home";

        if (BeginRequestedTab("首页", "home"))
        {
            DrawHomeTab();
            ImGui.EndTabItem();
        }

        if (IsTabVisible("community") && BeginRequestedTab(GetCommunityTabLabel(), "community"))
        {
            DrawCommunityTab();
            ImGui.EndTabItem();
        }

        if (IsTabVisible("mine") && BeginRequestedTab(GetMySoundsTabLabel(), "mine"))
        {
            DrawMySoundsTab();
            ImGui.EndTabItem();
        }

        if (IsTabVisible("library") && BeginRequestedTab("音效库", "library"))
        {
            DrawSoundLibraryTab();
            ImGui.EndTabItem();
        }

        if (IsTabVisible("log") && BeginRequestedTab("日志", "log"))
        {
            DrawEventLogTab();
            ImGui.EndTabItem();
        }

        if (IsTabVisible("advanced") && BeginRequestedTab("高级", "advanced"))
        {
            DrawAdvancedTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    public void Dispose()
    {
        CancelCommunityOperations("已取消社区后台任务。");
        lock (communityCancellationGate)
        {
            communityCancellation.Dispose();
        }
    }

    public override void OnClose()
    {
        CancelCommunityOperations("已取消社区后台任务。");
    }

    private void OpenTutorial()
    {
        try
        {
            var tutorialPath = ResolveTutorialPath();
            if (!File.Exists(tutorialPath))
            {
                windowMessage = $"未找到教程文件：{tutorialPath}";
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = tutorialPath,
                UseShellExecute = true
            });
            windowMessage = "已打开插件教程。";
        }
        catch (Exception ex)
        {
            windowMessage = $"打开教程失败：{ex.Message}";
        }
    }

    private string ResolveTutorialPath()
    {
        var candidates = new List<string>();
        AddCandidate(candidates, pluginDirectory);

        var assemblyPath = typeof(MainWindow).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            var assemblyDirectory = Path.GetDirectoryName(assemblyPath);
            if (!string.IsNullOrWhiteSpace(assemblyDirectory))
                AddCandidate(candidates, assemblyDirectory);
        }

        if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
            AddCandidate(candidates, AppContext.BaseDirectory);

        AddCandidate(candidates, Directory.GetCurrentDirectory());

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return candidates.Count > 0 ? candidates[0] : Path.GetFullPath(Path.Combine("docs", "tutorial.html"));
    }

    private static void AddCandidate(List<string> candidates, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        var candidate = Path.GetFullPath(Path.Combine(directory, "docs", "tutorial.html"));
        if (!candidates.Exists(item => item.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            candidates.Add(candidate);
    }

    private void DrawRulesTab()
    {
        DrawRulesEditorTab();
    }

    private bool BeginRequestedTab(string label, string tabKey)
    {
        var flags = requestedTab.Equals(tabKey, StringComparison.OrdinalIgnoreCase)
            ? ImGuiTabItemFlags.SetSelected
            : ImGuiTabItemFlags.None;
        var open = ImGui.BeginTabItem(label, flags);
        if (open && requestedTab.Equals(tabKey, StringComparison.OrdinalIgnoreCase))
            requestedTab = string.Empty;
        return open;
    }

    private void RequestTab(string tabKey)
    {
        requestedTab = IsTabVisible(tabKey) ? tabKey : "home";
    }

    private void DrawExperienceModeSelector()
    {
        ImGui.SetNextItemWidth(132f);
        if (!ImGui.BeginCombo("模式##ExperienceMode", GetExperienceModeLabel(configuration.ExperienceMode)))
            return;

        DrawExperienceModeOption(ExperienceMode.Beginner);
        DrawExperienceModeOption(ExperienceMode.Player);
        DrawExperienceModeOption(ExperienceMode.Creator);
        DrawExperienceModeOption(ExperienceMode.Advanced);
        if (configuration.CommunityDeveloperMode)
            DrawExperienceModeOption(ExperienceMode.Reviewer);

        ImGui.EndCombo();
    }

    private void DrawExperienceModeOption(ExperienceMode mode)
    {
        var selected = configuration.ExperienceMode == mode;
        if (ImGui.Selectable($"{GetExperienceModeLabel(mode)}##ExperienceMode{mode}", selected))
            SetExperienceMode(mode);

        if (selected)
            ImGui.SetItemDefaultFocus();
    }

    private void SetExperienceMode(ExperienceMode mode)
    {
        configuration.ExperienceMode = mode;
        configuration.ExperienceModeChosen = true;
        if (mode == ExperienceMode.Reviewer)
            configuration.CommunityDeveloperMode = true;
        configuration.Save();
        if (!IsTabVisible(requestedTab))
            requestedTab = "home";
        windowMessage = $"已切换到{GetExperienceModeLabel(mode)}。";
    }

    private bool IsTabVisible(string tabKey)
    {
        if (string.IsNullOrWhiteSpace(tabKey) || tabKey.Equals("home", StringComparison.OrdinalIgnoreCase))
            return true;

        var mode = configuration.ExperienceMode;
        if (tabKey.Equals("community", StringComparison.OrdinalIgnoreCase))
            return true;
        if (tabKey.Equals("mine", StringComparison.OrdinalIgnoreCase))
            return true;
        if (tabKey.Equals("library", StringComparison.OrdinalIgnoreCase))
            return CanCreateSounds();
        if (tabKey.Equals("log", StringComparison.OrdinalIgnoreCase))
            return mode != ExperienceMode.Beginner;
        if (tabKey.Equals("advanced", StringComparison.OrdinalIgnoreCase))
            return CanUseAdvancedTools();

        return false;
    }

    private bool CanCreateSounds()
        => configuration.ExperienceMode is ExperienceMode.Creator or ExperienceMode.Advanced or ExperienceMode.Reviewer;

    private bool CanEditRules()
        => configuration.ExperienceMode is ExperienceMode.Creator or ExperienceMode.Advanced or ExperienceMode.Reviewer;

    private bool CanUseAdvancedTools()
        => configuration.ExperienceMode is ExperienceMode.Advanced or ExperienceMode.Reviewer;

    private bool CanReviewCommunity()
        => configuration.CommunityDeveloperMode
            && configuration.ExperienceMode == ExperienceMode.Reviewer;

    private string GetCommunityTabLabel()
        => configuration.ExperienceMode is ExperienceMode.Beginner or ExperienceMode.Player
            ? "逛音效包"
            : "社区音效包";

    private string GetMySoundsTabLabel()
        => configuration.ExperienceMode switch
        {
            ExperienceMode.Beginner or ExperienceMode.Player => "我的音效包",
            ExperienceMode.Creator => "做音效",
            _ => "我的音效"
        };

    private static string GetExperienceModeLabel(ExperienceMode mode)
        => mode switch
        {
            ExperienceMode.Beginner => "小白模式",
            ExperienceMode.Player => "玩音效包",
            ExperienceMode.Creator => "创作者",
            ExperienceMode.Advanced => "高级模式",
            ExperienceMode.Reviewer => "审核模式",
            _ => "小白模式"
        };

    private static string GetExperienceModeHint(ExperienceMode mode)
        => mode switch
        {
            ExperienceMode.Beginner => "只保留安装、开关、试听和帮助。",
            ExperienceMode.Player => "适合逛社区包、管理已安装音效包。",
            ExperienceMode.Creator => "适合导入音频、用向导做音效、生成投稿包。",
            ExperienceMode.Advanced => "显示完整规则编辑、导入导出和排错工具。",
            ExperienceMode.Reviewer => "显示社区审核和 Gitee 发布工具。",
            _ => string.Empty
        };

    private void DrawAdvancedModeToggle()
    {
        var io = ImGui.GetIO();
        if (!io.KeyCtrl || !io.KeyShift)
            return;

        ImGui.SameLine();
        if (ImGui.SmallButton(configuration.CommunityDeveloperMode ? "关闭审核模式" : "审核模式"))
        {
            configuration.CommunityDeveloperMode = !configuration.CommunityDeveloperMode;
            configuration.ExperienceMode = configuration.CommunityDeveloperMode
                ? ExperienceMode.Reviewer
                : ExperienceMode.Advanced;
            configuration.ExperienceModeChosen = true;
            configuration.Save();
            if (!IsTabVisible(requestedTab))
                requestedTab = "home";
        }
    }

    private void DrawAdvancedTab()
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.72f, 0.35f, 1f), "高级模式：这里保留完整规则编辑、分享包导入导出和审核发布。");

        DrawRuntimeObservationPanel();

        if (ImGui.CollapsingHeader("完整规则编辑", ImGuiTreeNodeFlags.DefaultOpen))
            DrawRulesEditorTab();

        if (ImGui.CollapsingHeader("分享包导入导出"))
            DrawPackageTab();

        if (CanReviewCommunity())
            DrawCommunityDeveloperPanel();
    }

    private void DrawRuntimeObservationPanel()
    {
        if (!ImGui.CollapsingHeader("运行时观测", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var snapshot = getRulesEngineRuntimeSnapshot();
        ImGui.TextColored(
            new Vector4(0.70f, 0.72f, 0.76f, 1f),
            "用于排查卡顿、没触发、冷却跳过等问题。这里显示最近一次游戏事件的规则匹配情况。");

        if (!ImGui.BeginTable("##RuntimeObservationTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("指标", ImGuiTableColumnFlags.WidthFixed, 180f);
        ImGui.TableSetupColumn("当前值", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        DrawRuntimeObservationRow("当前规则数", snapshot.RuleCount.ToString());
        DrawRuntimeObservationRow("事件索引桶数量", snapshot.EventIndexBucketCount.ToString());
        DrawRuntimeObservationRow("全局规则数", snapshot.GlobalRuleCount.ToString());
        DrawRuntimeObservationRow("最近事件类型", string.IsNullOrWhiteSpace(snapshot.LastEventType) ? "暂无事件" : snapshot.LastEventType);
        DrawRuntimeObservationRow("最近事件时间", snapshot.LastEventAt?.ToString("HH:mm:ss") ?? "暂无事件");
        DrawRuntimeObservationRow("最近事件耗时", $"{snapshot.LastEventElapsedMilliseconds:0.###} ms");
        DrawRuntimeObservationRow("候选规则数", snapshot.LastCandidateRuleCount.ToString());
        DrawRuntimeObservationRow("触发器命中数", snapshot.LastMatchedRuleCount.ToString());
        DrawRuntimeObservationRow("触发规则数", snapshot.LastTriggeredRuleCount.ToString());
        DrawRuntimeObservationRow("被冷却跳过数", snapshot.LastCooldownSkippedCount.ToString());

        ImGui.EndTable();
    }

    private static void DrawRuntimeObservationRow(string label, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), label);
        ImGui.TableNextColumn();
        ImGui.Text(value);
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

        if (configuration.CommunityDeveloperMode)
        {
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
        ImGui.TableSetupColumn("音量", ImGuiTableColumnFlags.WidthFixed, 128f);
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
            if (DrawInputText($"名称##SoundName{i}", entry.Name, 120, value =>
                {
                    entry.Name = value;
                    entry.Normalize();
                    configuration.Save();
                    soundLibraryMessage = $"已重命名音效：{entry.Name}";
                }, 180f))
            {
                // SoundId stays stable, so existing rules keep pointing at the same sound.
            }
            ImGui.TextDisabled(entry.Id);

            ImGui.TableNextColumn();
            ImGui.TextColored(File.Exists(entry.FilePath)
                ? new Vector4(0.48f, 0.90f, 0.62f, 1f)
                : new Vector4(1f, 0.55f, 0.30f, 1f), File.Exists(entry.FilePath) ? "可用" : "缺失");

            ImGui.TableNextColumn();
            DrawSoundLibraryVolumeEditor(entry, i);

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
                var removedPath = entry.FilePath;
                entries.RemoveAt(i);
                var deletedFile = DeleteManagedSoundFileIfUnused(removedPath);
                configuration.Save();
                reloadRules();
                soundLibraryMessage = deletedFile
                    ? $"已删除：{removedName}，并已删除导入文件。"
                    : $"已删除：{removedName}";
                ImGui.PopID();
                break;
            }

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawSoundLibraryVolumeEditor(SoundLibraryEntry entry, int index)
    {
        var volume = entry.DefaultVolume;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat($"##SoundVolume{index}", ref volume, 0f, 1f, "%.2f"))
        {
            entry.DefaultVolume = Math.Clamp(volume, 0f, 1f);
            audioPlaybackService.RefreshActiveVolumeForFile(entry.FilePath, entry.DefaultVolume);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            entry.Normalize();
            configuration.Save();
            reloadRules();
            soundLibraryMessage = $"已更新音量：{entry.Name} / {entry.DefaultVolume:0.00}";
        }
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

    private bool DeleteManagedSoundFileIfUnused(string filePath)
    {
        var normalizedPath = FilePathText.Normalize(filePath);
        if (normalizedPath.Length == 0)
            return false;

        if (configuration.SoundLibrary.Entries.Exists(entry =>
                FilePathText.Normalize(entry.FilePath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var managedDirectory = Path.GetFullPath(sfxPackService.ManagedSoundDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(normalizedPath);
        if (!fullPath.StartsWith(managedDirectory, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return true;
            }
        }
        catch (Exception ex)
        {
            soundLibraryMessage = $"已删除音效条目，但删除文件失败：{ex.Message}";
        }

        return false;
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

    private static bool DrawInputText(string label, string currentValue, uint maxLength, Action<string> setValue, float width = 520f)
    {
        var value = currentValue ?? string.Empty;
        var encoded = Encoding.UTF8.GetBytes(value);
        var buffer = new byte[Math.Max((int)maxLength + 1, encoded.Length + 8)];
        Array.Copy(encoded, buffer, Math.Min(encoded.Length, buffer.Length - 1));

        ImGui.SetNextItemWidth(width);
        if (!ImGui.InputText(label, buffer))
            return false;

        var zeroIndex = Array.IndexOf(buffer, (byte)0);
        if (zeroIndex < 0)
            zeroIndex = buffer.Length;

        setValue(Encoding.UTF8.GetString(buffer, 0, zeroIndex).Trim());
        return true;
    }
}
