using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class SkillUsedTrigger : IEventIndexedTrigger
{
    private readonly TriggerTextFilter actorName;
    private readonly TriggerTextFilter skillNameContains;
    private readonly bool localPlayerOnly;

    public SkillUsedTrigger(string actorName, string skillNameContains, bool localPlayerOnly)
    {
        this.actorName = new TriggerTextFilter(actorName);
        this.skillNameContains = new TriggerTextFilter(skillNameContains);
        this.localPlayerOnly = localPlayerOnly;
    }

    public IReadOnlyList<string> EventTypes { get; } = ["SkillUsed"];

    public bool IsMatch(GameEvent e)
    {
        if (!e.EventType.Equals("SkillUsed", StringComparison.OrdinalIgnoreCase))
            return false;

        if (e.Payload is not SkillUsedPayload payload)
            return false;

        if (localPlayerOnly && !payload.IsLocalPlayer)
            return false;

        if (actorName.DoesNotMatch(payload.ActorName))
            return false;

        return skillNameContains.Matches(payload.SkillName);
    }
}
