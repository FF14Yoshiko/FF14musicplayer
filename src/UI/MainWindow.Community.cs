using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AllTimeSoundTrigger.Community;
using AllTimeSoundTrigger.ConfigurationModels;
using AllTimeSoundTrigger.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AllTimeSoundTrigger.UI;

public sealed partial class MainWindow
{
    private const string CommunityAllFilter = "全部";
    private const string CommunityInstallConfirmPopupId = "安装前确认##CommunityInstallConfirm";

    private static readonly string[] CommunitySortModes = ["推荐", "热门", "最近更新", "最新发布", "名称", "体积小到大", "体积大到小"];

    private readonly Dictionary<string, Task> communityCoverTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> communityCoverFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ISharedImmediateTexture> communityCoverTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CommunityInstallPreviewOperation> communityInstallPreviewOperations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CommunityInstallPreviewResult> communityInstallPreviewResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> communityInstallPreviewFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CommunityInstallOperation> communityInstallOperations = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> communityInstallFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> communityInstallMessages = new(StringComparer.OrdinalIgnoreCase);
    private readonly object communityCancellationGate = new();
    private CancellationTokenSource communityCancellation = new();
    private Task<CommunityRefreshResult>? communityRefreshTask;
    private Task<CommunityPublishResult>? communityPublishTask;
    private string communitySearchText = string.Empty;
    private string communityCategoryFilter = CommunityAllFilter;
    private string communityGameModeFilter = CommunityAllFilter;
    private string communityJobFilter = CommunityAllFilter;
    private string communityTriggerFilter = CommunityAllFilter;
    private string communitySortMode = "推荐";
    private string communityDetailPackId = string.Empty;
    private string communityPendingInstallPackId = string.Empty;
    private bool communityInstallConfirmOpenRequested;
    private string communityMessage = string.Empty;
    private bool communityRefreshFailed;
    private bool communityShowDeprecated;
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

        if (HasRunningCommunityOperations())
        {
            ImGui.SameLine();
            if (ImGui.Button("取消后台任务"))
                CancelCommunityOperations("已取消社区后台任务。");
        }

        if (CanReviewCommunity())
        {
            ImGui.SameLine();
            if (ImGui.Button("打开社区仓库"))
                OpenCommunityUrl("https://gitee.com/aikyan931023/ffxiv-sfx-community");

            ImGui.SameLine();
            if (ImGui.Button("打开缓存目录"))
                OpenCommunityUrl(communityPackService.CacheDirectory);
        }

        DrawInputText("搜索##CommunitySearch", communitySearchText, 120, value => communitySearchText = value, 260f);
        DrawCommunityFilters(communityPackService.Packs);

