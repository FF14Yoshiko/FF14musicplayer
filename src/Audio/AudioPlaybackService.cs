using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AllTimeSoundTrigger.Services;
using AllTimeSoundTrigger.Utilities;
using Dalamud.Plugin.Services;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AllTimeSoundTrigger.Audio;

public sealed class AudioPlaybackService : IDisposable
{
    private readonly Func<int> maxConcurrentSounds;
    private readonly Func<float> masterVolume;
    private readonly EventLogService eventLogService;
    private readonly IPluginLog log;
    private readonly object gate = new();
    private readonly List<ActivePlayback> active = [];
    private bool disposed;

    public AudioPlaybackService(
        Func<int> maxConcurrentSounds,
        Func<float> masterVolume,
        EventLogService eventLogService,
        IPluginLog log)
    {
        this.maxConcurrentSounds = maxConcurrentSounds;
        this.masterVolume = masterVolume;
        this.eventLogService = eventLogService;
        this.log = log;
    }

    public int ActiveCount
    {
        get
        {
            lock (gate)
                return active.Count;
        }
    }

    public bool Play(AudioPlaybackRequest request)
    {
        if (disposed)
            return false;

        request = new AudioPlaybackRequest
        {
            FilePath = FilePathText.Normalize(request.FilePath),
            Volume = request.Volume,
            Priority = request.Priority,
            InterruptLowerPriority = request.InterruptLowerPriority,
            Loop = request.Loop,
            PlaybackKey = (request.PlaybackKey ?? string.Empty).Trim(),
            StopOnStatusLost = request.StopOnStatusLost,
            StopStatusId = request.StopStatusId
        };

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            log.Warning("[AllTimeSoundTrigger] Sound file not found: {FilePath}", request.FilePath);
            eventLogService.AddSystemMessage($"音频文件不存在：{request.FilePath}");
            return false;
        }

        lock (gate)
        {
            PruneDisposed();
            if (IsPlaybackKeyActive(request.PlaybackKey))
                return true;
        }

        ActivePlayback playback;
        try
        {
            playback = CreatePlayback(request);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[AllTimeSoundTrigger] Failed to create audio playback for {FilePath}", request.FilePath);
            eventLogService.AddSystemMessage($"音频加载失败：{Path.GetFileName(request.FilePath)}");
            return false;
        }

        lock (gate)
        {
            if (IsPlaybackKeyActive(request.PlaybackKey))
            {
                playback.Dispose();
                return true;
            }

            if (!EnsureCapacityFor(request))
            {
                playback.Dispose();
                return false;
            }

            active.Add(playback);
        }

