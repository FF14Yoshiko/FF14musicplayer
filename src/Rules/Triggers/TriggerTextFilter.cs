using System;

namespace AllTimeSoundTrigger.Rules.Triggers;

internal readonly struct TriggerTextFilter
{
    private readonly string value;

    public TriggerTextFilter(string? value)
    {
        this.value = (value ?? string.Empty).Trim();
    }

    public bool HasValue => value.Length > 0;

    public bool Matches(string text)
        => value.Length == 0 || (text ?? string.Empty).Contains(value, StringComparison.OrdinalIgnoreCase);

    public bool DoesNotMatch(string text)
        => value.Length > 0 && !Matches(text);
}
