using AllTimeSoundTrigger.Services;
using Dalamud.Plugin.Services;

namespace AllTimeSoundTrigger.Actions;

public sealed class LogAction : IAction
{
    private readonly EventLogService eventLogService;
    private readonly IPluginLog log;
    private readonly string message;

    public LogAction(EventLogService eventLogService, IPluginLog log, string message)
    {
        this.eventLogService = eventLogService;
        this.log = log;
        this.message = string.IsNullOrWhiteSpace(message) ? "规则命中。" : message.Trim();
    }

    public void Execute()
    {
        eventLogService.AddRuleMessage(message);
        log.Information("[AllTimeSoundTrigger] RuleMatched: {Message}", message);
    }
}
