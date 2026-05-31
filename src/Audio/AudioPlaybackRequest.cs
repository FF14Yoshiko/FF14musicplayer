namespace AllTimeSoundTrigger.Audio;

public sealed class AudioPlaybackRequest
{
    public string FilePath { get; init; } = string.Empty;

    public float Volume { get; init; } = 1f;

    public int Priority { get; init; }

    public bool InterruptLowerPriority { get; init; } = true;

    public bool Loop { get; init; }

    public string PlaybackKey { get; init; } = string.Empty;

    public bool StopOnStatusLost { get; init; }

    public uint StopStatusId { get; init; }
}
