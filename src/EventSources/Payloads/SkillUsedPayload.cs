namespace AllTimeSoundTrigger.EventSources.Payloads;

public sealed class SkillUsedPayload
{
    public string ActorName { get; set; } = "你";

    public string SkillName { get; set; } = string.Empty;

    public string Verb { get; set; } = string.Empty;

    public bool IsLocalPlayer { get; set; }

    public bool IsCastStart { get; set; }

    public string RawMessage { get; set; } = string.Empty;

    public string ChatType { get; set; } = string.Empty;
}
