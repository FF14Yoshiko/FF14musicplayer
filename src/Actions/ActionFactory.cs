using System;
using System.Collections.Generic;
using System.Linq;
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
            CreateSoundRequests(definition)));
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

    private IReadOnlyList<AudioPlaybackRequest> CreateSoundRequests(ActionDefinition definition)
    {
        var soundIds = GetSoundIds(definition);
        if (soundIds.Count == 0)
        {
            var filePaths = GetFilePaths(definition);
            return filePaths
                .Select(filePath => new AudioPlaybackRequest
                {
                    FilePath = filePath,
                    Volume = definition.Volume,
                    Priority = definition.Priority,
                    InterruptLowerPriority = definition.InterruptLowerPriority,
                    Loop = definition.Loop || definition.StopOnStatusLost,
                    PlaybackKey = definition.PlaybackKey,
                    StopOnStatusLost = definition.StopOnStatusLost
                })
                .ToArray();
        }

        var requests = new List<AudioPlaybackRequest>();
        foreach (var soundId in soundIds)
        {
            var entry = resolveSoundById(soundId);
            if (entry == null)
                throw new InvalidOperationException($"找不到音效库条目：{soundId}");

            requests.Add(new AudioPlaybackRequest
            {
                FilePath = entry.FilePath,
                Volume = entry.DefaultVolume,
                Priority = entry.Priority,
                InterruptLowerPriority = entry.InterruptLowerPriority,
                Loop = definition.Loop || definition.StopOnStatusLost,
                PlaybackKey = definition.PlaybackKey,
                StopOnStatusLost = definition.StopOnStatusLost
            });
        }

        return requests;
    }

    private static IReadOnlyList<string> GetSoundIds(ActionDefinition definition)
    {
        var soundIds = new List<string>();
        if (definition.SoundIds != null)
        {
            soundIds.AddRange(definition.SoundIds
                .Select(item => (item ?? string.Empty).Trim())
                .Where(item => item.Length > 0));
        }

        var legacySoundId = (definition.SoundId ?? string.Empty).Trim();
        if (legacySoundId.Length > 0)
            soundIds.Insert(0, legacySoundId);

        return soundIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> GetFilePaths(ActionDefinition definition)
    {
        var filePaths = new List<string>();
        if (definition.FilePaths != null)
        {
            filePaths.AddRange(definition.FilePaths
                .Select(item => (item ?? string.Empty).Trim())
                .Where(item => item.Length > 0));
        }

        var legacyFilePath = (definition.FilePath ?? string.Empty).Trim();
        if (legacyFilePath.Length > 0)
            filePaths.Insert(0, legacyFilePath);

        return filePaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .DefaultIfEmpty(string.Empty)
            .ToArray();
    }
}
