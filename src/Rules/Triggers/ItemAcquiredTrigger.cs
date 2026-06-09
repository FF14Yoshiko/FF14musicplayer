using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class ItemAcquiredTrigger : IEventIndexedTrigger
{
    private readonly TriggerTextFilter actorName;
    private readonly TriggerTextFilter itemNameContains;
    private readonly bool localPlayerOnly;

    public ItemAcquiredTrigger(string actorName, string itemNameContains, bool localPlayerOnly)
    {
        this.actorName = new TriggerTextFilter(actorName);
        this.itemNameContains = new TriggerTextFilter(itemNameContains);
        this.localPlayerOnly = localPlayerOnly;
    }

    public IReadOnlyList<string> EventTypes { get; } = ["ItemAcquired"];

    public bool IsMatch(GameEvent e)
    {
        if (!e.EventType.Equals("ItemAcquired", StringComparison.OrdinalIgnoreCase))
            return false;

        if (e.Payload is not ItemAcquiredPayload payload)
            return false;

        if (localPlayerOnly && !payload.IsLocalPlayer)
            return false;

        if (actorName.DoesNotMatch(payload.ActorName))
            return false;

        return itemNameContains.Matches(payload.ItemName);
    }
}
