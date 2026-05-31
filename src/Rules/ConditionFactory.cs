using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.ConfigurationModels;
using AllTimeSoundTrigger.Rules.Conditions;

namespace AllTimeSoundTrigger.Rules;

public sealed class ConditionFactory
{
    private readonly Dictionary<string, Func<ConditionDefinition, ICondition>> registrations = new(StringComparer.OrdinalIgnoreCase);

    public ConditionFactory()
    {
        Register("Always", _ => new AlwaysCondition());
    }

    public void Register(string type, Func<ConditionDefinition, ICondition> factory)
    {
        registrations[type] = factory;
    }

    public ICondition Create(ConditionDefinition definition)
    {
        var type = string.IsNullOrWhiteSpace(definition.Type) ? "Always" : definition.Type.Trim();
        if (!registrations.TryGetValue(type, out var factory))
            throw new InvalidOperationException($"未知条件类型：{type}");

        return factory(definition);
    }
}
