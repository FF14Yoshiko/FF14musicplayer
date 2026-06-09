using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class StatusGainedTrigger : IEventIndexedTrigger
{
    private readonly uint statusId;
    private readonly TriggerTextFilter statusNameContains;

    public StatusGainedTrigger(int statusId, string statusNameContains)
    {
        this.statusId = statusId > 0 ? (uint)statusId : 0;
        this.statusNameContains = new TriggerTextFilter(statusNameContains);
    }

    public IReadOnlyList<string> EventTypes { get; } = ["StatusGained"];

    public bool IsMatch(GameEvent e)
    {
        if (!e.EventType.Equals("StatusGained", StringComparison.OrdinalIgnoreCase))
            return false;

        if (e.Payload is not StatusChangedPayload payload)
            return false;

        if (statusId > 0 && payload.StatusId != statusId)
            return false;

        return statusNameContains.Matches(payload.StatusName);
    }
}
