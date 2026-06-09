using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class HpLowTrigger : IEventIndexedTrigger
{
    private readonly int thresholdPercent;

    public HpLowTrigger(int thresholdPercent)
    {
        this.thresholdPercent = Math.Clamp(thresholdPercent <= 0 ? 30 : thresholdPercent, 1, 100);
    }

    public IReadOnlyList<string> EventTypes { get; } = ["HpChanged"];

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
