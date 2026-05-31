using System;
using AllTimeSoundTrigger.Core;
using AllTimeSoundTrigger.EventSources.Payloads;

namespace AllTimeSoundTrigger.Rules.Triggers;

public sealed class JobChangedTrigger : ITrigger
{
    private readonly uint classJobId;
    private readonly string jobNameContains;

    public JobChangedTrigger(int classJobId, string jobNameContains)
    {
        this.classJobId = classJobId > 0 ? (uint)classJobId : 0;
        this.jobNameContains = jobNameContains.Trim();
    }

    public bool IsMatch(GameEvent e)
    {
        if (!e.EventType.Equals("JobChanged", StringComparison.OrdinalIgnoreCase))
            return false;

        if (e.Payload is not JobChangedPayload payload)
            return false;

        if (classJobId > 0 && payload.ClassJobId != classJobId)
            return false;

        return jobNameContains.Length == 0
            || payload.JobName.Contains(jobNameContains, StringComparison.OrdinalIgnoreCase);
    }
}
