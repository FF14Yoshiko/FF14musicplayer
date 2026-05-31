namespace AllTimeSoundTrigger.EventSources.Payloads;

public sealed class CombatStateChangedPayload
{
    public bool WasInCombat { get; init; }

    public bool IsInCombat { get; init; }
}
