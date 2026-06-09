using System;
using System.Collections.Generic;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class JobChangedTrigger : IEventIndexedTrigger
{
    private readonly uint classJobId;
    private readonly TriggerTextFilter jobNameContains;

    public JobChangedTrigger(int classJobId, string jobNameContains)
    {
        this.classJobId = classJobId > 0 ? (uint)classJobId : 0;
        this.jobNameContains = new TriggerTextFilter(jobNameContains);
    }

    public IReadOnlyList<string> EventTypes { get; } = ["JobChanged"];

    public bool IsMatch(GameEvent e)
    {
        if (!e.EventType.Equals("JobChanged", StringComparison.OrdinalIgnoreCase))
            return false;

        if (e.Payload is not JobChangedPayload payload)
            return false;

        if (classJobId > 0 && payload.ClassJobId != classJobId)
            return false;

        return jobNameContains.Matches(payload.JobName);
    }
}
