using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.Audio;
using AllTimeSoundTrigger.ConfigurationModels;
using AllTimeSoundTrigger.Services;
using Dalamud.Plugin.Services;

namespace AllTimeSoundTrigger.Actions;

public sealed class ActionFactory
{
    private readonly Dictionary<string, Func<ActionDefinition, IAction>> registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, SoundLibraryEntry?> resolveSoundById;

    public ActionFactory(
        EventLogService eventLogService,
        AudioPlaybackService audioPlaybackService,
        IPluginLog log,
        Func<string, SoundLibraryEntry?> resolveSoundById)
    {
        this.resolveSoundById = resolveSoundById;
        Register("Log", definition => new LogAction(eventLogService, log, definition.Message));
        Register("Sound", definition => new SoundAction(
            audioPlaybackService,
            eventLogService,
            log,
            CreateSoundRequest(definition)));
        Register("StopSound", definition => new StopSoundAction(
            audioPlaybackService,
            eventLogService,
            log,
            definition.PlaybackKey));
    }

    public void Register(string type, Func<ActionDefinition, IAction> factory)
    {
        registrations[type] = factory;
    }

    public IAction Create(ActionDefinition definition)
    {
        var type = string.IsNullOrWhiteSpace(definition.Type) ? "Log" : definition.Type.Trim();
        if (!registrations.TryGetValue(type, out var factory))
            throw new InvalidOperationException($"未知动作类型：{type}");

        return factory(definition);
    }

    private AudioPlaybackRequest CreateSoundRequest(ActionDefinition definition)
    {
        var soundId = (definition.SoundId ?? string.Empty).Trim();
        if (soundId.Length == 0)
        {
            return new AudioPlaybackRequest
            {
                FilePath = definition.FilePath,
                Volume = definition.Volume,
                Priority = definition.Priority,
                InterruptLowerPriority = definition.InterruptLowerPriority,
                Loop = definition.Loop || definition.StopOnStatusLost,
                PlaybackKey = definition.PlaybackKey,
                StopOnStatusLost = definition.StopOnStatusLost
            };
        }

        var entry = resolveSoundById(soundId);
        if (entry == null)
            throw new InvalidOperationException($"找不到音效库条目：{soundId}");

        return new AudioPlaybackRequest
        {
            FilePath = entry.FilePath,
            Volume = entry.DefaultVolume,
            Priority = entry.Priority,
            InterruptLowerPriority = entry.InterruptLowerPriority,
            Loop = definition.Loop || definition.StopOnStatusLost,
            PlaybackKey = definition.PlaybackKey,
            StopOnStatusLost = definition.StopOnStatusLost
        };
    }
}
