using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.Core;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class EventTypeTrigger : IEventIndexedTrigger
{
    private readonly string eventType;

    public EventTypeTrigger(string eventType)
    {
        this.eventType = (eventType ?? string.Empty).Trim();
        EventTypes = this.eventType.Length == 0 ? [] : [this.eventType];
    }

    public IReadOnlyList<string> EventTypes { get; }

    public bool IsMatch(GameEvent e)
        => eventType.Length == 0 || e.EventType.Equals(eventType, StringComparison.OrdinalIgnoreCase);
}
