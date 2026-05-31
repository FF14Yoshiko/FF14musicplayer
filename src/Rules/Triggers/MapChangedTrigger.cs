using System;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class MapChangedTrigger : ITrigger
{
    private readonly uint territoryType;
    private readonly uint mapId;

    public MapChangedTrigger(int territoryType, int mapId)
    {
        this.territoryType = territoryType > 0 ? (uint)territoryType : 0;
        this.mapId = mapId > 0 ? (uint)mapId : 0;
    }

    public bool IsMatch(GameEvent e)
    {
        if (!e.EventType.Equals("MapChanged", StringComparison.OrdinalIgnoreCase))
            return false;

        if (e.Payload is not MapChangedPayload payload)
            return false;

        if (territoryType > 0 && payload.TerritoryType != territoryType)
            return false;

        return mapId == 0 || payload.MapId == mapId;
    }
}
