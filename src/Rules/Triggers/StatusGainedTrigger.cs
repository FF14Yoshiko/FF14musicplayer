using System;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class StatusGainedTrigger : ITrigger
{
    private readonly uint statusId;
    private readonly string statusNameContains;

    public StatusGainedTrigger(int statusId, string statusNameContains)
    {
        this.statusId = statusId > 0 ? (uint)statusId : 0;
        this.statusNameContains = statusNameContains.Trim();
    }

    public bool IsMatch(GameEvent e)
    {
        if (!e.EventType.Equals("StatusGained", StringComparison.OrdinalIgnoreCase))
            return false;

        if (e.Payload is not StatusChangedPayload payload)
            return false;

        if (statusId > 0 && payload.StatusId != statusId)
            return false;

        return statusNameContains.Length == 0
            || payload.StatusName.Contains(statusNameContains, StringComparison.OrdinalIgnoreCase);
    }
}
