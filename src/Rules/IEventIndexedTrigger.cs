using System.Collections.Generic;

namespace AllTimeSoundTrigger.Rules;

public interface IEventIndexedTrigger : ITrigger
{
    IReadOnlyList<string> EventTypes { get; }
}
