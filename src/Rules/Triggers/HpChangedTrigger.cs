using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.Core;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class HpChangedTrigger : IEventIndexedTrigger
{
    public IReadOnlyList<string> EventTypes { get; } = ["HpChanged"];

    public bool IsMatch(GameEvent e)
        => e.EventType.Equals("HpChanged", StringComparison.OrdinalIgnoreCase);
}
