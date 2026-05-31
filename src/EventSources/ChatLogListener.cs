using System;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Plugin.Services;

namespace AllTimeSoundTrigger.EventSources;

public sealed class ChatLogListener : IEventSource
{
    private readonly IChatGui chatGui;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private Action<GameEvent>? publish;
    private bool started;

    public ChatLogListener(IChatGui chatGui, IObjectTable objectTable, IPluginLog log)
    {
        this.chatGui = chatGui;
        this.objectTable = objectTable;
        this.log = log;
    }

    public void Start(Action<GameEvent> publish)
    {
        if (started)
            return;

        this.publish = publish;
        chatGui.ChatMessage += OnChatMessage;
        started = true;
    }

    public void Stop()
    {
        if (!started)
            return;

        chatGui.ChatMessage -= OnChatMessage;
        publish = null;
        started = false;
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        try
        {
            var rawText = CleanText(message.Message.TextValue);
            if (rawText.Length == 0)
                return;

            var localPlayerName = CleanText(objectTable.LocalPlayer?.Name.TextValue);
            var sender = CleanText(message.Sender.TextValue);
            if (ItemAcquiredChatParser.TryParse(rawText, sender, out var parsedItem))
            {
                var itemPayload = new ItemAcquiredPayload
                {
                    ActorName = parsedItem.ActorName,
                    ItemName = parsedItem.ItemName,
                    Quantity = parsedItem.Quantity,
                    IsLocalPlayer = SkillUseChatParser.IsLocalActor(parsedItem.ActorName, localPlayerName),
                    RawMessage = rawText,
                    ChatType = message.LogKind.ToString()
                };

                publish?.Invoke(new GameEvent
                {
                    EventType = "ItemAcquired",
                    Payload = itemPayload,
                    Timestamp = DateTime.Now
                });

                return;
            }

            if (KillChatParser.TryParse(rawText, sender, out var parsedKill))
            {
                var killPayload = new KillPayload
                {
                    ActorName = parsedKill.ActorName,
                    TargetName = parsedKill.TargetName,
                    IsLocalPlayerKill = SkillUseChatParser.IsLocalActor(parsedKill.ActorName, localPlayerName),
                    RawMessage = rawText,
                    ChatType = message.LogKind.ToString()
                };

                publish?.Invoke(new GameEvent
                {
                    EventType = "Kill",
                    Payload = killPayload,
                    Timestamp = DateTime.Now
                });

                return;
            }

            if (!SkillUseChatParser.TryParse(rawText, out var parsed))
                return;

            var payload = new SkillUsedPayload
            {
                ActorName = parsed.ActorName,
                SkillName = parsed.SkillName,
                Verb = parsed.Verb,
                IsLocalPlayer = SkillUseChatParser.IsLocalActor(parsed.ActorName, localPlayerName),
                IsCastStart = parsed.IsCastStart,
                RawMessage = rawText,
                ChatType = message.LogKind.ToString()
            };

            publish?.Invoke(new GameEvent
            {
                EventType = "SkillUsed",
                Payload = payload,
                Timestamp = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[ChatLogListener] 解析聊天日志失败。");
        }
    }

    private static string CleanText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        return System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", " ").Trim();
    }
}
