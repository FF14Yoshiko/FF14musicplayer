namespace AllTimeSoundTrigger.EventSources;

public readonly record struct ItemAcquiredParseResult(
    string ActorName,
    string ItemName,
    int Quantity);
