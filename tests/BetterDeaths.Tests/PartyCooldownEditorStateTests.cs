using BetterDeaths.PartyCooldowns;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownEditorStateTests
{
    [Fact]
    public void EveryJobFocusClosesThePreviousJobAndRoleWhenEnabled()
    {
        foreach (var previous in PartyCooldownCatalog.Jobs)
            foreach (var next in PartyCooldownCatalog.Jobs)
            {
                var state = new PartyCooldownEditorState();
                state.SetJobOpen(previous.Id, true, autoCollapse: true);
                Assert.True(state.Focus(next.Id, autoCollapse: true));
                Assert.Equal(next.Id, state.FocusedJob);
                foreach (var job in PartyCooldownCatalog.Jobs)
                    Assert.Equal(job.Id == next.Id, state.IsJobOpen(job.Id));
                foreach (var role in Enum.GetValues<PartyCooldownRole>())
                    Assert.Equal(role == next.Role, state.IsRoleOpen(role));
            }
    }

    [Fact]
    public void DisabledModeKeepsMultipleJobsAndRolesOpen()
    {
        var state = new PartyCooldownEditorState();
        foreach (var job in PartyCooldownCatalog.Jobs)
            state.SetJobOpen(job.Id, true, autoCollapse: false);
        Assert.All(PartyCooldownCatalog.Jobs, job => Assert.True(state.IsJobOpen(job.Id)));
        Assert.All(Enum.GetValues<PartyCooldownRole>(), role => Assert.True(state.IsRoleOpen(role)));
        Assert.Equal(PartyCooldownCatalog.Jobs[^1].Id, state.FocusedJob);
    }

    [Fact]
    public void PreviewSelectionAndOpeningAJobUseTheSameFocusRules()
    {
        var preview = new PartyCooldownEditorState();
        var tree = new PartyCooldownEditorState();
        foreach (var id in new uint[] { 19, 33, 20, 42, 40, 21 })
        {
            preview.Focus(id, autoCollapse: true);
            tree.SetJobOpen(id, true, autoCollapse: true);
            Assert.Equal(preview.FocusedJob, tree.FocusedJob);
            foreach (var job in PartyCooldownCatalog.Jobs)
                Assert.Equal(preview.IsJobOpen(job.Id), tree.IsJobOpen(job.Id));
            foreach (var role in Enum.GetValues<PartyCooldownRole>())
                Assert.Equal(preview.IsRoleOpen(role), tree.IsRoleOpen(role));
        }
    }

    [Fact]
    public void UsersCanCloseTheFocusedJobWithoutItBeingForcedOpenAgain()
    {
        var state = new PartyCooldownEditorState();
        state.Focus(33, autoCollapse: true);
        state.SetJobOpen(33, false, autoCollapse: true);
        Assert.False(state.IsJobOpen(33));
        Assert.True(state.IsRoleOpen(PartyCooldownRole.Healer));
        Assert.Equal(33u, state.FocusedJob);
        state.SetRoleOpen(PartyCooldownRole.Healer, false);
        Assert.False(state.IsRoleOpen(PartyCooldownRole.Healer));
        state.Focus(33, autoCollapse: true);
        Assert.True(state.IsJobOpen(33));
        Assert.True(state.IsRoleOpen(PartyCooldownRole.Healer));
    }

    [Fact]
    public void SwitchingFocusAlsoClearsJobsHiddenInsideCollapsedRoles()
    {
        var state = new PartyCooldownEditorState();
        state.Focus(19, autoCollapse: false);
        state.Focus(21, autoCollapse: false);
        state.SetRoleOpen(PartyCooldownRole.Tank, false);
        state.Focus(33, autoCollapse: true);
        state.SetRoleOpen(PartyCooldownRole.Tank, true);
        Assert.False(state.IsJobOpen(19));
        Assert.False(state.IsJobOpen(21));
        Assert.True(state.IsJobOpen(33));
    }

    [Fact]
    public void EnablingAutoCollapseNarrowsTheOpenSectionsToTheCurrentJob()
    {
        var state = new PartyCooldownEditorState();
        state.Focus(19, autoCollapse: false);
        state.Focus(21, autoCollapse: false);
        state.Focus(33, autoCollapse: false);
        state.Focus(state.FocusedJob, autoCollapse: true);
        Assert.True(state.IsJobOpen(33));
        Assert.False(state.IsJobOpen(19));
        Assert.False(state.IsJobOpen(21));
        Assert.False(state.IsRoleOpen(PartyCooldownRole.Tank));
        state.Focus(40, autoCollapse: false);
        Assert.True(state.IsJobOpen(33));
        Assert.True(state.IsJobOpen(40));
    }

    [Fact]
    public void InvalidJobsDoNotChangeTheFocusedJobOrExpansionState()
    {
        var state = new PartyCooldownEditorState();
        state.Focus(19, autoCollapse: true);
        foreach (var id in new uint[] { 0, 999, uint.MaxValue })
        {
            Assert.False(state.Focus(id, autoCollapse: true));
            Assert.Equal(19u, state.FocusedJob);
            Assert.True(state.IsJobOpen(19));
            Assert.True(state.IsRoleOpen(PartyCooldownRole.Tank));
        }
    }

    [Theory]
    [InlineData(1, 19)]
    [InlineData(2, 20)]
    [InlineData(6, 24)]
    [InlineData(26, 27)]
    public void ClassIdsResolveToTheirJobWithoutOpeningAnotherSection(uint classId, uint jobId)
    {
        var state = new PartyCooldownEditorState();
        Assert.True(state.Focus(classId, autoCollapse: true));
        Assert.Equal(jobId, state.FocusedJob);
        Assert.True(state.IsJobOpen(classId));
        Assert.True(state.IsJobOpen(jobId));
        state.SetJobOpen(classId, false, autoCollapse: true);
        Assert.False(state.IsJobOpen(jobId));
    }

    [Fact]
    public void SeparateEditorsDoNotShareExpandedSections()
    {
        var first = new PartyCooldownEditorState();
        var second = new PartyCooldownEditorState();
        first.Focus(33, autoCollapse: true);
        second.Focus(19, autoCollapse: true);
        Assert.False(first.IsJobOpen(19));
        Assert.False(second.IsJobOpen(33));
    }

    [Fact]
    public void ReadingExpansionStateDoesNotAllocateManagedMemory()
    {
        var state = new PartyCooldownEditorState();
        state.Focus(19, autoCollapse: true);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = 0;
        for (var index = 0; index < 10000; index++)
            if (state.IsJobOpen(19) && state.IsRoleOpen(PartyCooldownRole.Tank))
                count++;
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(10000, count);
    }
}