        playback.Output.PlaybackStopped += (_, _) => RemoveAndDispose(playback);
        playback.Output.Play();
        log.Information("[AllTimeSoundTrigger] SoundPlayed: {FilePath}", request.FilePath);
        return true;
    }

    public int StopByKey(string playbackKey)
    {
        var normalizedKey = (playbackKey ?? string.Empty).Trim();
        if (normalizedKey.Length == 0)
            return 0;

        ActivePlayback[] snapshot;
        lock (gate)
        {
            snapshot = active
                .Where(item => item.PlaybackKey.Equals(normalizedKey, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var playback in snapshot)
                active.Remove(playback);
        }

        foreach (var playback in snapshot)
            playback.Dispose();

        return snapshot.Length;
    }

    public int StopByStatusId(uint statusId)
    {
        if (statusId == 0)
            return 0;

        ActivePlayback[] snapshot;
        lock (gate)
        {
            snapshot = active
                .Where(item => item.StopOnStatusLost && item.StopStatusId == statusId)
                .ToArray();
            foreach (var playback in snapshot)
                active.Remove(playback);
        }

        foreach (var playback in snapshot)
            playback.Dispose();

        return snapshot.Length;
    }

    public void StopAll()
    {
        ActivePlayback[] snapshot;
        lock (gate)
        {
            snapshot = active.ToArray();
            active.Clear();
        }

        foreach (var playback in snapshot)
            playback.Dispose();
    }

    public void RefreshActiveVolumes()
    {
        ActivePlayback[] snapshot;
        lock (gate)
            snapshot = active.ToArray();

        var currentMasterVolume = Math.Clamp(masterVolume(), 0f, 1f);
        foreach (var playback in snapshot)
            playback.ApplyMasterVolume(currentMasterVolume);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        StopAll();
    }

    private ActivePlayback CreatePlayback(AudioPlaybackRequest request)
    {
        var sourceReader = CreateReader(request.FilePath);
        WaveStream reader = request.Loop ? new LoopStream(sourceReader) : sourceReader;
        var volume = Math.Clamp(request.Volume, 0f, 1f) * Math.Clamp(masterVolume(), 0f, 1f);
        var volumeProvider = new VolumeSampleProvider(reader.ToSampleProvider())
        {
            Volume = volume
        };

        var output = new WaveOutEvent();
        output.Init(volumeProvider);
        return new ActivePlayback(
            request.FilePath,
            request.PlaybackKey,
            request.Priority,
            request.StopOnStatusLost,
            request.StopStatusId,
            output,
            reader,
            volumeProvider,
            Math.Clamp(request.Volume, 0f, 1f));
    }

    private static WaveStream CreateReader(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            ? new VorbisWaveReader(filePath)
            : new AudioFileReader(filePath);
    }

    private bool EnsureCapacityFor(AudioPlaybackRequest request)
    {
        PruneDisposed();
        var max = Math.Max(1, maxConcurrentSounds());
        if (active.Count < max)
            return true;

        if (!request.InterruptLowerPriority)
        {
            eventLogService.AddSystemMessage($"并发播放已满，跳过：{Path.GetFileName(request.FilePath)}");
            return false;
        }

        var candidate = active
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.StartedAtUtc)
            .FirstOrDefault();

        if (candidate == null || candidate.Priority >= request.Priority)
        {
            eventLogService.AddSystemMessage($"没有更低优先级音频可打断，跳过：{Path.GetFileName(request.FilePath)}");
            return false;
        }

        active.Remove(candidate);
        candidate.Dispose();
        return true;
    }

    private bool IsPlaybackKeyActive(string playbackKey)
        => playbackKey.Length > 0
            && active.Any(item => item.PlaybackKey.Equals(playbackKey, StringComparison.OrdinalIgnoreCase));

    private void PruneDisposed()
    {
        active.RemoveAll(item => item.IsDisposed);
    }

    private void RemoveAndDispose(ActivePlayback playback)
    {
        lock (gate)
            active.Remove(playback);

        playback.Dispose();
    }

    private sealed class ActivePlayback : IDisposable
    {
        private readonly WaveStream reader;
        private readonly VolumeSampleProvider volumeProvider;
        private readonly float requestVolume;

        public ActivePlayback(
            string filePath,
            string playbackKey,
            int priority,
            bool stopOnStatusLost,
            uint stopStatusId,
            IWavePlayer output,
            WaveStream reader,
            VolumeSampleProvider volumeProvider,
            float requestVolume)
        {
            FilePath = filePath;
            PlaybackKey = playbackKey;
            Priority = priority;
            StopOnStatusLost = stopOnStatusLost;
            StopStatusId = stopStatusId;
            Output = output;
            this.reader = reader;
            this.volumeProvider = volumeProvider;
            this.requestVolume = requestVolume;
        }

        public string FilePath { get; }

        public string PlaybackKey { get; }

        public int Priority { get; }

        public bool StopOnStatusLost { get; }

        public uint StopStatusId { get; }

        public IWavePlayer Output { get; }

        public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

        public bool IsDisposed { get; private set; }

        public void ApplyMasterVolume(float masterVolume)
        {
            if (IsDisposed)
                return;

            volumeProvider.Volume = requestVolume * Math.Clamp(masterVolume, 0f, 1f);
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            Output.Dispose();
            reader.Dispose();
        }
    }

    private sealed class LoopStream : WaveStream
    {
        private readonly WaveStream sourceStream;

        public LoopStream(WaveStream sourceStream)
        {
            this.sourceStream = sourceStream;
        }

        public override WaveFormat WaveFormat => sourceStream.WaveFormat;

        public override long Length => sourceStream.Length;

        public override long Position
        {
            get => sourceStream.Position;
            set => sourceStream.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var totalBytesRead = 0;
            while (totalBytesRead < count)
            {
                var bytesRead = sourceStream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
                if (bytesRead == 0)
                {
                    if (sourceStream.Position == 0)
                        break;

                    sourceStream.Position = 0;
                    continue;
                }

                totalBytesRead += bytesRead;
            }

            return totalBytesRead;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                sourceStream.Dispose();

            base.Dispose(disposing);
        }
    }
}
