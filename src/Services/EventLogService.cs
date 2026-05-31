using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;

namespace AllTimeSoundTrigger.Services;

public sealed class EventLogService
{
    private const int MaxEntries = 300;

    private readonly object gate = new();
    private readonly List<EventLogEntry> entries = new();

    public void AddSystemMessage(string message)
    {
        Add(new EventLogEntry
        {
            Timestamp = DateTime.Now,
            Level = "System",
            EventType = "System",
            Message = message
        });
    }

    public void AddRuleMessage(string message)
    {
        Add(new EventLogEntry
        {
            Timestamp = DateTime.Now,
            Level = "Rule",
            EventType = "RuleMatched",
            Message = message
        });
    }

    public void RecordEvent(GameEvent gameEvent)
    {
        Add(gameEvent.Payload switch
        {
            SkillUsedPayload payload => new EventLogEntry
            {
                Timestamp = gameEvent.Timestamp,
                Level = "Event",
                EventType = gameEvent.EventType,
                Message = $"{payload.ActorName}{payload.Verb}: {payload.SkillName}"
                    + (payload.IsLocalPlayer ? "（本机玩家）" : string.Empty)
                    + (payload.IsCastStart ? "（开始读条）" : string.Empty),
                RawText = payload.RawMessage
            },
            KillPayload payload => new EventLogEntry
            {
                Timestamp = gameEvent.Timestamp,
                Level = "Event",
                EventType = gameEvent.EventType,
                Message = $"{payload.ActorName}击倒：{payload.TargetName}"
                    + (payload.IsLocalPlayerKill ? "（本机玩家）" : string.Empty),
                RawText = payload.RawMessage
            },
            ItemAcquiredPayload payload => new EventLogEntry
            {
                Timestamp = gameEvent.Timestamp,
                Level = "Event",
                EventType = gameEvent.EventType,
                Message = $"{payload.ActorName} 获得：{payload.ItemName} x{payload.Quantity}"
                    + (payload.IsLocalPlayer ? "（本机玩家）" : string.Empty),
                RawText = payload.RawMessage
            },
            MapChangedPayload payload => new EventLogEntry
            {
                Timestamp = gameEvent.Timestamp,
                Level = "Event",
                EventType = gameEvent.EventType,
                Message = $"地图切换：{payload.PreviousTerritoryType}/{payload.PreviousMapId} -> {payload.TerritoryType}/{payload.MapId}"
            },
            CombatStateChangedPayload payload => new EventLogEntry
            {
                Timestamp = gameEvent.Timestamp,
                Level = "Event",
                EventType = gameEvent.EventType,
                Message = payload.IsInCombat ? "进入战斗" : "脱离战斗"
            },
            HpChangedPayload payload => new EventLogEntry
            {
                Timestamp = gameEvent.Timestamp,
                Level = "Event",
                EventType = gameEvent.EventType,
                Message = $"HP：{payload.PreviousCurrentHp}/{payload.PreviousMaxHp} ({payload.PreviousHpPercent:0.0}%) -> {payload.CurrentHp}/{payload.MaxHp} ({payload.HpPercent:0.0}%)"
            },
            JobChangedPayload payload => new EventLogEntry
            {
                Timestamp = gameEvent.Timestamp,
                Level = "Event",
                EventType = gameEvent.EventType,
                Message = $"职业切换：{FormatJob(payload.PreviousClassJobId, payload.PreviousJobName)} -> {FormatJob(payload.ClassJobId, payload.JobName)}"
            },
            StatusChangedPayload payload => new EventLogEntry
            {
                Timestamp = gameEvent.Timestamp,
                Level = "Event",
                EventType = gameEvent.EventType,
                Message = $"{(gameEvent.EventType.Equals("StatusGained", StringComparison.OrdinalIgnoreCase) ? "获得状态" : "状态消失")}：{FormatStatus(payload.StatusId, payload.StatusName)}"
                    + (payload.RemainingTime > 0 ? $"，剩余 {payload.RemainingTime:0.0}s" : string.Empty)
            },
            _ => new EventLogEntry
            {
                Timestamp = gameEvent.Timestamp,
                Level = "Event",
                EventType = gameEvent.EventType,
                Message = $"收到事件：{gameEvent.EventType}"
            }
        });
    }

    public EventLogEntry[] Snapshot()
    {
        lock (gate)
            return entries.ToArray();
    }

    public void Clear()
    {
        lock (gate)
            entries.Clear();
    }

    private static string FormatJob(uint classJobId, string jobName)
        => string.IsNullOrWhiteSpace(jobName)
            ? classJobId.ToString()
            : $"{jobName}({classJobId})";

    private static string FormatStatus(uint statusId, string statusName)
        => string.IsNullOrWhiteSpace(statusName)
            ? statusId.ToString()
            : $"{statusName}({statusId})";

    private void Add(EventLogEntry entry)
    {
        lock (gate)
        {
            entries.Add(entry);
            if (entries.Count > MaxEntries)
                entries.RemoveRange(0, entries.Count - MaxEntries);
        }
    }
}
