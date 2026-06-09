using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class KillTrigger : IEventIndexedTrigger
{
    private readonly TriggerTextFilter actorName;
    private readonly TriggerTextFilter targetName;
    private readonly bool localPlayerOnly;

    public KillTrigger(string actorName, string targetName, bool localPlayerOnly)
    {
        this.actorName = new TriggerTextFilter(actorName);
        this.targetName = new TriggerTextFilter(targetName);
        this.localPlayerOnly = localPlayerOnly;
    }

    public IReadOnlyList<string> EventTypes { get; } = ["Kill"];

    public bool IsMatch(GameEvent e)
    {
        if (!e.EventType.Equals("Kill", StringComparison.OrdinalIgnoreCase))
            return false;

        if (e.Payload is not KillPayload payload)
            return false;

        if (localPlayerOnly && !payload.IsLocalPlayerKill)
            return false;

        if (actorName.DoesNotMatch(payload.ActorName))
            return false;

        return targetName.Matches(payload.TargetName);
    }
}
