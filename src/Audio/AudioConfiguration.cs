using System;

namespace AllTimeSoundTrigger.Audio;

[Serializable]
public sealed class AudioConfiguration
{
    public int MaxConcurrentSounds { get; set; } = 4;

    public float MasterVolume { get; set; } = 1f;

    public void Normalize()
    {
        MaxConcurrentSounds = Math.Clamp(MaxConcurrentSounds, 1, 16);
        MasterVolume = Math.Clamp(MasterVolume, 0f, 1f);
    }
}
