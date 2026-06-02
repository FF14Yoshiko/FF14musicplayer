using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ClientCondition = Dalamud.Plugin.Services.ICondition;

namespace AllTimeSoundTrigger.EventSources;

public sealed class ClientStatePoller : IEventSource
{
    private const long PollIntervalMs = 100;

    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly ClientCondition condition;
    private readonly IPluginLog log;
    private Action<GameEvent>? publish;
    private bool started;
    private bool hasSnapshot;
    private long lastPollTicks;
    private StateSnapshot lastSnapshot;

    public ClientStatePoller(
        IFramework framework,
        IClientState clientState,
        IObjectTable objectTable,
        ClientCondition condition,
        IPluginLog log)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.condition = condition;
        this.log = log;
    }

    public void Start(Action<GameEvent> publish)
    {
        if (started)
            return;

        this.publish = publish;
        framework.Update += OnFrameworkUpdate;
        started = true;
    }

    public void Stop()
    {
        if (!started)
            return;

        framework.Update -= OnFrameworkUpdate;
        publish = null;
        started = false;
        hasSnapshot = false;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = Environment.TickCount64;
        if (lastPollTicks != 0 && now - lastPollTicks < PollIntervalMs)
            return;

        lastPollTicks = now;
        try
        {
            Poll();
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "[ClientStatePoller] Failed to poll client state.");
        }
    }

    private void Poll()
    {
        var current = CaptureSnapshot();
        if (!hasSnapshot)
        {
            lastSnapshot = current;
            hasSnapshot = true;
            return;
        }

        if (current.TerritoryType != lastSnapshot.TerritoryType || current.MapId != lastSnapshot.MapId)
        {
            Publish("MapChanged", new MapChangedPayload
            {
                PreviousTerritoryType = lastSnapshot.TerritoryType,
                TerritoryType = current.TerritoryType,
                PreviousMapId = lastSnapshot.MapId,
                MapId = current.MapId
            });
        }

        if (current.HasLocalPlayer && lastSnapshot.HasLocalPlayer)
        {
            if (lastSnapshot.ClassJobId > 0 && current.ClassJobId > 0 && current.ClassJobId != lastSnapshot.ClassJobId)
            {
                Publish("JobChanged", new JobChangedPayload
                {
                    PreviousClassJobId = lastSnapshot.ClassJobId,
                    ClassJobId = current.ClassJobId,
                    PreviousJobName = lastSnapshot.JobName,
                    JobName = current.JobName
                });
            }

            if (lastSnapshot.MaxHp > 0 && current.MaxHp > 0
                && (current.CurrentHp != lastSnapshot.CurrentHp || current.MaxHp != lastSnapshot.MaxHp))
            {
                var payload = new HpChangedPayload
                {
                    PreviousCurrentHp = lastSnapshot.CurrentHp,
                    CurrentHp = current.CurrentHp,
                    PreviousMaxHp = lastSnapshot.MaxHp,
                    MaxHp = current.MaxHp,
                    PreviousHpPercent = CalculateHpPercent(lastSnapshot.CurrentHp, lastSnapshot.MaxHp),
                    HpPercent = CalculateHpPercent(current.CurrentHp, current.MaxHp)
                };

                Publish("HpChanged", payload);
                if (lastSnapshot.CurrentHp > 0 && current.CurrentHp == 0)
                    Publish("LocalPlayerDefeated", payload);
            }

            if (current.IsInCombat != lastSnapshot.IsInCombat)
            {
                Publish(current.IsInCombat ? "CombatEntered" : "CombatExited", new CombatStateChangedPayload
                {
                    WasInCombat = lastSnapshot.IsInCombat,
                    IsInCombat = current.IsInCombat
                });
            }
        }

        PublishStatusChanges(current);
        lastSnapshot = current;
    }

    private StateSnapshot CaptureSnapshot()
    {
        var localPlayer = objectTable.LocalPlayer;
        var hasLocalPlayer = localPlayer != null;
        var classJobId = hasLocalPlayer ? localPlayer!.ClassJob.RowId : 0;
        var currentHp = hasLocalPlayer ? localPlayer!.CurrentHp : 0;
        var maxHp = hasLocalPlayer ? localPlayer!.MaxHp : 0;
        var jobName = string.Empty;
        var statuses = hasLocalPlayer ? CaptureStatuses() : new Dictionary<uint, StatusSnapshot>();

        if (hasLocalPlayer)
        {
            try
            {
                jobName = localPlayer!.ClassJob.Value.Name.ExtractText();
            }
            catch
            {
                jobName = string.Empty;
            }
        }

        return new StateSnapshot(
            hasLocalPlayer,
            clientState.TerritoryType,
            clientState.MapId,
            classJobId,
            jobName,
            currentHp,
            maxHp,
            hasLocalPlayer && condition[ConditionFlag.InCombat],
            statuses);
    }

    private void PublishStatusChanges(StateSnapshot current)
    {
        foreach (var currentStatus in current.Statuses.Values)
        {
            if (!lastSnapshot.Statuses.ContainsKey(currentStatus.StatusId))
                Publish("StatusGained", ToPayload(currentStatus));
        }

        foreach (var previousStatus in lastSnapshot.Statuses.Values)
        {
            if (!current.Statuses.ContainsKey(previousStatus.StatusId))
                Publish("StatusLost", ToPayload(previousStatus));
        }
    }

    private Dictionary<uint, StatusSnapshot> CaptureStatuses()
    {
        var statuses = new Dictionary<uint, StatusSnapshot>();
        if (objectTable.LocalPlayer is not IBattleChara battleChara)
            return statuses;

        try
        {
            foreach (var status in battleChara.StatusList)
            {
                if (status.StatusId == 0)
                    continue;

                statuses[status.StatusId] = new StatusSnapshot(
                    status.StatusId,
                    ResolveStatusName(status),
                    status.Param,
                    status.RemainingTime);
            }
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "[ClientStatePoller] Failed to read local status list.");
        }

        return statuses;
    }

    private static string ResolveStatusName(Dalamud.Game.ClientState.Statuses.IStatus status)
    {
        try
        {
            return status.GameData.Value.Name.ExtractText();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static StatusChangedPayload ToPayload(StatusSnapshot status)
        => new()
        {
            StatusId = status.StatusId,
            StatusName = status.StatusName,
            Param = status.Param,
            RemainingTime = status.RemainingTime,
            IsLocalPlayer = true
        };

    private void Publish(string eventType, object payload)
    {
        publish?.Invoke(new GameEvent
        {
            EventType = eventType,
            Payload = payload,
            Timestamp = DateTime.Now
        });
    }

    private static double CalculateHpPercent(uint currentHp, uint maxHp)
        => maxHp == 0 ? 0 : Math.Clamp(currentHp * 100d / maxHp, 0d, 100d);

    private readonly record struct StateSnapshot(
        bool HasLocalPlayer,
        uint TerritoryType,
        uint MapId,
        uint ClassJobId,
        string JobName,
        uint CurrentHp,
        uint MaxHp,
        bool IsInCombat,
        IReadOnlyDictionary<uint, StatusSnapshot> Statuses);

    private readonly record struct StatusSnapshot(
        uint StatusId,
        string StatusName,
        ushort Param,
        float RemainingTime);
}
