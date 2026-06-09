using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class StatusLostTrigger : IEventIndexedTrigger
{
    private readonly uint statusId;
    private readonly TriggerTextFilter statusNameContains;

    public StatusLostTrigger(int statusId, string statusNameContains)
    {
        this.statusId = statusId > 0 ? (uint)statusId : 0;
        this.statusNameContains = new TriggerTextFilter(statusNameContains);
    }

    public IReadOnlyList<string> EventTypes { get; } = ["StatusLost"];

    public bool IsMatch(GameEvent e)
    {
        if (!e.EventType.Equals("StatusLost", StringComparison.OrdinalIgnoreCase))
            return false;

        if (e.Payload is not StatusChangedPayload payload)
            return false;

        if (statusId > 0 && payload.StatusId != statusId)
            return false;

        return statusNameContains.Matches(payload.StatusName);
    }
}
