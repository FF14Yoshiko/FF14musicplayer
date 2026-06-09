using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.Core;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class CombatEnteredTrigger : IEventIndexedTrigger
{
    public IReadOnlyList<string> EventTypes { get; } = ["CombatEntered"];

    public bool IsMatch(GameEvent e)
        => e.EventType.Equals("CombatEntered", StringComparison.OrdinalIgnoreCase);
}
