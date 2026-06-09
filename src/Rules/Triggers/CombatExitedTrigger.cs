using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.Core;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class CombatExitedTrigger : IEventIndexedTrigger
{
    public IReadOnlyList<string> EventTypes { get; } = ["CombatExited"];

    public bool IsMatch(GameEvent e)
        => e.EventType.Equals("CombatExited", StringComparison.OrdinalIgnoreCase);
}
