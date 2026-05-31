namespace AllTimeSoundTrigger.EventSources.Payloads;

public sealed class MapChangedPayload
{
    public uint PreviousTerritoryType { get; init; }

    public uint TerritoryType { get; init; }

    public uint PreviousMapId { get; init; }

    public uint MapId { get; init; }
}
