using System.Collections.Generic;

namespace BetterDeaths.PartyCooldowns;

internal sealed class PartyCooldownEditorState
{
    private readonly HashSet<uint> openJobs = [];
    private readonly HashSet<PartyCooldownRole> openRoles = [];

    public uint FocusedJob { get; private set; } = 19;

    public bool IsJobOpen(uint jobId) => openJobs.Contains(PartyCooldownCatalog.NormalizeJob(jobId));
    public bool IsRoleOpen(PartyCooldownRole role) => openRoles.Contains(role);

    public bool Focus(uint jobId, bool autoCollapse)
    {
        jobId = PartyCooldownCatalog.NormalizeJob(jobId);
        foreach (var job in PartyCooldownCatalog.Jobs)
        {
            if (job.Id != jobId)
                continue;
            if (autoCollapse)
            {
                openJobs.Clear();
                openRoles.Clear();
            }
            FocusedJob = job.Id;
            openJobs.Add(job.Id);
            openRoles.Add(job.Role);
            return true;
        }
        return false;
    }

    public void SetJobOpen(uint jobId, bool open, bool autoCollapse)
    {
        if (open)
            Focus(jobId, autoCollapse);
        else
            openJobs.Remove(PartyCooldownCatalog.NormalizeJob(jobId));
    }

    public void SetRoleOpen(PartyCooldownRole role, bool open)
    {
        if (open)
            openRoles.Add(role);
        else
            openRoles.Remove(role);
    }
}
