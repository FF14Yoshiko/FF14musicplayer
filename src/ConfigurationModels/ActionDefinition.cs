using System;
using System.Collections.Generic;

namespace AllTimeSoundTrigger.ConfigurationModels;

[Serializable]
public sealed class ActionDefinition
{
    public string Type { get; set; } = "Log";

    public string Message { get; set; } = string.Empty;

    public string SoundId { get; set; } = string.Empty;

    public List<string> SoundIds { get; set; } = [];

    public string FilePath { get; set; } = string.Empty;

    public List<string> FilePaths { get; set; } = [];

    public float Volume { get; set; } = 1f;

    public int Priority { get; set; }

    public bool InterruptLowerPriority { get; set; } = true;

    public bool Loop { get; set; }

    public string PlaybackKey { get; set; } = string.Empty;

    public bool StopOnStatusLost { get; set; }
}
