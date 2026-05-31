using System;

namespace AllTimeSoundTrigger.Core;

// 统一事件结构。所有事件源都只向外发布这个类型，规则引擎与 UI 不依赖 Dalamud 原始事件。
public sealed class GameEvent
{
    public string EventType { get; set; } = string.Empty;

    public object Payload { get; set; } = new();

    public DateTime Timestamp { get; set; } = DateTime.Now;
}
