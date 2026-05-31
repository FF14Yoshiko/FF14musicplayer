namespace AllTimeSoundTrigger.EventSources;

public readonly record struct KillParseResult(
    string ActorName,
    string TargetName);
