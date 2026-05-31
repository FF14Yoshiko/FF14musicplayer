namespace AllTimeSoundTrigger.EventSources.Payloads;

public sealed class JobChangedPayload
{
    public uint PreviousClassJobId { get; init; }

    public uint ClassJobId { get; init; }

    public string PreviousJobName { get; init; } = string.Empty;

    public string JobName { get; init; } = string.Empty;
}
