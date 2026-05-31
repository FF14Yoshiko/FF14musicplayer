namespace AllTimeSoundTrigger.EventSources;

public sealed class SkillUseParseResult
{
    public string ActorName { get; init; } = string.Empty;

    public string SkillName { get; init; } = string.Empty;

    public string Verb { get; init; } = string.Empty;

    public bool IsCastStart { get; init; }
}
