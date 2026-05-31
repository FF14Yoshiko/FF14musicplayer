namespace AllTimeSoundTrigger.EventSources.Payloads;

public sealed class HpChangedPayload
{
    public uint PreviousCurrentHp { get; init; }

    public uint CurrentHp { get; init; }

    public uint PreviousMaxHp { get; init; }

    public uint MaxHp { get; init; }

    public double PreviousHpPercent { get; init; }

    public double HpPercent { get; init; }
}
