using AllTimeSoundTrigger.Core;

namespace AllTimeSoundTrigger.Actions;

public interface IContextualAction : IAction
{
    void Execute(GameEvent gameEvent);
}
