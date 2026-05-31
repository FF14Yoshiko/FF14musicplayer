using System;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class ItemAcquiredTrigger : ITrigger
{
    private readonly string actorName;
    private readonly string itemNameContains;
    private readonly bool localPlayerOnly;

    public ItemAcquiredTrigger(string actorName, string itemNameContains, bool localPlayerOnly)
    {
        this.actorName = actorName.Trim();
        this.itemNameContains = itemNameContains.Trim();
        this.localPlayerOnly = localPlayerOnly;
    }

    public bool IsMatch(GameEvent e)
    {
        if (!e.EventType.Equals("ItemAcquired", StringComparison.OrdinalIgnoreCase))
            return false;

        if (e.Payload is not ItemAcquiredPayload payload)
            return false;

        if (localPlayerOnly && !payload.IsLocalPlayer)
            return false;

        if (actorName.Length > 0 && !payload.ActorName.Contains(actorName, StringComparison.OrdinalIgnoreCase))
            return false;

        return itemNameContains.Length == 0
            || payload.ItemName.Contains(itemNameContains, StringComparison.OrdinalIgnoreCase);
    }
}
