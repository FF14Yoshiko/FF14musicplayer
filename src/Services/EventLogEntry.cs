using System;

namespace AllTimeSoundTrigger.Services;

public sealed class EventLogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;

    public string Level { get; init; } = "Info";

    public string EventType { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string RawText { get; init; } = string.Empty;
}
