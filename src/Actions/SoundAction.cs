using System;
using System.Collections.Generic;
using System.IO;
using AllTimeSoundTrigger.Audio;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;
using AllTimeSoundTrigger.Services;
using Dalamud.Plugin.Services;

namespace AllTimeSoundTrigger.Actions;

public sealed class SoundAction : IContextualAction
{
    private readonly AudioPlaybackService audioPlaybackService;
    private readonly EventLogService eventLogService;
    private readonly IPluginLog log;
    private readonly IReadOnlyList<AudioPlaybackRequest> requests;
    private readonly Random random = new();

    public SoundAction(
        AudioPlaybackService audioPlaybackService,
        EventLogService eventLogService,
        IPluginLog log,
        IReadOnlyList<AudioPlaybackRequest> requests)
    {
        this.audioPlaybackService = audioPlaybackService;
        this.eventLogService = eventLogService;
        this.log = log;
        this.requests = requests.Count > 0
            ? requests
            : throw new ArgumentException("SoundAction requires at least one playback request.", nameof(requests));
    }

    public void Execute()
    {
        ExecuteCore(PickRequest());
    }

    public void Execute(GameEvent gameEvent)
    {
        ExecuteCore(CreateContextualRequest(gameEvent, PickRequest()));
    }

    private AudioPlaybackRequest PickRequest()
    {
        if (requests.Count == 1)
            return requests[0];

        lock (random)
            return requests[random.Next(requests.Count)];
    }

    private AudioPlaybackRequest? CreateContextualRequest(GameEvent gameEvent, AudioPlaybackRequest request)
    {
        if (!request.StopOnStatusLost)
            return request;

        if (!gameEvent.EventType.Equals("StatusGained", StringComparison.OrdinalIgnoreCase)
            || gameEvent.Payload is not StatusChangedPayload statusPayload
            || statusPayload.StatusId == 0)
        {
            eventLogService.AddRuleMessage("Buff 消失自动停止只支持“获得状态/Buff”触发器，已跳过播放。");
            log.Warning("[AllTimeSoundTrigger] StopOnStatusLost sound skipped because the event was {EventType}.", gameEvent.EventType);
            return null;
        }

        return new AudioPlaybackRequest
        {
            FilePath = request.FilePath,
            Volume = request.Volume,
            Priority = request.Priority,
            InterruptLowerPriority = request.InterruptLowerPriority,
            Loop = true,
            PlaybackKey = string.IsNullOrWhiteSpace(request.PlaybackKey)
                ? $"status:{statusPayload.StatusId}"
                : request.PlaybackKey,
            StopOnStatusLost = true,
            StopStatusId = statusPayload.StatusId
        };
    }

    private void ExecuteCore(AudioPlaybackRequest? effectiveRequest)
    {
        if (effectiveRequest == null)
            return;

        if (effectiveRequest.StopOnStatusLost && effectiveRequest.StopStatusId == 0)
        {
            eventLogService.AddRuleMessage("Buff 消失自动停止缺少 Buff ID，已跳过播放。");
            log.Warning("[AllTimeSoundTrigger] StopOnStatusLost sound skipped because StopStatusId was empty.");
            return;
        }

        if (audioPlaybackService.Play(effectiveRequest))
            eventLogService.AddRuleMessage($"{(effectiveRequest.Loop ? "循环播放音效" : "播放音效")}：{Path.GetFileName(effectiveRequest.FilePath)}");
        else
            log.Warning("[AllTimeSoundTrigger] SoundAction skipped: {FilePath}", effectiveRequest.FilePath);
    }
}
