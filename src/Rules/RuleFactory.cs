using System;
using System.Collections.Generic;
using System.Linq;
using AllTimeSoundTrigger.Actions;
using AllTimeSoundTrigger.Audio;
using AllTimeSoundTrigger.ConfigurationModels;
using AllTimeSoundTrigger.Services;
using Dalamud.Plugin.Services;

namespace AllTimeSoundTrigger.Rules;

public sealed class RuleFactory
{
    private readonly TriggerFactory triggerFactory = new();
    private readonly ConditionFactory conditionFactory = new();
    private readonly ActionFactory actionFactory;
    private readonly IPluginLog log;

    public RuleFactory(
        EventLogService eventLogService,
        AudioPlaybackService audioPlaybackService,
        IPluginLog log,
        Func<string, SoundLibraryEntry?> resolveSoundById)
    {
        actionFactory = new ActionFactory(eventLogService, audioPlaybackService, log, resolveSoundById);
        this.log = log;
    }

    public List<Rule> CreateRules(IEnumerable<RuleDefinition> definitions)
    {
        var rules = new List<Rule>();
        foreach (var definition in definitions)
        {
            if (!definition.Enabled)
                continue;

            try
            {
                rules.Add(CreateRule(definition));
            }
            catch (Exception ex)
            {
                log.Warning(ex, "[AllTimeSoundTrigger] 跳过无效规则 {RuleId}: {RuleName}", definition.Id, definition.Name);
            }
        }

        return rules;
    }

    private Rule CreateRule(RuleDefinition definition)
    {
        var trigger = triggerFactory.Create(definition.Trigger);
        var conditions = definition.Conditions.Select(conditionFactory.Create).ToArray();
        var actions = definition.Actions.Select(actionFactory.Create).ToArray();
        return new Rule(definition.Id, definition.CooldownSeconds, trigger, conditions, actions);
    }
}
