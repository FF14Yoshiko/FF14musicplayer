using AllTimeSoundTrigger.Core;

namespace AllTimeSoundTrigger.Rules;

public interface ITrigger
{
    bool IsMatch(GameEvent e);
}
