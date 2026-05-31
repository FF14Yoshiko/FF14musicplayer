namespace AllTimeSoundTrigger.EventSources.Payloads;

public sealed class StatusChangedPayload
{
    public uint StatusId { get; init; }

    public string StatusName { get; init; } = string.Empty;

    public ushort Param { get; init; }

    public float RemainingTime { get; init; }

    public bool IsLocalPlayer { get; init; } = true;
}
