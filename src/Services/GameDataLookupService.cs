using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using ActionSheet = Lumina.Excel.Sheets.Action;
using TerritoryTypeSheet = Lumina.Excel.Sheets.TerritoryType;

namespace AllTimeSoundTrigger.Services;

public sealed class GameDataLookupService
{
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;
    private IReadOnlyList<GameDataOption>? actionOptions;
    private IReadOnlyList<MapDataOption>? mapOptions;

    public GameDataLookupService(IDataManager dataManager, IPluginLog log)
    {
        this.dataManager = dataManager;
        this.log = log;
    }

    public IReadOnlyList<GameDataOption> SearchActions(string query, int limit = 20)
    {
        var normalized = (query ?? string.Empty).Trim();
        if (normalized.Length == 0)
            return [];

        return GetActionOptions()
            .Where(option => option.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToArray();
    }

    public IReadOnlyList<MapDataOption> SearchMaps(string query, int limit = 30)
    {
        var normalized = (query ?? string.Empty).Trim();
        return GetMapOptions()
            .Where(option => normalized.Length == 0 || option.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToArray();
    }

    public MapDataOption? FindMap(uint territoryType, uint mapId)
        => GetMapOptions().FirstOrDefault(option =>
            (territoryType == 0 || option.TerritoryType == territoryType)
            && (mapId == 0 || option.MapId == mapId));

    private IReadOnlyList<GameDataOption> GetActionOptions()
    {
        if (actionOptions != null)
            return actionOptions;

        var results = new List<GameDataOption>();
        try
        {
            var sheet = dataManager.GetExcelSheet<ActionSheet>();
            foreach (var action in sheet)
            {
                var name = action.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                results.Add(new GameDataOption(action.RowId, name));
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[AllTimeSoundTrigger] 读取技能列表失败。");
        }

        actionOptions = results
            .GroupBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(option => option.Name, StringComparer.Ordinal)
            .ToArray();
        return actionOptions;
    }

    private IReadOnlyList<MapDataOption> GetMapOptions()
    {
        if (mapOptions != null)
            return mapOptions;

        var results = new List<MapDataOption>();
        try
        {
            var sheet = dataManager.GetExcelSheet<TerritoryTypeSheet>();
            foreach (var territory in sheet)
            {
                var name = territory.PlaceName.Value.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name) || territory.Map.RowId == 0)
                    continue;

                results.Add(new MapDataOption(territory.RowId, territory.Map.RowId, name));
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[AllTimeSoundTrigger] 读取地图列表失败。");
        }

        mapOptions = results
            .GroupBy(option => (option.TerritoryType, option.MapId))
            .Select(group => group.First())
            .OrderBy(option => option.Name, StringComparer.Ordinal)
            .ToArray();
        return mapOptions;
    }
}

public sealed record GameDataOption(uint Id, string Name);

public sealed record MapDataOption(uint TerritoryType, uint MapId, string Name);
