using System;
using AllTimeSoundTrigger.Core;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class EventTypeTrigger : ITrigger
{
    private readonly string eventType;

    public EventTypeTrigger(string eventType)
    {
        this.eventType = eventType;
    }

    public bool IsMatch(GameEvent e)
        => eventType.Length == 0 || e.EventType.Equals(eventType, StringComparison.OrdinalIgnoreCase);
}
