using System;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class KillTrigger : ITrigger
{
    private readonly string actorName;
    private readonly string targetName;
    private readonly bool localPlayerOnly;

    public KillTrigger(string actorName, string targetName, bool localPlayerOnly)
    {
        this.actorName = actorName.Trim();
        this.targetName = targetName.Trim();
        this.localPlayerOnly = localPlayerOnly;
    }

    public bool IsMatch(GameEvent e)
    {
        if (!e.EventType.Equals("Kill", StringComparison.OrdinalIgnoreCase))
            return false;

        if (e.Payload is not KillPayload payload)
            return false;

        if (localPlayerOnly && !payload.IsLocalPlayerKill)
            return false;

        if (actorName.Length > 0 && !payload.ActorName.Contains(actorName, StringComparison.OrdinalIgnoreCase))
            return false;

        return targetName.Length == 0
            || payload.TargetName.Contains(targetName, StringComparison.OrdinalIgnoreCase);
    }
}
