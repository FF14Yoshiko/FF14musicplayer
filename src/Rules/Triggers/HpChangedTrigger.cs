using System;
using AllTimeSoundTrigger.Core;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class HpChangedTrigger : ITrigger
{
    public bool IsMatch(GameEvent e)
        => e.EventType.Equals("HpChanged", StringComparison.OrdinalIgnoreCase);
}
