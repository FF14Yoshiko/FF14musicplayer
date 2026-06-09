using System.Linq;
using System.Numerics;
using AllTimeSoundTrigger.ConfigurationModels;
using Dalamud.Bindings.ImGui;

namespace AllTimeSoundTrigger.UI;

public sealed partial class MainWindow
{
    private void DrawHomeTab()
    {
        var profile = profileStorageService.ActiveProfile;
        var enabledGroupCount = profile.Groups.Count(group => group.Enabled);
        var totalRuleCount = profileStorageService.GetActiveRules().Count;
        var runtimeRuleCount = profileStorageService.GetRuntimeRules().Count;

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.30f, 0.78f, 1f, 1f), "今天想做什么？");
        ImGui.TextColored(
            new Vector4(0.70f, 0.72f, 0.76f, 1f),
            $"当前内容：启用分组 {enabledGroupCount}/{profile.Groups.Count} / 可触发规则 {runtimeRuleCount}/{totalRuleCount} / 音效 {configuration.SoundLibrary.Entries.Count}");

        ImGui.Spacing();
        if (!configuration.ExperienceModeChosen)
        {
            DrawFirstRunModePicker();
            ImGui.Separator();
        }

        DrawHomeModeActions();

        ImGui.Separator();
        ImGui.Text("快速检查");
        ImGui.BulletText(configuration.SoundLibrary.Entries.Count == 0
            ? CanCreateSounds()
                ? "音效库还是空的，先去「音效库」导入 mp3/wav/ogg。"
                : "现在还没有本地音效，可以先去「逛音效包」安装一个社区包。"
            : $"音效库里已有 {configuration.SoundLibrary.Entries.Count} 个音效。");
        ImGui.BulletText(totalRuleCount == 0
            ? CanEditRules()
                ? "现在还没有规则，可以点上面的“新建规则向导”。"
                : "现在还没有已安装音效包，可以先从社区安装。"
            : $"现在已有 {totalRuleCount} 条规则，其中 {runtimeRuleCount} 条所在分组已启用。");
        ImGui.BulletText(IsTabVisible("log")
            ? "遇到没响，先看「日志」里有没有事件、规则命中和播放结果。"
            : "遇到没响，可以切到“玩音效包”或“高级模式”查看日志。");

        ImGui.Separator();
        ImGui.Text("最近事件");
        var entries = eventLogService.Snapshot().Take(5).ToArray();
        if (entries.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "暂无事件。");
            return;
        }

        foreach (var entry in entries)
            ImGui.TextWrapped($"{entry.Timestamp:HH:mm:ss} [{entry.Level}] {entry.Message}");
    }

    private void DrawFirstRunModePicker()
    {
        ImGui.TextColored(new Vector4(0.92f, 0.94f, 0.96f, 1f), "先选一种用法，之后可以随时切换。");
        DrawModePickButton(ExperienceMode.Beginner, "我只想下载音效包玩", "community", new Vector2(226f, 44f));
        ImGui.SameLine();
        DrawModePickButton(ExperienceMode.Player, "我要管理很多音效包", "mine", new Vector2(226f, 44f));
        ImGui.Spacing();
        DrawModePickButton(ExperienceMode.Creator, "我要自己做音效", "library", new Vector2(200f, 44f));
        ImGui.SameLine();
        DrawModePickButton(ExperienceMode.Advanced, "我要完整编辑", "advanced", new Vector2(180f, 44f));

        ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), GetExperienceModeHint(configuration.ExperienceMode));
    }

    private void DrawModePickButton(ExperienceMode mode, string label, string targetTab, Vector2 size)
    {
        if (ImGui.Button($"{label}##PickMode{mode}", size))
        {
            SetExperienceMode(mode);
            RequestTab(targetTab);
        }
    }

    private void DrawHomeModeActions()
    {
        switch (configuration.ExperienceMode)
        {
            case ExperienceMode.Beginner:
                DrawHomeActionButton("安装社区音效包", "community", new Vector2(220f, 46f));
                ImGui.SameLine();
                DrawHomeActionButton("查看我的音效包", "mine", new Vector2(220f, 46f));
                ImGui.SameLine();
                if (ImGui.Button("查看插件教程##HomeTutorial", new Vector2(180f, 46f)))
                    OpenTutorial();
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "小白模式只保留安装、开关、试听和帮助。");
                break;

            case ExperienceMode.Player:
                DrawHomeActionButton("逛社区音效包", "community", new Vector2(220f, 46f));
                ImGui.SameLine();
                DrawHomeActionButton("管理我的音效包", "mine", new Vector2(220f, 46f));
                ImGui.SameLine();
                DrawHomeActionButton("查看日志", "log", new Vector2(160f, 46f));
                break;

            case ExperienceMode.Creator:
                DrawHomeActionButton("导入音效文件", "library", new Vector2(200f, 46f));
                ImGui.SameLine();
                if (ImGui.Button("新建规则向导##HomeWizard", new Vector2(200f, 46f)))
                {
                    StartRuleWizard();
                    RequestTab("mine");
                }

                ImGui.SameLine();
                DrawHomeActionButton("生成投稿包", "mine", new Vector2(180f, 46f));
                break;

            case ExperienceMode.Advanced:
                DrawHomeActionButton("完整规则编辑", "advanced", new Vector2(200f, 46f));
                ImGui.SameLine();
                DrawHomeActionButton("导入导出分享包", "advanced", new Vector2(220f, 46f));
                ImGui.SameLine();
                DrawHomeActionButton("查看日志", "log", new Vector2(160f, 46f));
                break;

            case ExperienceMode.Reviewer:
                DrawHomeActionButton("社区审核发布", "advanced", new Vector2(200f, 46f));
                ImGui.SameLine();
                DrawHomeActionButton("完整规则编辑", "advanced", new Vector2(200f, 46f));
                ImGui.SameLine();
                DrawHomeActionButton("查看日志", "log", new Vector2(160f, 46f));
                break;
        }
    }

    private void DrawHomeActionButton(string label, string targetTab, Vector2 size)
    {
        if (ImGui.Button($"{label}##HomeAction{targetTab}{label}", size))
            RequestTab(targetTab);
    }
}