        if (communityRefreshTask is { IsCompleted: false })
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "正在刷新社区列表...");
        else if (!string.IsNullOrWhiteSpace(communityMessage))
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), communityMessage);
        DrawCommunityRefreshRecoveryActions();

        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "点击安装后，插件会自动新建一个分组并导入音效包规则。");
        ImGui.Separator();

        var packs = FilterCommunityPacks(communityPackService.Packs).ToArray();
        DrawCommunitySummary(packs);
        if (packs.Length == 0)
        {
            DrawCommunityInstallConfirmationPopup();
            ImGui.Text("当前没有可显示的社区音效包。");
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), CommunityPackService.DefaultIndexUrl);
            return;
        }

        if (!ImGui.BeginTable("##CommunityPackTable", 2, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp))
        {
            DrawCommunityInstallConfirmationPopup();
            return;
        }

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
        DrawCommunityInstallConfirmationPopup();
    }

    private void StartCommunityRefresh()
    {
        if (communityRefreshTask is { IsCompleted: false })
            return;

        communityMessage = "正在连接 Gitee 社区仓库...";
        communityRefreshFailed = false;
        communityRefreshTask = communityPackService.RefreshIndexAsync(GetCommunityCancellationToken());
    }

    private void DrawCommunityRefreshRecoveryActions()
    {
        if (!communityRefreshFailed)
            return;

        if (ImGui.SmallButton("重试刷新##CommunityRetryRefresh"))
            StartCommunityRefresh();

        if (File.Exists(communityPackService.IndexCachePath))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("使用本地缓存##CommunityUseCache"))
                UseCommunityCache();
        }
    }

    private void UseCommunityCache()
    {
        try
        {
            var cached = communityPackService.TryLoadCachedIndex();
            communityRefreshFailed = false;
            communityMessage = cached.PackCount > 0
                ? $"已使用本地缓存：{cached.PackCount} 个音效包。"
                : "本地缓存为空。";
        }
        catch (Exception ex)
        {
            communityMessage = $"读取本地缓存失败：{ex.Message}";
        }
    }

    private bool HasRunningCommunityOperations()
        => communityRefreshTask is { IsCompleted: false }
           || communityCoverTasks.Values.Any(task => !task.IsCompleted)
           || communityInstallPreviewOperations.Values.Any(operation => !operation.PreviewTask.IsCompleted)
           || communityInstallOperations.Values.Any(operation => !operation.DownloadTask.IsCompleted);

    private CancellationToken GetCommunityCancellationToken()
    {
        lock (communityCancellationGate)
        {
            if (communityCancellation.IsCancellationRequested)
            {
                communityCancellation.Dispose();
                communityCancellation = new CancellationTokenSource();
            }

            return communityCancellation.Token;
        }
    }

    private void CancelCommunityOperations(string message)
    {
        lock (communityCancellationGate)
        {
            if (!communityCancellation.IsCancellationRequested)
                communityCancellation.Cancel();
        }

        communityMessage = message;
    }

    private void CompleteCommunityOperations()
    {
        if (communityRefreshTask is { IsCompleted: true } refreshTask)
        {
            communityRefreshTask = null;
            if (refreshTask.IsCanceled)
            {
                communityRefreshFailed = false;
                communityMessage = "已取消刷新社区列表。";
            }
            else if (refreshTask.IsFaulted)
            {
                communityRefreshFailed = true;
                communityMessage = $"刷新失败：{GetTaskExceptionMessage(refreshTask)}";
            }
            else
            {
                var result = refreshTask.Result;
                communityRefreshFailed = false;
                communityMessage = $"已刷新社区列表：{result.PackCount} 个音效包。";
            }
        }

        foreach (var item in communityCoverTasks.ToArray())
        {
            var task = item.Value;
            if (!task.IsCompleted)
                continue;

            communityCoverTasks.Remove(item.Key);
            if (task.IsCanceled)
                continue;
            if (task.IsFaulted)
                communityCoverFailures[item.Key] = GetTaskExceptionMessage(task);
            else
                communityCoverFailures.Remove(item.Key);
        }

        foreach (var item in communityInstallPreviewOperations.ToArray())
        {
            var operation = item.Value;
            if (!operation.PreviewTask.IsCompleted)
                continue;

            communityInstallPreviewOperations.Remove(item.Key);
            if (operation.PreviewTask.IsCanceled)
            {
                communityInstallPreviewFailures[item.Key] = "已取消冲突检测。";
                continue;
            }

            if (operation.PreviewTask.IsFaulted)
            {
                communityInstallPreviewFailures[item.Key] = $"冲突检测失败：{GetTaskExceptionMessage(operation.PreviewTask)}";
                continue;
            }

            var result = operation.PreviewTask.Result;
            if (result.PackVersion.Equals(operation.Pack.Version, StringComparison.OrdinalIgnoreCase))
            {
                communityInstallPreviewFailures.Remove(item.Key);
                communityInstallPreviewResults[item.Key] = result;
            }
        }

        foreach (var item in communityInstallOperations.ToArray())
        {
            var operation = item.Value;
            if (!operation.DownloadTask.IsCompleted)
                continue;

            communityInstallOperations.Remove(item.Key);
            if (operation.DownloadTask.IsCanceled)
            {
                communityInstallFailures.Add(item.Key);
                communityInstallMessages[item.Key] = "已取消下载。";
                continue;
            }

            if (operation.DownloadTask.IsFaulted)
            {
                communityInstallFailures.Add(item.Key);
                communityInstallMessages[item.Key] = $"安装失败：{GetTaskExceptionMessage(operation.DownloadTask)}";
                continue;
            }

            try
            {
                communityInstallFailures.Remove(item.Key);
                communityInstallMessages[item.Key] = "正在导入...";
                InstallDownloadedCommunityPack(operation.Pack, operation.DownloadTask.Result);
            }
            catch (Exception ex)
            {
                communityInstallMessages[item.Key] = $"导入失败：{ex.Message}";
            }
        }
    }

    private void DrawCommunityFilters(IReadOnlyList<CommunityPackInfo> allPacks)
    {
        var optionPacks = allPacks
            .Where(IsCommunityPackVisibleBeforeFilters)
            .ToArray();

        ImGui.SetNextItemWidth(128f);
        DrawCommunityFilterCombo(
            "分类##CommunityCategory",
            communityCategoryFilter,
            BuildCommunityFilterOptions(optionPacks.Select(pack => pack.Category)),
            value => communityCategoryFilter = value);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(128f);
        DrawCommunityFilterCombo(
            "玩法##CommunityGameMode",
            communityGameModeFilter,
            BuildCommunityFilterOptions(optionPacks.SelectMany(pack => pack.GameModes)),
            value => communityGameModeFilter = value);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(128f);
        DrawCommunityFilterCombo(
            "职业##CommunityJob",
            communityJobFilter,
            BuildCommunityFilterOptions(optionPacks.SelectMany(pack => pack.Jobs)),
            value => communityJobFilter = value);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(138f);
        DrawCommunityFilterCombo(
            "触发器##CommunityTrigger",
            communityTriggerFilter,
            BuildCommunityFilterOptions(optionPacks.SelectMany(pack => pack.TriggerTypes)),
            value => communityTriggerFilter = value);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(128f);
        DrawCommunityFilterCombo(
            "排序##CommunitySort",
            communitySortMode,
            CommunitySortModes,
            value => communitySortMode = value);

        if (allPacks.Any(pack => pack.Deprecated || pack.Hidden))
        {
            ImGui.SameLine();
            if (ImGui.Checkbox(CanReviewCommunity() ? "显示隐藏/弃用##CommunityShowDeprecated" : "显示已弃用##CommunityShowDeprecated", ref communityShowDeprecated))
                communityMessage = communityShowDeprecated ? "已显示维护中的音效包。" : "已隐藏维护中的音效包。";
        }
    }

    private void DrawCommunitySummary(IReadOnlyList<CommunityPackInfo> visiblePacks)
    {
        var updateCount = visiblePacks.Count(pack =>
        {
            var installed = communityPackService.GetInstalledPack(pack);
            return installed != null && !installed.Version.Equals(pack.Version, StringComparison.OrdinalIgnoreCase);
        });
        var installedCount = visiblePacks.Count(pack => communityPackService.GetInstalledPack(pack) != null);
        var deprecatedCount = visiblePacks.Count(pack => pack.Deprecated || pack.Hidden);
        var totalDownloads = visiblePacks.Sum(pack => Math.Max(0, pack.DownloadCount));

        ImGui.TextColored(
            new Vector4(0.70f, 0.72f, 0.76f, 1f),
            $"显示 {visiblePacks.Count} 个音效包 / 已安装 {installedCount} 个 / 可更新 {updateCount} 个 / 下载 {FormatCommunityCount(totalDownloads)} 次");
        if (deprecatedCount > 0)
            ImGui.TextColored(new Vector4(1f, 0.78f, 0.30f, 1f), $"其中 {deprecatedCount} 个处于隐藏或弃用状态，请先查看详情再安装。");
    }

    private IEnumerable<CommunityPackInfo> FilterCommunityPacks(IEnumerable<CommunityPackInfo> packs)
    {
        var keyword = (communitySearchText ?? string.Empty).Trim();
        var filtered = packs
            .Where(IsCommunityPackVisibleBeforeFilters)
            .Where(pack => MatchesCommunityFilter(communityCategoryFilter, pack.Category))
            .Where(pack => MatchesCommunityFilter(communityGameModeFilter, pack.GameModes))
            .Where(pack => MatchesCommunityFilter(communityJobFilter, pack.Jobs))
            .Where(pack => MatchesCommunityFilter(communityTriggerFilter, pack.TriggerTypes));

        if (keyword.Length > 0)
            filtered = filtered.Where(pack => MatchesCommunityKeyword(pack, keyword));

        return SortCommunityPacks(filtered);
    }

    private bool IsCommunityPackVisibleBeforeFilters(CommunityPackInfo pack)
    {
        var installed = communityPackService.GetInstalledPack(pack) != null;
        if (pack.Hidden && !CanReviewCommunity() && !installed)
            return false;
        if ((pack.Deprecated || pack.Hidden) && !communityShowDeprecated && !CanReviewCommunity() && !installed)
            return false;

        return true;
    }

    private IEnumerable<CommunityPackInfo> SortCommunityPacks(IEnumerable<CommunityPackInfo> packs)
    {
        var list = packs.ToList();
        return communitySortMode switch
        {
            "最近更新" => list
                .OrderByDescending(pack => GetCommunityDateScore(pack.UpdatedAt))
                .ThenBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase),
            "最新发布" => list
                .OrderByDescending(pack => GetCommunityDateScore(pack.CreatedAt))
                .ThenBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase),
            "热门" => list
                .OrderByDescending(pack => pack.DownloadCount)
                .ThenByDescending(pack => GetCommunityDateScore(pack.UpdatedAt))
                .ThenBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase),
            "名称" => list.OrderBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase),
            "体积小到大" => list
                .OrderBy(pack => pack.SizeBytes <= 0 ? long.MaxValue : pack.SizeBytes)
                .ThenBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase),
            "体积大到小" => list
                .OrderByDescending(pack => pack.SizeBytes)
                .ThenBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => list
                .OrderByDescending(GetCommunityRecommendedScore)
                .ThenByDescending(pack => pack.DownloadCount)
                .ThenByDescending(pack => GetCommunityDateScore(pack.UpdatedAt))
                .ThenBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase)
        };
    }

    private int GetCommunityRecommendedScore(CommunityPackInfo pack)
    {
        var installed = communityPackService.GetInstalledPack(pack);
        if (installed != null && !installed.Version.Equals(pack.Version, StringComparison.OrdinalIgnoreCase))
            return 40;
        if (installed != null)
            return 30;
        if (pack.Hidden)
            return 0;
        if (pack.Deprecated)
            return 5;
        if (!string.IsNullOrWhiteSpace(pack.ContentWarning))
            return 10;

        return 20;
    }

    private static bool MatchesCommunityKeyword(CommunityPackInfo pack, string keyword)
        => Contains(pack.Name, keyword)
           || Contains(pack.Author, keyword)
           || Contains(pack.Description, keyword)
           || Contains(pack.Category, keyword)
           || Contains(pack.CompatiblePluginVersion, keyword)
           || Contains(pack.License, keyword)
           || Contains(pack.ContentWarning, keyword)
           || Contains(pack.Changelog, keyword)
           || pack.Tags.Any(tag => Contains(tag, keyword))
           || pack.GameModes.Any(item => Contains(item, keyword))
           || pack.Jobs.Any(item => Contains(item, keyword))
           || pack.TriggerTypes.Any(item => Contains(item, keyword));

    private static bool MatchesCommunityFilter(string selected, string value)
        => IsCommunityAllFilter(selected) || value.Equals(selected, StringComparison.CurrentCultureIgnoreCase);

    private static bool MatchesCommunityFilter(string selected, IReadOnlyList<string> values)
        => IsCommunityAllFilter(selected) || values.Any(value => value.Equals(selected, StringComparison.CurrentCultureIgnoreCase));

    private static bool IsCommunityAllFilter(string value)
        => string.IsNullOrWhiteSpace(value) || value.Equals(CommunityAllFilter, StringComparison.CurrentCultureIgnoreCase);

    private static bool Contains(string value, string keyword)
        => (value ?? string.Empty).Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    private static IReadOnlyList<string> BuildCommunityFilterOptions(IEnumerable<string> values)
        => new[] { CommunityAllFilter }
            .Concat(values.Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();

    private static void DrawCommunityFilterCombo(string label, string currentValue, IReadOnlyList<string> options, Action<string> setValue)
    {
        var current = options.Any(option => option.Equals(currentValue, StringComparison.CurrentCultureIgnoreCase))
            ? currentValue
            : CommunityAllFilter;
        if (!current.Equals(currentValue, StringComparison.CurrentCultureIgnoreCase))
            setValue(current);

        if (!ImGui.BeginCombo(label, current))
            return;

        foreach (var option in options)
        {
            var selected = option.Equals(current, StringComparison.CurrentCultureIgnoreCase);
            if (ImGui.Selectable($"{option}##{label}{option}", selected))
                setValue(option);

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private static long GetCommunityDateScore(string value)
        => TryParseCommunityDate(value, out var date)
            ? date.ToUnixTimeSeconds()
            : 0;

    private static bool TryParseCommunityDate(string value, out DateTimeOffset date)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
            out date);

    private void DrawCommunityCover(CommunityPackInfo pack)
    {
        var size = new Vector2(150f, 84f);
        if (string.IsNullOrWhiteSpace(pack.CoverUrl))
        {
            DrawCommunityCoverPlaceholder(size, "暂无封面");
            return;
        }

        if (communityCoverFailures.TryGetValue(pack.Id, out _))
        {
            DrawCommunityCoverPlaceholder(size, "封面失败");
            if (ImGui.SmallButton("重试封面"))
            {
                communityCoverFailures.Remove(pack.Id);
                StartCommunityCoverDownload(pack);
            }

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

        communityCoverTasks[pack.Id] = communityPackService.EnsureCoverCachedAsync(pack, GetCommunityCancellationToken());
    }

    private void DrawCommunityPackInfo(CommunityPackInfo pack)
    {
        ImGui.TextColored(new Vector4(0.92f, 0.94f, 0.96f, 1f), pack.Name);
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), $"v{pack.Version}");

        ImGui.TextColored(
            new Vector4(0.70f, 0.72f, 0.76f, 1f),
            $"作者：{pack.Author} / 大小：{FormatCommunityBytes(pack.SizeBytes)} / 下载：{FormatCommunityCount(pack.DownloadCount)} 次");
        if (!string.IsNullOrWhiteSpace(pack.Description))
            ImGui.TextWrapped(pack.Description);

        DrawCommunityPackBadges(pack);
        DrawCommunityPackWarnings(pack);

        var installedRecord = communityPackService.GetInstalledPack(pack);
        var installed = installedRecord != null;
        var currentVersionInstalled = installedRecord != null
            && installedRecord.Version.Equals(pack.Version, StringComparison.OrdinalIgnoreCase);
        var installing = communityInstallOperations.ContainsKey(pack.Id);
        if (currentVersionInstalled)
            ImGui.TextColored(new Vector4(0.48f, 0.90f, 0.62f, 1f), "已安装当前版本");
        else if (installedRecord != null)
            ImGui.TextColored(new Vector4(1f, 0.78f, 0.30f, 1f), $"已安装 v{installedRecord.Version}，可更新到 v{pack.Version}");
        if (installedRecord is { GroupIds.Count: 0 })
            ImGui.TextColored(new Vector4(1f, 0.78f, 0.30f, 1f), "这是旧安装记录，重新安装后可启用完整管理。");

        if (communityInstallMessages.TryGetValue(pack.Id, out var message) && !string.IsNullOrWhiteSpace(message))
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), message);
        if (communityInstallFailures.Contains(pack.Id))
        {
            if (ImGui.SmallButton("重试下载"))
                StartCommunityInstall(pack);
        }

        var showingDetails = communityDetailPackId.Equals(pack.Id, StringComparison.OrdinalIgnoreCase);
        if (ImGui.Button(showingDetails ? "收起详情" : "详情"))
            communityDetailPackId = showingDetails ? string.Empty : pack.Id;

        ImGui.SameLine();
        ImGui.BeginDisabled(installing);
        var installLabel = GetCommunityInstallActionLabel(pack, installedRecord);
        if (ImGui.Button(installLabel))
            OpenCommunityInstallConfirmation(pack);
        ImGui.EndDisabled();

        if (installedRecord != null)
            DrawInstalledCommunityPackActions(pack, installedRecord);

        if (!string.IsNullOrWhiteSpace(pack.ReadmeUrl))
        {
            ImGui.SameLine();
            if (ImGui.Button("查看说明"))
                OpenCommunityUrl(pack.ReadmeUrl);
        }

        if (showingDetails)
            DrawCommunityPackDetails(pack);
    }

    private static void DrawCommunityPackBadges(CommunityPackInfo pack)
    {
        var badges = new List<string>();
        if (!string.IsNullOrWhiteSpace(pack.Category))
            badges.Add(pack.Category);
        badges.AddRange(pack.GameModes);
        badges.AddRange(pack.Jobs);
        badges.AddRange(pack.TriggerTypes);
        badges.AddRange(pack.Tags);

        DrawCommunityTags(badges
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(12)
            .ToArray());
    }

    private static void DrawCommunityPackWarnings(CommunityPackInfo pack)
    {
        if (pack.Hidden)
            ImGui.TextColored(new Vector4(1f, 0.58f, 0.30f, 1f), "这个包已隐藏，只建议审核或已安装用户处理。");
        if (pack.Deprecated)
            ImGui.TextColored(new Vector4(1f, 0.78f, 0.30f, 1f), "这个包已弃用，建议优先选择其他音效包。");
        if (!string.IsNullOrWhiteSpace(pack.ContentWarning))
            ImGui.TextColored(new Vector4(1f, 0.78f, 0.30f, 1f), $"内容提醒：{pack.ContentWarning}");
        if (!string.IsNullOrWhiteSpace(pack.CompatiblePluginVersion))
            ImGui.TextDisabled($"兼容插件：{pack.CompatiblePluginVersion}");
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

    private void DrawCommunityPackDetails(CommunityPackInfo pack)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.30f, 0.78f, 1f, 1f), "包详情");
        DrawCommunityDetailLine("包 ID", pack.Id);
        DrawCommunityDetailLine("内容数量", FormatCommunityPackContentCounts(pack));
        DrawCommunityDetailLine("分类", pack.Category);
        DrawCommunityDetailLine("玩法", FormatCommunityList(pack.GameModes));
        DrawCommunityDetailLine("职业", FormatCommunityList(pack.Jobs));
        DrawCommunityDetailLine("触发器", FormatCommunityList(pack.TriggerTypes));
        DrawCommunityDetailLine("标签", FormatCommunityList(pack.Tags));
        DrawCommunityDetailLine("兼容插件", pack.CompatiblePluginVersion);
        DrawCommunityDetailLine("创建时间", FormatCommunityDate(pack.CreatedAt));
        DrawCommunityDetailLine("更新时间", FormatCommunityDate(pack.UpdatedAt));
        DrawCommunityDetailLine("许可证", pack.License);
        DrawCommunityDetailLine("内容提醒", pack.ContentWarning);
        DrawCommunityDetailLine("下载量", $"{FormatCommunityCount(pack.DownloadCount)} 次");
        DrawCommunityDetailLine("更新日志", FormatCommunityChangelog(pack));
        DrawCommunityDetailLine("包校验", ShortCommunityHash(pack.Sha256));
        DrawCommunityDetailLine("封面校验", ShortCommunityHash(pack.CoverSha256));
        DrawCommunityDetailLine("源下载", pack.SourcePackageUrl);

        if (!string.IsNullOrWhiteSpace(pack.PackageUrl))
        {
            if (ImGui.SmallButton("复制下载链接"))
                ImGui.SetClipboardText(pack.PackageUrl);
        }

        if (!string.IsNullOrWhiteSpace(pack.ReadmeUrl))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("打开 README"))
                OpenCommunityUrl(pack.ReadmeUrl);
        }

        var releaseNotesUrl = GetCommunityReleaseNotesUrl(pack);
        if (!string.IsNullOrWhiteSpace(releaseNotesUrl))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("打开更新日志"))
                OpenCommunityUrl(releaseNotesUrl);
        }

        ImGui.Separator();
    }

    private void OpenCommunityInstallConfirmation(CommunityPackInfo pack)
    {
        communityPendingInstallPackId = pack.Id;
        communityInstallConfirmOpenRequested = true;
    }

    private void StartCommunityInstallPreview(CommunityPackInfo pack)
    {
        if (TryGetCommunityInstallPreview(pack, out _))
            return;
        if (communityInstallPreviewOperations.TryGetValue(pack.Id, out var operation)
            && operation.Pack.Version.Equals(pack.Version, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        communityInstallPreviewResults.Remove(pack.Id);
        communityInstallPreviewFailures.Remove(pack.Id);
        communityInstallPreviewOperations[pack.Id] = new CommunityInstallPreviewOperation(
            pack,
            BuildCommunityInstallPreviewAsync(pack, GetCommunityCancellationToken()));
    }

    private async Task<CommunityInstallPreviewResult> BuildCommunityInstallPreviewAsync(
        CommunityPackInfo pack,
        CancellationToken cancellationToken)
    {
        var packagePath = await communityPackService.DownloadPackageAsync(pack, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var preview = sfxPackService.PreviewWithProfile(packagePath);
        return new CommunityInstallPreviewResult(pack.Id, pack.Version, packagePath, preview.Summary, preview.Profile);
    }

    private bool TryGetCommunityInstallPreview(CommunityPackInfo pack, out CommunityInstallPreviewResult? result)
    {
        if (communityInstallPreviewResults.TryGetValue(pack.Id, out result)
            && result.PackVersion.Equals(pack.Version, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        result = null;
        return false;
    }

    private void DrawCommunityInstallConfirmationPopup()
    {
        if (string.IsNullOrWhiteSpace(communityPendingInstallPackId))
            return;

        var pack = communityPackService.Packs.FirstOrDefault(item =>
            item.Id.Equals(communityPendingInstallPackId, StringComparison.OrdinalIgnoreCase));
        if (pack == null)
        {
            communityPendingInstallPackId = string.Empty;
            communityInstallConfirmOpenRequested = false;
            return;
        }

        StartCommunityInstallPreview(pack);

        if (communityInstallConfirmOpenRequested)
        {
            ImGui.OpenPopup(CommunityInstallConfirmPopupId);
            communityInstallConfirmOpenRequested = false;
        }

        var open = true;
        ImGui.SetNextWindowSize(new Vector2(560f, 0f), ImGuiCond.Appearing);
        if (ImGui.BeginPopupModal(CommunityInstallConfirmPopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            DrawCommunityInstallConfirmation(pack, communityPackService.GetInstalledPack(pack));
            ImGui.EndPopup();
        }

        if (!open)
            communityPendingInstallPackId = string.Empty;
    }

    private void DrawCommunityInstallConfirmation(CommunityPackInfo pack, CommunityInstalledPack? installedRecord)
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.30f, 0.78f, 1f, 1f), "安装前确认");
        DrawCommunityDetailLine("音效包", $"{pack.Name} v{pack.Version}");
        DrawCommunityDetailLine("作者", pack.Author);
        DrawCommunityDetailLine("内容数量", FormatCommunityPackContentCounts(pack));
        DrawCommunityDetailLine("大小", FormatCommunityBytes(pack.SizeBytes));
        DrawCommunityDetailLine("许可证", string.IsNullOrWhiteSpace(pack.License) ? "未填写" : pack.License);
        DrawCommunityDetailLine("内容提醒", string.IsNullOrWhiteSpace(pack.ContentWarning) ? "无" : pack.ContentWarning);
        DrawCommunityDetailLine("更新日志", FormatCommunityChangelog(pack));

        if (installedRecord != null && !installedRecord.Version.Equals(pack.Version, StringComparison.OrdinalIgnoreCase))
            ImGui.TextColored(new Vector4(1f, 0.78f, 0.30f, 1f), $"将从 v{installedRecord.Version} 更新到 v{pack.Version}。新包导入成功后才会替换旧版本。");
        else if (installedRecord != null)
            ImGui.TextColored(new Vector4(1f, 0.78f, 0.30f, 1f), "将重新安装当前版本。成功后会替换原来的分组和导入音效。");

        if (pack.Deprecated || pack.Hidden)
            ImGui.TextColored(new Vector4(1f, 0.58f, 0.30f, 1f), "这个包处于隐藏或弃用状态，请确认你确实需要安装。");

        ImGui.Separator();
        var previewReady = DrawCommunityInstallConflictStatus(pack);

        ImGui.Separator();
        var installing = communityInstallOperations.ContainsKey(pack.Id);
        ImGui.BeginDisabled(installing || !previewReady);
        if (ImGui.Button($"确认{GetCommunityInstallActionLabel(pack, installedRecord)}"))
        {
            communityPendingInstallPackId = string.Empty;
            StartCommunityInstall(pack);
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("取消"))
        {
            communityPendingInstallPackId = string.Empty;
            ImGui.CloseCurrentPopup();
        }
    }

    private bool DrawCommunityInstallConflictStatus(CommunityPackInfo pack)
    {
        ImGui.TextColored(new Vector4(0.30f, 0.78f, 1f, 1f), "安装前冲突检测");

        if (communityInstallPreviewOperations.ContainsKey(pack.Id))
        {
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "正在下载并检测触发冲突...");
            return false;
        }

        if (communityInstallPreviewFailures.TryGetValue(pack.Id, out var failure))
        {
            ImGui.TextColored(new Vector4(1f, 0.58f, 0.30f, 1f), failure);
            if (ImGui.SmallButton("重试检测"))
                StartCommunityInstallPreview(pack);
            return false;
        }

        if (!TryGetCommunityInstallPreview(pack, out var preview) || preview == null)
        {
            StartCommunityInstallPreview(pack);
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), "正在准备冲突检测...");
            return false;
        }

        var report = RuleTriggerConflictDetector.Detect(
            preview.Profile,
            profileStorageService.ActiveProfile,
            communityPackService.InstalledPacks,
            pack.Id,
            6);
        if (!report.HasConflicts)
        {
            ImGui.TextColored(new Vector4(0.48f, 0.90f, 0.62f, 1f), "未发现和当前启用规则的明显触发冲突。");
            return true;
        }

        var owners = FormatCommunityConflictOwners(report);
        ImGui.TextColored(
            new Vector4(1f, 0.78f, 0.30f, 1f),
            $"这个包可能和{owners}同时触发。");
        foreach (var conflict in report.Conflicts)
        {
            ImGui.TextWrapped(
                $"- {conflict.Reason}：{conflict.TriggerDescription} / 新规则「{conflict.IncomingRuleName}」 ↔ {FormatCommunityConflictOwner(conflict)} 的规则「{conflict.ExistingRuleName}」");
        }

        if (report.HasMoreConflicts)
            ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), $"还有 {report.TotalConflictCount - report.Conflicts.Count} 条类似冲突未展开。");

        return true;
    }

    private static string FormatCommunityConflictOwners(RuleTriggerConflictReport report)
    {
        var owners = report.Conflicts
            .Select(FormatCommunityConflictOwner)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(3)
            .ToArray();
        return owners.Length == 0 ? "已有规则" : string.Join("、", owners);
    }

    private static string FormatCommunityConflictOwner(RuleTriggerConflict conflict)
        => conflict.ExistingSourceIsCommunityPack
            ? $"「{conflict.ExistingSourceName}」包"
            : $"分组「{conflict.ExistingSourceName}」";

    private static string GetCommunityInstallActionLabel(CommunityPackInfo pack, CommunityInstalledPack? installedRecord)
    {
        if (installedRecord == null)
            return "安装";

        return installedRecord.Version.Equals(pack.Version, StringComparison.OrdinalIgnoreCase)
            ? "重新安装"
            : "更新";
    }

    private static void DrawCommunityDetailLine(string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        ImGui.TextColored(new Vector4(0.70f, 0.72f, 0.76f, 1f), $"{label}：");
        ImGui.SameLine();
        ImGui.TextWrapped(value);
    }

    private static string FormatCommunityList(IReadOnlyList<string> values)
        => values.Count == 0 ? string.Empty : string.Join("、", values);

    private static string FormatCommunityPackContentCounts(CommunityPackInfo pack)
    {
        var groups = pack.GroupCount > 0 ? pack.GroupCount.ToString() : "未知";
        var rules = pack.RuleCount > 0 ? pack.RuleCount.ToString() : "未知";
        var sounds = pack.SoundCount > 0 ? pack.SoundCount.ToString() : "未知";
        return $"分组 {groups} / 规则 {rules} / 音效 {sounds}";
    }

    private static string FormatCommunityChangelog(CommunityPackInfo pack)
    {
        if (!string.IsNullOrWhiteSpace(pack.Changelog))
            return pack.Changelog;
        var releaseNotesUrl = GetCommunityReleaseNotesUrl(pack);
        if (!string.IsNullOrWhiteSpace(releaseNotesUrl))
            return releaseNotesUrl;

        return "暂无";
    }

    private static string GetCommunityReleaseNotesUrl(CommunityPackInfo pack)
    {
        if (!string.IsNullOrWhiteSpace(pack.ReleaseNotesUrl))
            return pack.ReleaseNotesUrl;
        if (!string.IsNullOrWhiteSpace(pack.ChangelogUrl))
            return pack.ChangelogUrl;

        return string.Empty;
    }

    private static string FormatCommunityDate(string value)
        => TryParseCommunityDate(value, out var date)
            ? date.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : value;

    private static string ShortCommunityHash(string value)
    {
        var hash = (value ?? string.Empty).Trim();
        return hash.Length <= 16 ? hash : $"{hash[..12]}...{hash[^8..]}";
    }

    private void StartCommunityInstall(CommunityPackInfo pack)
    {
        if (communityInstallOperations.ContainsKey(pack.Id))
            return;

        communityInstallFailures.Remove(pack.Id);
        communityInstallMessages[pack.Id] = "正在下载...";
        var packageTask = TryGetCommunityInstallPreview(pack, out var preview) && preview != null && File.Exists(preview.PackagePath)
            ? Task.FromResult(preview.PackagePath)
            : communityPackService.DownloadPackageAsync(pack, GetCommunityCancellationToken());
        communityInstallOperations[pack.Id] = new CommunityInstallOperation(
            pack,
            packageTask);
    }

    private void InstallDownloadedCommunityPack(CommunityPackInfo pack, string packagePath)
    {
        var importResult = sfxPackService.ImportWithDetails(packagePath, configuration.SoundLibrary);
        var importedProfile = importResult.Profile;
        var importedRules = importedProfile.EnumerateRules().ToList();
        if (importedRules.Count == 0)
        {
            CleanupFailedCommunityImport(importResult);
            throw new InvalidOperationException("这个社区包里没有可导入的规则。");
        }

        var existing = communityPackService.GetInstalledPack(pack);
        if (existing != null)
            UninstallCommunityPack(pack, existing, true);

        configuration.Save();
        var targetProfile = profileStorageService.ActiveProfile;

        var targetGroup = new RuleGroupDefinition
        {
            Name = pack.Name,
            Rules = []
        };
        targetProfile.Groups.Add(targetGroup);

        targetGroup.Rules.AddRange(importedRules);
        profileStorageService.SaveProfile(targetProfile);

        selectedRuleId = importedRules.FirstOrDefault()?.Id;
        selectedGroupId = targetGroup.Id;
        exportGroupIds.Clear();
        exportRuleIds.Clear();
        communityPackService.MarkInstalled(pack, [targetGroup.Id], importResult.ImportedSoundIds, importResult.ImportDirectories);
        reloadRules();
        communityInstallMessages[pack.Id] = $"已安装为新分组：{targetGroup.Name}";
    }

    private void CleanupFailedCommunityImport(SfxPackImportResult importResult)
    {
        foreach (var soundId in importResult.ImportedSoundIds)
        {
            var entry = configuration.SoundLibrary.FindById(soundId);
            if (entry != null)
                configuration.SoundLibrary.Entries.Remove(entry);
        }

        foreach (var directory in importResult.ImportDirectories)
        {
            try
            {
                sfxPackService.TryDeleteImportedDirectory(directory);
            }
            catch
            {
                // Best-effort cleanup for a rejected package.
            }
        }

        configuration.Save();
    }

    private void DrawInstalledCommunityPackActions(CommunityPackInfo pack, CommunityInstalledPack installed)
    {
        ImGui.SameLine();
        if (ImGui.Button("查看"))
            JumpToInstalledCommunityPack(installed);

        ImGui.SameLine();
        var enabled = AreInstalledCommunityPackGroupsEnabled(installed);
        if (ImGui.Button(enabled ? "停用" : "启用"))
            SetInstalledCommunityPackEnabled(installed, !enabled);

        ImGui.SameLine();
        if (ImGui.Button("卸载"))
            UninstallCommunityPack(pack, installed, false);
    }

    private bool AreInstalledCommunityPackGroupsEnabled(CommunityInstalledPack installed)
    {
        var groupIds = installed.GroupIds.ToHashSet(StringComparer.Ordinal);
        return profileStorageService.ActiveProfile.Groups
            .Any(group => groupIds.Contains(group.Id) && group.Enabled);
    }

    private void SetInstalledCommunityPackEnabled(CommunityInstalledPack installed, bool enabled)
    {
        var groupIds = installed.GroupIds.ToHashSet(StringComparer.Ordinal);
        var changed = 0;
        foreach (var group in profileStorageService.ActiveProfile.Groups.Where(group => groupIds.Contains(group.Id)))
        {
            if (group.Enabled == enabled)
                continue;

            group.Enabled = enabled;
            changed++;
        }

        if (changed == 0)
        {
            communityInstallMessages[installed.Id] = "没有找到这个音效包对应的分组。";
            return;
        }

        profileStorageService.SaveActiveProfile();
        reloadRules();
        communityInstallMessages[installed.Id] = enabled ? "已启用这个音效包。" : "已停用这个音效包。";
    }

    private void JumpToInstalledCommunityPack(CommunityInstalledPack installed)
    {
        var groupIds = installed.GroupIds.ToHashSet(StringComparer.Ordinal);
        var group = profileStorageService.ActiveProfile.Groups.FirstOrDefault(item => groupIds.Contains(item.Id));
        if (group == null)
        {
            communityInstallMessages[installed.Id] = "没有找到这个音效包对应的分组。";
            return;
        }

        selectedGroupId = group.Id;
        selectedRuleId = group.Rules.FirstOrDefault()?.Id;
        RequestTab("mine");
    }

    private void UninstallCommunityPack(CommunityPackInfo pack, CommunityInstalledPack installed, bool silent)
    {
        var profile = profileStorageService.ActiveProfile;
        var groupIds = installed.GroupIds.ToHashSet(StringComparer.Ordinal);
        var groups = profile.Groups
            .Where(group => groupIds.Contains(group.Id))
            .ToList();
        var removedRules = groups.Sum(group => group.Rules.Count);

        foreach (var group in groups)
        {
            exportGroupIds.Remove(group.Id);
            foreach (var rule in group.Rules)
                exportRuleIds.Remove(rule.Id);
            profile.Groups.Remove(group);
        }

        if (profile.Groups.Count == 0)
            profile.Groups.Add(new RuleGroupDefinition { Name = "默认分组", Rules = [] });

        var remainingSoundIds = CollectRuleSoundIds(profile.EnumerateRules());
        var removedSoundEntries = 0;
        foreach (var soundId in installed.SoundIds.Where(soundId => !remainingSoundIds.Contains(soundId)))
        {
            var entry = configuration.SoundLibrary.FindById(soundId);
            if (entry == null)
                continue;

            configuration.SoundLibrary.Entries.Remove(entry);
            removedSoundEntries++;
        }

        var removedDirectories = 0;
        foreach (var directory in installed.ImportDirectories)
        {
            try
            {
                if (sfxPackService.TryDeleteImportedDirectory(directory))
                    removedDirectories++;
            }
            catch (Exception ex)
            {
                communityInstallMessages[pack.Id] = $"卸载时清理目录失败：{ex.Message}";
            }
        }

        if (selectedGroupId != null && groupIds.Contains(selectedGroupId))
            selectedGroupId = profile.Groups.FirstOrDefault()?.Id;
        selectedRuleId = profile.EnumerateRules().FirstOrDefault()?.Id;

        profileStorageService.SaveActiveProfile();
        configuration.Save();
        communityPackService.UnmarkInstalled(pack.Id);
        reloadRules();

        if (!silent)
            communityInstallMessages[pack.Id] = $"已卸载：删除分组 {groups.Count} 个、规则 {removedRules} 条、音效 {removedSoundEntries} 个、导入目录 {removedDirectories} 个。";
    }

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

    private static string FormatCommunityCount(long value)
    {
        if (value >= 10000)
            return $"{value / 10000d:0.0}万";
        return Math.Max(0, value).ToString();
    }

    private sealed record CommunityInstallPreviewOperation(CommunityPackInfo Pack, Task<CommunityInstallPreviewResult> PreviewTask);

    private sealed record CommunityInstallPreviewResult(
        string PackId,
        string PackVersion,
        string PackagePath,
        SfxPackPreview Summary,
        ProfileDefinition Profile);

    private sealed record CommunityInstallOperation(CommunityPackInfo Pack, Task<string> DownloadTask);
}
