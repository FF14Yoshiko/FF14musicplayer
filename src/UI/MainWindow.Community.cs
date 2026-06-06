using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AllTimeSoundTrigger.Community;
using AllTimeSoundTrigger.ConfigurationModels;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AllTimeSoundTrigger.UI;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, Task> communityCoverTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ISharedImmediateTexture> communityCoverTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CommunityInstallOperation> communityInstallOperations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> communityInstallMessages = new(StringComparer.OrdinalIgnoreCase);
    private Task<CommunityRefreshResult>? communityRefreshTask;
    private Task<CommunityPublishResult>? communityPublishTask;
    private string communitySearchText = string.Empty;
    private string communityMessage = string.Empty;
    private string communityInstallProfileId = string.Empty;
    private string communityInstallGroupId = string.Empty;
    private string communityInstallNewGroupName = string.Empty;
    private bool communityInstallCreateGroup;
    private bool communityRefreshStarted;

    private void DrawCommunityTab()
    {
        CompleteCommunityOperations();
        CompleteCommunityPublishOperation();
        if (!communityRefreshStarted)
        {
            communityRefreshStarted = true;
            if (communityPackService.Packs.Count == 0)
                StartCommunityRefresh();
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.30f, 0.78f, 1f, 1f), "社区音效包");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "来自 Gitee 的 .sfxpack 列表");

        if (ImGui.Button("刷新列表"))
            StartCommunityRefresh();

        if (configuration.CommunityDeveloperMode)
        {
            ImGui.SameLine();
            if (ImGui.Button("打开社区仓库"))
                OpenCommunityUrl("https://gitee.com/aikyan931023/ffxiv-sfx-community");

            ImGui.SameLine();
            if (ImGui.Button("打开缓存目录"))
                OpenCommunityUrl(communityPackService.CacheDirectory);
        }

        DrawInputText("搜索##CommunitySearch", communitySearchText, 120, value => communitySearchText = value, 260f);

        if (communityRefreshTask is { IsCompleted: false })
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "正在刷新社区列表...");
        else if (!string.IsNullOrWhiteSpace(communityMessage))
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), communityMessage);

        ImGui.Separator();
        DrawCommunityInstallTargetPanel();
        ImGui.Separator();

        var packs = FilterCommunityPacks(communityPackService.Packs).ToArray();
        if (packs.Length == 0)
        {
            ImGui.Text("当前没有可显示的社区音效包。");
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), CommunityPackService.DefaultIndexUrl);
            return;
        }

        if (!ImGui.BeginTable("##CommunityPackTable", 2, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("封面", ImGuiTableColumnFlags.WidthFixed, 168f);
        ImGui.TableSetupColumn("音效包", ImGuiTableColumnFlags.WidthStretch);

        foreach (var pack in packs)
        {
            ImGui.PushID(pack.Id);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCommunityCover(pack);

            ImGui.TableNextColumn();
            DrawCommunityPackInfo(pack);
            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void StartCommunityRefresh()
    {
        if (communityRefreshTask is { IsCompleted: false })
            return;

        communityMessage = "正在连接 Gitee 社区仓库...";
        communityRefreshTask = communityPackService.RefreshIndexAsync();
    }

    private void CompleteCommunityOperations()
    {
        if (communityRefreshTask is { IsCompleted: true } refreshTask)
        {
            communityRefreshTask = null;
            if (refreshTask.IsFaulted)
            {
                var cached = communityPackService.TryLoadCachedIndex();
                communityMessage = cached.PackCount > 0
                    ? $"刷新失败，已使用本地缓存：{GetTaskExceptionMessage(refreshTask)}"
                    : $"刷新失败：{GetTaskExceptionMessage(refreshTask)}";
            }
            else
            {
                var result = refreshTask.Result;
                communityMessage = $"已刷新社区列表：{result.PackCount} 个音效包。";
            }
        }

        foreach (var item in communityCoverTasks.ToArray())
        {
            if (item.Value.IsCompleted)
                communityCoverTasks.Remove(item.Key);
        }

        foreach (var item in communityInstallOperations.ToArray())
        {
            var operation = item.Value;
            if (!operation.DownloadTask.IsCompleted)
                continue;

            communityInstallOperations.Remove(item.Key);
            if (operation.DownloadTask.IsFaulted)
            {
                communityInstallMessages[item.Key] = $"安装失败：{GetTaskExceptionMessage(operation.DownloadTask)}";
                continue;
            }

            try
            {
                communityInstallMessages[item.Key] = "正在导入...";
                InstallDownloadedCommunityPack(operation.Pack, operation.DownloadTask.Result, operation.Target);
            }
            catch (Exception ex)
            {
                communityInstallMessages[item.Key] = $"导入失败：{ex.Message}";
            }
        }
    }

    private IEnumerable<CommunityPackInfo> FilterCommunityPacks(IEnumerable<CommunityPackInfo> packs)
    {
        var keyword = (communitySearchText ?? string.Empty).Trim();
        if (keyword.Length == 0)
            return packs;

        return packs.Where(pack =>
            Contains(pack.Name, keyword)
            || Contains(pack.Author, keyword)
            || Contains(pack.Description, keyword)
            || pack.Tags.Any(tag => Contains(tag, keyword)));
    }

    private static bool Contains(string value, string keyword)
        => (value ?? string.Empty).Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    private void DrawCommunityCover(CommunityPackInfo pack)
    {
        var size = new Vector2(150f, 84f);
        if (string.IsNullOrWhiteSpace(pack.CoverUrl))
        {
            DrawCommunityCoverPlaceholder(size, "暂无封面");
            return;
        }

        var coverPath = communityPackService.GetCoverCachePath(pack);
        if (File.Exists(coverPath))
        {
            if (!communityCoverTextures.TryGetValue(coverPath, out var texture))
            {
                texture = textureProvider.GetFromFile(coverPath);
                communityCoverTextures[coverPath] = texture;
            }

            if (texture.TryGetWrap(out var wrap, out _) && wrap != null)
            {
                ImGui.BeginChild("##CommunityCoverImage", size, true);
                var imageSize = CalculateContainedImageSize(new Vector2(wrap.Width, wrap.Height), size);
                var cursor = ImGui.GetCursorPos();
                ImGui.SetCursorPos(new Vector2(
                    cursor.X + Math.Max(0f, (size.X - imageSize.X) * 0.5f),
                    cursor.Y + Math.Max(0f, (size.Y - imageSize.Y) * 0.5f)));
                ImGui.Image(wrap.Handle, imageSize);
                ImGui.EndChild();
                return;
            }

            DrawCommunityCoverPlaceholder(size, "加载中");
            return;
        }

        StartCommunityCoverDownload(pack);
        DrawCommunityCoverPlaceholder(size, "下载封面");
    }

    private static Vector2 CalculateContainedImageSize(Vector2 sourceSize, Vector2 boxSize)
    {
        var width = Math.Max(1f, sourceSize.X);
        var height = Math.Max(1f, sourceSize.Y);
        var scale = Math.Min(boxSize.X / width, boxSize.Y / height);
        return new Vector2(width * scale, height * scale);
    }

    private static void DrawCommunityCoverPlaceholder(Vector2 size, string text)
    {
        ImGui.BeginChild($"##CommunityCoverPlaceholder{text}", size, true);
        ImGui.SetCursorPosY(Math.Max(4f, (size.Y - ImGui.GetTextLineHeight()) * 0.5f));
        ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), text);
        ImGui.EndChild();
    }

    private void StartCommunityCoverDownload(CommunityPackInfo pack)
    {
        if (communityCoverTasks.ContainsKey(pack.Id))
            return;

        communityCoverTasks[pack.Id] = communityPackService.EnsureCoverCachedAsync(pack);
    }

    private void DrawCommunityPackInfo(CommunityPackInfo pack)
    {
        ImGui.TextColored(new Vector4(0.92f, 0.94f, 0.96f, 1f), pack.Name);
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), $"v{pack.Version}");

        ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), $"作者：{pack.Author} / 大小：{FormatCommunityBytes(pack.SizeBytes)}");
        if (!string.IsNullOrWhiteSpace(pack.Description))
            ImGui.TextWrapped(pack.Description);

        if (pack.Tags.Count > 0)
            DrawCommunityTags(pack.Tags);

        var installed = communityPackService.IsInstalled(pack);
        var installing = communityInstallOperations.ContainsKey(pack.Id);
        if (installed)
            ImGui.TextColored(new Vector4(0.48f, 0.90f, 0.62f, 1f), "已安装当前版本");

        if (communityInstallMessages.TryGetValue(pack.Id, out var message) && !string.IsNullOrWhiteSpace(message))
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), message);

        ImGui.BeginDisabled(installing);
        if (ImGui.Button(installed ? "重新安装" : "安装"))
            StartCommunityInstall(pack);
        ImGui.EndDisabled();

        if (!string.IsNullOrWhiteSpace(pack.ReadmeUrl))
        {
            ImGui.SameLine();
            if (ImGui.Button("查看说明"))
                OpenCommunityUrl(pack.ReadmeUrl);
        }
    }

    private static void DrawCommunityTags(IReadOnlyList<string> tags)
    {
        for (var i = 0; i < tags.Count; i++)
        {
            if (i > 0)
                ImGui.SameLine();

            ImGui.TextDisabled($"[{tags[i]}]");
        }
    }

    private void StartCommunityInstall(CommunityPackInfo pack)
    {
        if (communityInstallOperations.ContainsKey(pack.Id))
            return;

        var target = BuildCommunityInstallTarget();
        communityInstallMessages[pack.Id] = "正在下载...";
        communityInstallOperations[pack.Id] = new CommunityInstallOperation(
            pack,
            target,
            communityPackService.DownloadPackageAsync(pack));
    }

    private void InstallDownloadedCommunityPack(CommunityPackInfo pack, string packagePath, CommunityInstallTarget target)
    {
        var importedProfile = sfxPackService.Import(packagePath);
        var targetProfile = profileStorageService.Profiles.FirstOrDefault(profile => profile.Id.Equals(target.ProfileId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("找不到安装目标方案，请重新选择安装位置。");
        var importedRules = importedProfile.EnumerateRules().ToList();
        if (importedRules.Count == 0)
            throw new InvalidOperationException("这个社区包里没有可导入的规则。");

        RuleGroupDefinition targetGroup;
        if (target.CreateGroup)
        {
            targetGroup = new RuleGroupDefinition
            {
                Name = string.IsNullOrWhiteSpace(target.NewGroupName) ? pack.Name : target.NewGroupName.Trim(),
                Rules = []
            };
            targetProfile.Groups.Add(targetGroup);
        }
        else
        {
            targetGroup = targetProfile.Groups.FirstOrDefault(group => group.Id.Equals(target.GroupId, StringComparison.OrdinalIgnoreCase))
                ?? targetProfile.GetOrCreateDefaultGroup();
        }

        targetGroup.Rules.AddRange(importedRules);
        profileStorageService.SaveProfile(targetProfile);
        profileStorageService.SwitchProfile(targetProfile.Id, configuration);

        selectedRuleId = importedRules.FirstOrDefault()?.Id;
        selectedGroupId = targetGroup.Id;
        communityInstallProfileId = targetProfile.Id;
        communityInstallGroupId = targetGroup.Id;
        communityInstallCreateGroup = false;
        communityInstallNewGroupName = string.Empty;
        exportGroupIds.Clear();
        exportRuleIds.Clear();
        communityPackService.MarkInstalled(pack);
        reloadRules();
        communityInstallMessages[pack.Id] = $"已安装到：{targetProfile.Name} / {targetGroup.Name}";
    }

    private void DrawCommunityInstallTargetPanel()
    {
        EnsureCommunityInstallTarget();
        if (!ImGui.CollapsingHeader("安装位置", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextColored(
            new Vector4(0.70f, 0.72f, 0.76f, 1f),
            "第一步：选择安装到哪里。第二步：在下面找到喜欢的音效包，点安装。");

        DrawCommunityInstallProfileCombo();

        var createGroup = communityInstallCreateGroup;
        if (ImGui.Checkbox("安装时新建分组##CommunityInstallCreateGroup", ref createGroup))
            communityInstallCreateGroup = createGroup;

        if (communityInstallCreateGroup)
        {
            DrawInputText("新分组名，留空则使用音效包名##CommunityInstallNewGroup", communityInstallNewGroupName, 120, value => communityInstallNewGroupName = value, 320f);
        }
        else
        {
            DrawCommunityInstallGroupCombo();
        }
    }

    private void DrawCommunityInstallProfileCombo()
    {
        var targetProfile = GetCommunityInstallProfile();
        ImGui.SetNextItemWidth(260f);
        if (!ImGui.BeginCombo("安装到方案##CommunityInstallProfile", targetProfile?.Name ?? "未选择"))
            return;

        foreach (var profile in profileStorageService.Profiles)
        {
            var selected = profile.Id.Equals(communityInstallProfileId, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable($"{profile.Name}##CommunityInstallProfile{profile.Id}", selected))
            {
                communityInstallProfileId = profile.Id;
                communityInstallGroupId = profile.Groups.FirstOrDefault()?.Id ?? string.Empty;
                communityInstallCreateGroup = false;
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private void DrawCommunityInstallGroupCombo()
    {
        var targetProfile = GetCommunityInstallProfile();
        if (targetProfile == null)
            return;

        var targetGroup = targetProfile.Groups.FirstOrDefault(group => group.Id.Equals(communityInstallGroupId, StringComparison.OrdinalIgnoreCase))
            ?? targetProfile.Groups.FirstOrDefault();
        if (targetGroup == null)
            return;

        communityInstallGroupId = targetGroup.Id;
        ImGui.SetNextItemWidth(260f);
        if (!ImGui.BeginCombo("安装到分组##CommunityInstallGroup", targetGroup.Name))
            return;

        foreach (var group in targetProfile.Groups)
        {
            var selected = group.Id.Equals(communityInstallGroupId, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable($"{group.Name}##CommunityInstallGroup{group.Id}", selected))
                communityInstallGroupId = group.Id;

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private CommunityInstallTarget BuildCommunityInstallTarget()
    {
        EnsureCommunityInstallTarget();
        return new CommunityInstallTarget(
            communityInstallProfileId,
            communityInstallGroupId,
            communityInstallCreateGroup,
            communityInstallNewGroupName);
    }

    private void EnsureCommunityInstallTarget()
    {
        var targetProfile = GetCommunityInstallProfile();
        if (targetProfile == null)
        {
            targetProfile = profileStorageService.ActiveProfile;
            communityInstallProfileId = targetProfile.Id;
        }

        if (string.IsNullOrWhiteSpace(communityInstallGroupId)
            || targetProfile.Groups.All(group => !group.Id.Equals(communityInstallGroupId, StringComparison.OrdinalIgnoreCase)))
        {
            communityInstallGroupId = targetProfile.Groups.FirstOrDefault()?.Id ?? string.Empty;
        }
    }

    private ProfileDefinition? GetCommunityInstallProfile()
        => profileStorageService.Profiles.FirstOrDefault(profile => profile.Id.Equals(communityInstallProfileId, StringComparison.OrdinalIgnoreCase))
           ?? profileStorageService.Profiles.FirstOrDefault(profile => profile.Id.Equals(profileStorageService.ActiveProfile.Id, StringComparison.OrdinalIgnoreCase))
           ?? profileStorageService.Profiles.FirstOrDefault();

    private static void OpenCommunityUrl(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        });
    }

    private static string GetTaskExceptionMessage(Task task)
        => task.Exception?.GetBaseException().Message ?? "未知错误";

    private static string FormatCommunityBytes(long value)
    {
        if (value <= 0)
            return "未知";
        if (value >= 1024L * 1024L)
            return $"{value / 1024f / 1024f:0.0} MB";
        if (value >= 1024L)
            return $"{value / 1024f:0.0} KB";

        return $"{value} B";
    }

    private sealed record CommunityInstallTarget(string ProfileId, string GroupId, bool CreateGroup, string NewGroupName);

    private sealed record CommunityInstallOperation(CommunityPackInfo Pack, CommunityInstallTarget Target, Task<string> DownloadTask);
}
