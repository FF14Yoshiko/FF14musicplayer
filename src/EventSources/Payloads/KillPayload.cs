namespace AllTimeSoundTrigger.EventSources.Payloads;

public sealed class KillPayload
{
    public string ActorName { get; init; } = string.Empty;

    public string TargetName { get; init; } = string.Empty;

    public bool IsLocalPlayerKill { get; init; }

    public string RawMessage { get; init; } = string.Empty;

    public string ChatType { get; init; } = string.Empty;
}
