namespace AllTimeSoundTrigger.EventSources.Payloads;

public sealed class ItemAcquiredPayload
{
    public string ActorName { get; init; } = string.Empty;

    public string ItemName { get; init; } = string.Empty;

    public int Quantity { get; init; } = 1;

    public bool IsLocalPlayer { get; init; }

    public string RawMessage { get; init; } = string.Empty;

    public string ChatType { get; init; } = string.Empty;
}
