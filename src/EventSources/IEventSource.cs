using System;
using AllTimeSoundTrigger.Core;

namespace AllTimeSoundTrigger.EventSources;

public interface IEventSource
{
    void Start(Action<GameEvent> publish);

    void Stop();
}
