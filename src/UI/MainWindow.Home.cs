using System.Linq;
using System.Numerics;
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
        if (ImGui.Button("安装社区音效包##HomeCommunity", new Vector2(220f, 46f)))
            RequestTab("community");

        ImGui.SameLine();
        if (ImGui.Button("自己做一个音效规则##HomeWizard", new Vector2(220f, 46f)))
        {
            StartRuleWizard();
            RequestTab("mine");
        }

        ImGui.SameLine();
        if (ImGui.Button("查看插件教程##HomeTutorial", new Vector2(180f, 46f)))
            OpenTutorial();

        ImGui.Separator();
        ImGui.Text("快速检查");
        ImGui.BulletText(configuration.SoundLibrary.Entries.Count == 0
            ? "音效库还是空的，先去「音效库」导入 mp3/wav/ogg。"
            : $"音效库里已有 {configuration.SoundLibrary.Entries.Count} 个音效。");
        ImGui.BulletText(totalRuleCount == 0
            ? "现在还没有规则，可以点上面的“自己做一个音效规则”。"
            : $"现在已有 {totalRuleCount} 条规则，其中 {runtimeRuleCount} 条所在分组已启用。");
        ImGui.BulletText("遇到没响，先看「日志」里有没有事件、规则命中和播放结果。");

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
}
