using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using AllTimeSoundTrigger.Community;
using AllTimeSoundTrigger.ConfigurationModels;
using Dalamud.Bindings.ImGui;

namespace AllTimeSoundTrigger.UI;

public sealed partial class MainWindow
{
    private static readonly string[] SubmissionScopes = ["整个方案", "某个分组", "单条规则"];

    private int submissionScopeIndex;
    private string submissionGroupId = string.Empty;
    private string submissionRuleId = string.Empty;
    private string submissionPackageName = string.Empty;
    private string submissionAuthor = string.Empty;
    private string submissionVersion = "1.0.0";
    private string submissionCoverPath = string.Empty;
    private string submissionOutputPath = string.Empty;
    private string submissionMessage = string.Empty;

    private string reviewPackagePath = string.Empty;
    private string reviewRepoPath = string.Empty;
    private string reviewCoverPath = string.Empty;
    private string reviewPackId = string.Empty;
    private string reviewPackName = string.Empty;
    private string reviewAuthor = string.Empty;
    private string reviewVersion = "1.0.0";
    private string reviewTagsText = string.Empty;
    private string reviewDescription = string.Empty;
    private string reviewReadme = string.Empty;
    private string reviewMessage = string.Empty;
    private bool reviewAllowOverwrite;
    private bool reviewPushToRemote = true;
    private CommunitySubmissionValidation? reviewValidation;

    private void DrawCommunitySubmissionPanel()
    {
        EnsureSubmissionDefaults();

        if (!ImGui.CollapsingHeader("投稿音效包", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextColored(
            new Vector4(0.70f, 0.72f, 0.76f, 1f),
            "生成投稿文件后，把 .sfxpack 和 投稿信息.txt 发到邮箱 1104449674@qq.com，或 QQ 群 659827727。");

        DrawSubmissionScopePicker();
        DrawInputText("包名##SubmissionName", submissionPackageName, 120, value =>
        {
            submissionPackageName = value;
            submissionOutputPath = sfxPackService.BuildDefaultSubmissionPath(value);
        }, 320f);

        DrawInputText("作者##SubmissionAuthor", submissionAuthor, 80, value => submissionAuthor = value, 220f);
        DrawInputText("版本##SubmissionVersion", submissionVersion, 32, value => submissionVersion = string.IsNullOrWhiteSpace(value) ? "1.0.0" : value, 120f);

        DrawInputText("封面图，可空置##SubmissionCoverPath", submissionCoverPath, 520, value => submissionCoverPath = value);
        ImGui.SameLine();
        if (ImGui.Button("选择封面##PickSubmissionCover"))
            OpenSubmissionCoverDialog();

        DrawInputText("生成位置##SubmissionOutputPath", submissionOutputPath, 520, value => submissionOutputPath = value);
        ImGui.SameLine();
        if (ImGui.Button("选择位置##PickSubmissionOutput"))
            OpenSubmissionOutputDialog();

        if (ImGui.Button("生成投稿文件"))
            ExportCommunitySubmission();

        ImGui.SameLine();
        if (ImGui.Button("打开投稿目录"))
            OpenSubmissionDirectory();

        if (!string.IsNullOrWhiteSpace(submissionMessage))
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), submissionMessage);
    }

