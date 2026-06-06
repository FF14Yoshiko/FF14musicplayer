using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AllTimeSoundTrigger.UI;

public sealed partial class MainWindow
{
    private void DrawHomeTab()
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.30f, 0.78f, 1f, 1f), "今天想做什么？");
        ImGui.TextColored(
            new Vector4(0.70f, 0.72f, 0.76f, 1f),
            $"当前方案：{profileStorageService.ActiveProfile.Name} / 分组 {profileStorageService.ActiveProfile.Groups.Count} / 规则 {profileStorageService.GetActiveRules().Count} / 音效 {configuration.SoundLibrary.Entries.Count}");

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
        ImGui.BulletText(profileStorageService.GetActiveRules().Count == 0
            ? "当前方案还没有规则，可以点上面的“自己做一个音效规则”。"
            : $"当前方案已有 {profileStorageService.GetActiveRules().Count} 条规则。");
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
