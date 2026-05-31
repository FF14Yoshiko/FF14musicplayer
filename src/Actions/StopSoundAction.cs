using AllTimeSoundTrigger.Audio;
using AllTimeSoundTrigger.Services;
using Dalamud.Plugin.Services;

namespace AllTimeSoundTrigger.Actions;

public sealed class StopSoundAction : IAction
{
    private readonly AudioPlaybackService audioPlaybackService;
    private readonly EventLogService eventLogService;
    private readonly IPluginLog log;
    private readonly string playbackKey;

    public StopSoundAction(
        AudioPlaybackService audioPlaybackService,
        EventLogService eventLogService,
        IPluginLog log,
        string playbackKey)
    {
        this.audioPlaybackService = audioPlaybackService;
        this.eventLogService = eventLogService;
        this.log = log;
        this.playbackKey = (playbackKey ?? string.Empty).Trim();
    }

    public void Execute()
    {
        if (playbackKey.Length == 0)
        {
            log.Warning("[AllTimeSoundTrigger] StopSoundAction skipped: playback key is empty.");
            return;
        }

        var stopped = audioPlaybackService.StopByKey(playbackKey);
        eventLogService.AddRuleMessage(stopped > 0
            ? $"停止音效：{playbackKey}"
            : $"未找到正在播放的音效：{playbackKey}");
    }
}