    private void DrawSubmissionScopePicker()
    {
        ImGui.SetNextItemWidth(180f);
        if (ImGui.BeginCombo("要上传的内容##SubmissionScope", SubmissionScopes[Math.Clamp(submissionScopeIndex, 0, SubmissionScopes.Length - 1)]))
        {
            for (var i = 0; i < SubmissionScopes.Length; i++)
            {
                var selected = submissionScopeIndex == i;
                if (ImGui.Selectable($"{SubmissionScopes[i]}##SubmissionScope{i}", selected))
                {
                    submissionScopeIndex = i;
                    ResetSubmissionNameFromSelection();
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (submissionScopeIndex == 1)
            DrawSubmissionGroupCombo();
        else if (submissionScopeIndex == 2)
            DrawSubmissionRuleCombo();
    }

    private void DrawSubmissionGroupCombo()
    {
        var groups = profileStorageService.ActiveProfile.Groups;
        var current = groups.FirstOrDefault(group => group.Id.Equals(submissionGroupId, StringComparison.Ordinal))
            ?? groups.FirstOrDefault();
        if (current == null)
            return;

        submissionGroupId = current.Id;
        ImGui.SetNextItemWidth(320f);
        if (!ImGui.BeginCombo("选择分组##SubmissionGroup", current.Name))
            return;

        foreach (var group in groups)
        {
            var selected = group.Id.Equals(submissionGroupId, StringComparison.Ordinal);
            if (ImGui.Selectable($"{group.Name}##SubmissionGroup{group.Id}", selected))
            {
                submissionGroupId = group.Id;
                ResetSubmissionNameFromSelection();
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private void DrawSubmissionRuleCombo()
    {
        var rules = profileStorageService.ActiveProfile.Groups
            .SelectMany(group => group.Rules.Select(rule => (Group: group, Rule: rule)))
            .ToList();
        var current = rules.FirstOrDefault(item => item.Rule.Id.Equals(submissionRuleId, StringComparison.Ordinal));
        if (current.Rule == null)
            current = rules.FirstOrDefault();
        if (current.Rule == null)
            return;

        submissionRuleId = current.Rule.Id;
        ImGui.SetNextItemWidth(360f);
        if (!ImGui.BeginCombo("选择规则##SubmissionRule", $"{current.Group.Name} / {current.Rule.Name}"))
            return;

        foreach (var item in rules)
        {
            var selected = item.Rule.Id.Equals(submissionRuleId, StringComparison.Ordinal);
            if (ImGui.Selectable($"{item.Group.Name} / {item.Rule.Name}##SubmissionRule{item.Rule.Id}", selected))
            {
                submissionRuleId = item.Rule.Id;
                submissionGroupId = item.Group.Id;
                ResetSubmissionNameFromSelection();
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private void ExportCommunitySubmission()
    {
        try
        {
            var selection = BuildSubmissionSelection();
            if (selection.Rules.Count == 0)
            {
                submissionMessage = "请先选择至少一条规则。";
                return;
            }

            var manifest = BuildSubmissionManifest(selection);
            var outputPath = string.IsNullOrWhiteSpace(submissionOutputPath)
                ? sfxPackService.BuildDefaultSubmissionPath(manifest.Name)
                : submissionOutputPath;

            var result = sfxPackService.ExportSubmission(
                profileStorageService.ActiveProfile,
                selection.GroupIds,
                selection.RuleIds,
                configuration.SoundLibrary,
                outputPath,
                manifest,
                submissionCoverPath);

            if (!result.Success)
            {
                submissionMessage = result.Message;
                return;
            }

            var infoPath = sfxPackService.WriteSubmissionInfoText(result.PackagePath, manifest, submissionCoverPath);
            configuration.CommunitySubmissionAuthor = manifest.Author;
            configuration.Save();
            submissionOutputPath = result.PackagePath;
            submissionMessage = $"投稿文件已生成：{Path.GetFileName(result.PackagePath)}，{Path.GetFileName(infoPath)}。请发送到邮箱 1104449674@qq.com 或 QQ 群 659827727。";
            OpenFileInExplorer(result.PackagePath);
        }
        catch (Exception ex)
        {
            submissionMessage = $"生成投稿文件失败：{ex.Message}";
        }
    }

    private CommunitySubmissionManifest BuildSubmissionManifest(SubmissionSelection selection)
    {
        var name = string.IsNullOrWhiteSpace(submissionPackageName)
            ? selection.DisplayName
            : submissionPackageName.Trim();
        var author = string.IsNullOrWhiteSpace(submissionAuthor)
            ? FirstNonEmpty(configuration.CommunitySubmissionAuthor, Environment.UserName, "未署名玩家")
            : submissionAuthor.Trim();

        var manifest = new CommunitySubmissionManifest
        {
            Id = BuildEnglishId(name),
            Name = name,
            Author = author,
            PackageVersion = string.IsNullOrWhiteSpace(submissionVersion) ? "1.0.0" : submissionVersion.Trim(),
            Description = BuildAutoSubmissionDescription(selection),
            Tags = BuildAutoSubmissionTags(selection).ToList()
        };

        manifest.Readme = BuildAutoSubmissionReadme(manifest, selection);
        manifest.Normalize();
        return manifest;
    }

    private SubmissionSelection BuildSubmissionSelection()
    {
        var profile = profileStorageService.ActiveProfile;
        var groupIds = new List<string>();
        var ruleIds = new List<string>();
        var rules = new List<RuleDefinition>();
        var displayName = profile.Name;

        if (submissionScopeIndex == 0)
        {
            groupIds.AddRange(profile.Groups.Select(group => group.Id));
            rules.AddRange(profile.EnumerateRules());
        }
        else if (submissionScopeIndex == 1)
        {
            var group = profile.Groups.FirstOrDefault(item => item.Id.Equals(submissionGroupId, StringComparison.Ordinal))
                ?? profile.Groups.FirstOrDefault();
            if (group != null)
            {
                groupIds.Add(group.Id);
                rules.AddRange(group.Rules);
                displayName = group.Name;
            }
        }
        else
        {
            var item = profile.Groups
                .SelectMany(group => group.Rules.Select(rule => (Group: group, Rule: rule)))
                .FirstOrDefault(value => value.Rule.Id.Equals(submissionRuleId, StringComparison.Ordinal));
            if (item.Rule != null)
            {
                ruleIds.Add(item.Rule.Id);
                rules.Add(item.Rule);
                displayName = item.Rule.Name;
            }
        }

        return new SubmissionSelection(displayName, groupIds, ruleIds, rules);
    }

    private void EnsureSubmissionDefaults()
    {
        if (string.IsNullOrWhiteSpace(submissionGroupId))
            submissionGroupId = profileStorageService.ActiveProfile.Groups.FirstOrDefault()?.Id ?? string.Empty;
        if (string.IsNullOrWhiteSpace(submissionRuleId))
            submissionRuleId = profileStorageService.GetActiveRules().FirstOrDefault()?.Id ?? string.Empty;
        if (string.IsNullOrWhiteSpace(submissionPackageName))
            ResetSubmissionNameFromSelection();
        if (string.IsNullOrWhiteSpace(submissionAuthor))
            submissionAuthor = FirstNonEmpty(configuration.CommunitySubmissionAuthor, Environment.UserName);
        if (string.IsNullOrWhiteSpace(submissionVersion))
            submissionVersion = "1.0.0";
        if (string.IsNullOrWhiteSpace(submissionOutputPath))
            submissionOutputPath = sfxPackService.BuildDefaultSubmissionPath(submissionPackageName);
    }

    private void ResetSubmissionNameFromSelection()
    {
        var selection = BuildSubmissionSelection();
        submissionPackageName = string.IsNullOrWhiteSpace(selection.DisplayName)
            ? profileStorageService.ActiveProfile.Name
            : selection.DisplayName;
        submissionOutputPath = sfxPackService.BuildDefaultSubmissionPath(submissionPackageName);
    }

    private string BuildAutoSubmissionDescription(SubmissionSelection selection)
    {
        var triggerNames = selection.Rules
            .Select(rule => FormatTriggerSummary(rule.Trigger))
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(4)
            .ToArray();
        var ruleNames = selection.Rules
            .Select(rule => rule.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(3)
            .ToArray();

        var triggerText = triggerNames.Length == 0 ? "多种游戏事件" : string.Join("、", triggerNames);
        var ruleText = ruleNames.Length == 0 ? string.Empty : $"，包含 {string.Join("、", ruleNames)} 等规则";
        return $"包含 {selection.Rules.Count} 条规则，覆盖 {triggerText}{ruleText}。";
    }

    private IEnumerable<string> BuildAutoSubmissionTags(SubmissionSelection selection)
    {
        var tags = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase) { "玩家投稿" };
        var haystack = $"{selection.DisplayName} {string.Join(' ', selection.Rules.Select(rule => rule.Name))}";

        foreach (var rule in selection.Rules)
        {
            var trigger = rule.Trigger;
            if (trigger == null)
                continue;

            AddTriggerTags(tags, trigger);
            AddKeywordTag(tags, trigger.SkillNameContains);
            AddKeywordTag(tags, trigger.StatusNameContains);
            AddKeywordTag(tags, trigger.JobNameContains);
            AddKeywordTag(tags, trigger.ItemNameContains);
        }

        if (ContainsAny(haystack, "pvp", "PVP", "战场", "水晶冲突", "纷争前线"))
            tags.Add("PVP");
        if (ContainsAny(haystack, "pve", "PVE", "高难", "零式", "绝本", "副本", "讨伐"))
            tags.Add("PVE");

        return tags.Take(8);
    }

    private string BuildAutoSubmissionReadme(CommunitySubmissionManifest manifest, SubmissionSelection selection)
    {
        var lines = new List<string>
        {
            manifest.Name,
            string.Empty,
            $"作者：{manifest.Author}",
            $"版本：{manifest.PackageVersion}",
            string.Empty,
            manifest.Description,
            string.Empty,
            "规则列表："
        };

        for (var i = 0; i < selection.Rules.Count; i++)
        {
            var rule = selection.Rules[i];
            lines.Add($"{i + 1}. {rule.Name}");
            lines.Add($"   触发：{FormatTriggerSummary(rule.Trigger)}");
            lines.Add($"   音效：{FormatActionSummary(rule.Actions)}");
        }

        lines.Add(string.Empty);
        lines.Add("投稿方式：");
        lines.Add("请将 .sfxpack、投稿信息.txt，以及可选配图发送到邮箱 1104449674@qq.com，或 QQ 群 659827727。");
        lines.Add("审核通过后会出现在插件的社区列表中。");
        return string.Join(Environment.NewLine, lines);
    }

    private string FormatActionSummary(IReadOnlyList<ActionDefinition> actions)
    {
        var soundNames = new List<string>();
        foreach (var action in actions.Where(action => action.Type.Equals("Sound", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var soundId in GetSelectedSoundIds(action))
            {
                var entry = configuration.SoundLibrary.FindById(soundId);
                soundNames.Add(entry?.Name ?? soundId);
            }

            if (soundNames.Count == 0 && !string.IsNullOrWhiteSpace(action.FilePath))
                soundNames.Add(Path.GetFileName(action.FilePath));
        }

        return soundNames.Count == 0
            ? "无音效动作或使用日志/停止动作"
            : string.Join(" / ", soundNames.Distinct(StringComparer.CurrentCultureIgnoreCase));
    }

    private static string FormatTriggerSummary(TriggerDefinition? trigger)
    {
        if (trigger == null)
            return "未知触发器";

        return trigger.Type switch
        {
            "SkillUsed" => string.IsNullOrWhiteSpace(trigger.SkillNameContains) ? "使用技能" : $"使用技能：{trigger.SkillNameContains}",
            "ItemAcquired" => string.IsNullOrWhiteSpace(trigger.ItemNameContains) ? "获得物品" : $"获得物品：{trigger.ItemNameContains}",
            "MapChanged" => trigger.TerritoryType > 0 ? $"切换地图：{trigger.TerritoryType}" : "切换地图",
            "CombatEntered" => "进入战斗",
            "CombatExited" => "脱离战斗",
            "HpChanged" => "血量变化",
            "HpLow" => $"血量低于 {Math.Clamp(trigger.HpPercentBelow, 1, 100)}%",
            "LocalPlayerDefeated" => "本机玩家被击倒",
            "JobChanged" => string.IsNullOrWhiteSpace(trigger.JobNameContains) ? "切换职业" : $"切换职业：{trigger.JobNameContains}",
            "StatusGained" => string.IsNullOrWhiteSpace(trigger.StatusNameContains) ? "获得 Buff" : $"获得 Buff：{trigger.StatusNameContains}",
            "StatusLost" => string.IsNullOrWhiteSpace(trigger.StatusNameContains) ? "Buff 消失" : $"Buff 消失：{trigger.StatusNameContains}",
            "Kill" => string.IsNullOrWhiteSpace(trigger.TargetName) ? "击杀目标" : $"击杀：{trigger.TargetName}",
            "EventType" => string.IsNullOrWhiteSpace(trigger.EventType) ? "原始事件" : $"原始事件：{trigger.EventType}",
            _ => trigger.Type
        };
    }

    private static void AddTriggerTags(HashSet<string> tags, TriggerDefinition trigger)
    {
        switch (trigger.Type)
        {
            case "SkillUsed":
                tags.Add("技能");
                break;
            case "StatusGained":
            case "StatusLost":
                tags.Add("Buff");
                break;
            case "Kill":
                tags.Add("击杀");
                break;
            case "CombatEntered":
            case "CombatExited":
                tags.Add("战斗");
                break;
            case "MapChanged":
                tags.Add("地图");
                break;
            case "HpChanged":
            case "HpLow":
                tags.Add("血量");
                break;
            case "LocalPlayerDefeated":
                tags.Add("倒地");
                break;
            case "JobChanged":
                tags.Add("职业");
                break;
            case "ItemAcquired":
                tags.Add("物品");
                break;
        }
    }

    private static void AddKeywordTag(HashSet<string> tags, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var keyword in new[] { "武士", "蝰蛇", "黑魔", "白魔", "忍者", "龙骑", "机工", "诗人", "召唤", "学者", "占星", "贤者", "战士", "骑士", "暗黑", "绝枪" })
        {
            if (value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
                tags.Add(keyword);
        }
    }

    private static bool ContainsAny(string value, params string[] keywords)
        => keywords.Any(keyword => value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));

    private string BuildEnglishId(string packageName)
    {
        var text = packageName;
        var replacements = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase)
        {
            ["无名"] = "nameless",
            ["蝰蛇"] = "viper",
            ["剑士"] = "swordsman",
            ["武士"] = "samurai",
            ["忍者"] = "ninja",
            ["龙骑"] = "dragoon",
            ["黑魔"] = "black-mage",
            ["白魔"] = "white-mage",
            ["占星"] = "astrologian",
            ["贤者"] = "sage",
            ["战士"] = "warrior",
            ["骑士"] = "paladin",
            ["暗黑"] = "dark-knight",
            ["绝枪"] = "gunbreaker",
            ["击杀"] = "kill",
            ["处刑"] = "execution",
            ["技能"] = "skill",
            ["高难"] = "raid",
            ["日常"] = "daily",
            ["音效"] = "sfx",
            ["语音"] = "voice",
            ["整活"] = "fun"
        };

        foreach (var item in replacements)
            text = text.Replace(item.Key, $"-{item.Value}-", StringComparison.CurrentCultureIgnoreCase);

        return CommunitySubmissionManifest.NormalizeId(text);
    }

    private void OpenSubmissionCoverDialog()
    {
        try
        {
            using var dialog = new OpenFileDialog
            {
                Title = "选择投稿封面",
                Filter = "图片 (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
                submissionCoverPath = dialog.FileName;
        }
        catch (Exception ex)
        {
            submissionMessage = $"选择封面失败：{ex.Message}";
        }
    }

    private void OpenSubmissionOutputDialog()
    {
        try
        {
            using var dialog = new SaveFileDialog
            {
                Title = "保存投稿包",
                Filter = "音效投稿包 (*.sfxpack)|*.sfxpack|所有文件 (*.*)|*.*",
                DefaultExt = "sfxpack",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                FileName = $"{submissionPackageName}.sfxpack"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
                submissionOutputPath = dialog.FileName;
        }
        catch (Exception ex)
        {
            submissionMessage = $"选择生成位置失败：{ex.Message}";
        }
    }

    private void OpenSubmissionDirectory()
    {
        var path = string.IsNullOrWhiteSpace(submissionOutputPath)
            ? sfxPackService.SubmissionDirectory
            : submissionOutputPath;
        var directory = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        if (!string.IsNullOrWhiteSpace(directory))
            OpenCommunityUrl(directory);
    }

    private static void OpenFileInExplorer(string path)
    {
        if (!File.Exists(path))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true
        });
    }

    private static string FirstNonEmpty(params string[] values)
        => values.Select(value => (value ?? string.Empty).Trim()).FirstOrDefault(value => value.Length > 0) ?? string.Empty;

    private void DrawDeveloperModeToggle()
    {
        var io = ImGui.GetIO();
        if (!io.KeyCtrl || !io.KeyShift)
            return;

        ImGui.SameLine();
        if (ImGui.SmallButton(configuration.CommunityDeveloperMode ? "关闭开发者模式" : "开发者模式"))
        {
            configuration.CommunityDeveloperMode = !configuration.CommunityDeveloperMode;
            configuration.Save();
        }
    }

    private void DrawCommunityDeveloperPanel()
    {
        EnsureReviewDefaults();
        if (!ImGui.CollapsingHeader("开发者审核发布", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextColored(
            new Vector4(1f, 0.72f, 0.35f, 1f),
            "开发者模式：待审包通过后才会写入 Gitee 社区仓库，并更新 index.json。");

        DrawInputText("Gitee 仓库路径##ReviewRepoPath", reviewRepoPath, 520, value => reviewRepoPath = value);
        ImGui.SameLine();
        if (ImGui.Button("选择仓库##PickReviewRepo"))
            OpenReviewRepoDialog();

        DrawInputText("待审核 .sfxpack##ReviewPackagePath", reviewPackagePath, 520, value => reviewPackagePath = value);
        ImGui.SameLine();
        if (ImGui.Button("选择待审包##PickReviewPackage"))
            OpenReviewPackageDialog();

        if (ImGui.Button("预览待审包"))
            PreviewReviewPackage();

        if (reviewValidation != null)
        {
            ImGui.TextColored(
                new Vector4(0.70f, 0.72f, 0.76f, 1f),
                $"方案：{reviewValidation.ProfileName} / 分组 {reviewValidation.GroupCount} / 规则 {reviewValidation.RuleCount} / 音效 {reviewValidation.SoundCount} / {FormatBytes(reviewValidation.TotalSoundBytes)}");
            if (!string.IsNullOrWhiteSpace(reviewValidation.CoverEntryName))
                ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), $"包内封面：{reviewValidation.CoverEntryName}");
        }

        DrawInputText("包 ID##ReviewPackId", reviewPackId, 100, value => reviewPackId = CommunitySubmissionManifest.NormalizeId(value), 260f);
        DrawInputText("包名##ReviewPackName", reviewPackName, 120, value => reviewPackName = value, 320f);
        DrawInputText("作者##ReviewAuthor", reviewAuthor, 80, value => reviewAuthor = value, 220f);
        DrawInputText("版本##ReviewVersion", reviewVersion, 32, value => reviewVersion = string.IsNullOrWhiteSpace(value) ? "1.0.0" : value, 120f);
        DrawInputText("标签，逗号分隔##ReviewTags", reviewTagsText, 220, value => reviewTagsText = value, 420f);
        DrawInputText("简介##ReviewDescription", reviewDescription, 260, value => reviewDescription = value, 520f);

        DrawInputText("审核封面，可覆盖包内封面##ReviewCoverPath", reviewCoverPath, 520, value => reviewCoverPath = value);
        ImGui.SameLine();
        if (ImGui.Button("选择封面##PickReviewCover"))
            OpenReviewCoverDialog();

        if (ImGui.CollapsingHeader("README 预览 / 可修改"))
            DrawInputText("README##ReviewReadme", reviewReadme, 4000, value => reviewReadme = value, 680f);

        if (ImGui.Checkbox("允许覆盖同 ID 音效包##ReviewAllowOverwrite", ref reviewAllowOverwrite))
            reviewMessage = "覆盖开关已更新。";
        if (ImGui.Checkbox("审核通过后推送到 Gitee##ReviewPush", ref reviewPushToRemote))
            reviewMessage = reviewPushToRemote ? "将会推送到 Gitee。" : "只提交到本地社区仓库。";

        var publishing = communityPublishTask is { IsCompleted: false };
        ImGui.BeginDisabled(publishing);
        if (ImGui.Button(reviewPushToRemote ? "审核通过并推送 Gitee" : "审核通过并本地提交"))
            StartReviewPublish();
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("关闭开发者模式"))
        {
            configuration.CommunityDeveloperMode = false;
            configuration.Save();
        }

        if (!string.IsNullOrWhiteSpace(reviewMessage))
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), reviewMessage);
    }

    private void CompleteCommunityPublishOperation()
    {
        if (communityPublishTask is not { IsCompleted: true } task)
            return;

        communityPublishTask = null;
        if (task.IsFaulted)
        {
            reviewMessage = $"发布失败：{task.Exception?.GetBaseException().Message ?? "未知错误"}";
            return;
        }

        var result = task.Result;
        reviewMessage = result.Message;
        if (result.Pack != null)
            communityMessage = $"社区包已更新：{result.Pack.Name}";
    }

    private void StartReviewPublish()
    {
        try
        {
            if (communityPublishTask is { IsCompleted: false })
                return;

            configuration.CommunityRepositoryPath = reviewRepoPath;
            configuration.Save();

            var request = new CommunityPublishRequest
            {
                RepositoryPath = reviewRepoPath,
                PackagePath = reviewPackagePath,
                CoverPath = reviewCoverPath,
                Id = reviewPackId,
                Name = reviewPackName,
                Author = reviewAuthor,
                Description = reviewDescription,
                PackageVersion = reviewVersion,
                TagsText = reviewTagsText,
                Readme = reviewReadme,
                AllowOverwrite = reviewAllowOverwrite,
                PushToRemote = reviewPushToRemote
            };

            reviewMessage = reviewPushToRemote ? "正在审核、提交并推送 Gitee..." : "正在审核并提交到本地社区仓库...";
            communityPublishTask = Task.Run(() => CommunitySubmissionPublisher.ApproveAndPublish(request));
        }
        catch (Exception ex)
        {
            reviewMessage = $"发布失败：{ex.Message}";
        }
    }

    private void PreviewReviewPackage()
    {
        try
        {
            reviewValidation = CommunitySubmissionPublisher.ValidatePackage(reviewPackagePath);
            var manifest = reviewValidation.Manifest;

            reviewPackName = FirstNonEmpty(reviewPackName, manifest?.Name ?? string.Empty, reviewValidation.ProfileName);
            reviewAuthor = FirstNonEmpty(reviewAuthor, manifest?.Author ?? string.Empty, "肘击");
            reviewVersion = FirstNonEmpty(reviewVersion, manifest?.PackageVersion ?? string.Empty, "1.0.0");
            reviewPackId = FirstNonEmpty(reviewPackId, manifest?.Id ?? string.Empty, BuildEnglishId(reviewPackName));
            reviewDescription = FirstNonEmpty(reviewDescription, manifest?.Description ?? string.Empty, $"由玩家投稿的「{reviewPackName}」音效包。");
            reviewTagsText = FirstNonEmpty(reviewTagsText, manifest == null ? string.Empty : string.Join("，", manifest.Tags), "玩家投稿");
            reviewReadme = FirstNonEmpty(reviewReadme, manifest?.Readme ?? string.Empty, reviewValidation.Readme, BuildFallbackReviewReadme());
            reviewMessage = "待审包预览已加载。";
        }
        catch (Exception ex)
        {
            reviewValidation = null;
            reviewMessage = $"预览失败：{ex.Message}";
        }
    }

    private string BuildFallbackReviewReadme()
    {
        if (reviewValidation == null)
            return string.Empty;

        return $"""
               {reviewPackName}

               作者：{reviewAuthor}

               {reviewDescription}

               内容：
               - 分组：{reviewValidation.GroupCount}
               - 规则：{reviewValidation.RuleCount}
               - 音效：{reviewValidation.SoundCount}
               """.Trim();
    }

    private void EnsureReviewDefaults()
    {
        if (string.IsNullOrWhiteSpace(reviewRepoPath))
            reviewRepoPath = configuration.CommunityRepositoryPath;
        if (string.IsNullOrWhiteSpace(reviewVersion))
            reviewVersion = "1.0.0";
    }

    private void OpenReviewRepoDialog()
    {
        try
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "选择 ffxiv-sfx-community 本地仓库",
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(reviewRepoPath) ? reviewRepoPath : string.Empty
            };

            if (dialog.ShowDialog() == DialogResult.OK)
                reviewRepoPath = dialog.SelectedPath;
        }
        catch (Exception ex)
        {
            reviewMessage = $"选择仓库失败：{ex.Message}";
        }
    }

    private void OpenReviewPackageDialog()
    {
        try
        {
            using var dialog = new OpenFileDialog
            {
                Title = "选择待审核投稿包",
                Filter = "音效投稿包 (*.sfxpack)|*.sfxpack|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                reviewPackagePath = dialog.FileName;
                reviewPackId = string.Empty;
                reviewPackName = string.Empty;
                reviewDescription = string.Empty;
                reviewTagsText = string.Empty;
                reviewReadme = string.Empty;
                PreviewReviewPackage();
            }
        }
        catch (Exception ex)
        {
            reviewMessage = $"选择待审包失败：{ex.Message}";
        }
    }

    private void OpenReviewCoverDialog()
    {
        try
        {
            using var dialog = new OpenFileDialog
            {
                Title = "选择审核封面",
                Filter = "图片 (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
                reviewCoverPath = dialog.FileName;
        }
        catch (Exception ex)
        {
            reviewMessage = $"选择封面失败：{ex.Message}";
        }
    }

    private sealed record SubmissionSelection(
        string DisplayName,
        IReadOnlyCollection<string> GroupIds,
        IReadOnlyCollection<string> RuleIds,
        IReadOnlyList<RuleDefinition> Rules);
}
