using System;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class SkillUsedTrigger : ITrigger
{
    private readonly string actorName;
    private readonly string skillNameContains;
    private readonly bool localPlayerOnly;

    public SkillUsedTrigger(string actorName, string skillNameContains, bool localPlayerOnly)
    {
        this.actorName = actorName.Trim();
        this.skillNameContains = skillNameContains.Trim();
        this.localPlayerOnly = localPlayerOnly;
    }

    public bool IsMatch(GameEvent e)
    {
        if (!e.EventType.Equals("SkillUsed", StringComparison.OrdinalIgnoreCase))
            return false;

        if (e.Payload is not SkillUsedPayload payload)
            return false;

        if (localPlayerOnly && !payload.IsLocalPlayer)
            return false;

        if (actorName.Length > 0 && !payload.ActorName.Contains(actorName, StringComparison.OrdinalIgnoreCase))
            return false;

        return skillNameContains.Length == 0
            || payload.SkillName.Contains(skillNameContains, StringComparison.OrdinalIgnoreCase);
    }
}
