using System;
using AllTimeSoundTrigger.Core;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class CombatEnteredTrigger : ITrigger
{
    public bool IsMatch(GameEvent e)
        => e.EventType.Equals("CombatEntered", StringComparison.OrdinalIgnoreCase);
}
