using System;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class HpLowTrigger : ITrigger
{
    private readonly int thresholdPercent;

    public HpLowTrigger(int thresholdPercent)
    {
        this.thresholdPercent = Math.Clamp(thresholdPercent <= 0 ? 30 : thresholdPercent, 1, 100);
    }

    public bool IsMatch(GameEvent e)
    {
        if (!e.EventType.Equals("HpChanged", StringComparison.OrdinalIgnoreCase))
            return false;

        if (e.Payload is not HpChangedPayload payload)
            return false;

        return payload.PreviousHpPercent > thresholdPercent
            && payload.HpPercent <= thresholdPercent;
    }
}
