namespace BetterDeaths.Tests;

public sealed class ReplayDebuffSourceCorrectionPolicyTests
{
    [Fact]
    public void NormalizeForAnalysis_RemovesSourcePlaceholdersButKeepsDistinctKnownInstances()
    {
        var startedAt = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        ReplayDebuffSnapshot Change(float seconds, uint sourceId, bool active) => new(
            startedAt.AddSeconds(seconds),
            seconds,
            "member-1",
            "Nai",
            0,
            34,
            "SAM",
            new StatusSnapshot(4879, "Tele-Portent", 0, sourceId, 0, active ? 7 : 0),
            active);

        var normalized = ReplayDebuffSourceCorrectionPolicy.NormalizeForAnalysis(
        [
            Change(10.00f, 0, true),
            Change(10.02f, 0, true),
            Change(10.05f, 0x40000001, true),
            Change(10.06f, 0, false),
            Change(10.07f, 0x40000002, true),
            Change(10.08f, 0, false),
            Change(17.00f, 0x40000001, false),
            Change(20.00f, 0x40000002, false),
        ]);

        Assert.Equal(4, normalized.Count);
        Assert.DoesNotContain(normalized, change => change.Status.SourceId == 0);
        Assert.Equal(2, normalized.Count(change => change.Active));
        Assert.Equal(2, normalized.Count(change => !change.Active));
        Assert.Equal(2, normalized.Select(change => change.Status.SourceId).Distinct().Count());
    }

    [Fact]
    public void NormalizeForAnalysis_PreservesSourceLessStatusWithoutAReplacement()
    {
        var startedAt = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        var changes = new[]
        {
            new ReplayDebuffSnapshot(
                startedAt,
                10,
                "member-1",
                "Nai",
                0,
                34,
                "SAM",
                new StatusSnapshot(4876, "Tele-Portent", 0, 0, 0, 7),
                true),
            new ReplayDebuffSnapshot(
                startedAt.AddSeconds(7),
                17,
                "member-1",
                "Nai",
                0,
                34,
                "SAM",
                new StatusSnapshot(4876, "Tele-Portent", 0, 0, 0, 0),
                false),
        };

        var normalized = ReplayDebuffSourceCorrectionPolicy.NormalizeForAnalysis(changes);

        Assert.Equal(changes, normalized);
    }

    [Fact]
    public void NormalizeForAnalysis_ReconcilesEveryStatusWhenASourcedCopyArrives()
    {
        var startedAt = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        ReplayDebuffSnapshot Change(float seconds, uint sourceId, bool active) => new(
            startedAt.AddSeconds(seconds),
            seconds,
            "member-1",
            "Nai",
            0,
            34,
            "SAM",
            new StatusSnapshot(9999, "Other debuff", 0, sourceId, 0, active ? 1 : 0),
            active);
        var changes = new[]
        {
            Change(10.00f, 0, true),
            Change(10.05f, 0x40000001, true),
            Change(10.10f, 0, false),
        };

        var normalized = ReplayDebuffSourceCorrectionPolicy.NormalizeForAnalysis(changes);

        var sourced = Assert.Single(normalized);
        Assert.Equal(0x40000001u, sourced.Status.SourceId);
        Assert.True(sourced.Active);
    }

    [Fact]
    public void NormalizeForAnalysis_PreservesSourceLessChangesOutsideTheCorrectionWindow()
    {
        var startedAt = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        ReplayDebuffSnapshot Change(float seconds, uint sourceId, bool active) => new(
            startedAt.AddSeconds(seconds),
            seconds,
            "member-1",
            "Nai",
            0,
            34,
            "SAM",
            new StatusSnapshot(9999, "Other debuff", 0, sourceId, 0, active ? 1 : 0),
            active);
        var changes = new[]
        {
            Change(10.00f, 0, true),
            Change(12.00f, 0x40000001, true),
            Change(13.00f, 0, false),
        };

        var normalized = ReplayDebuffSourceCorrectionPolicy.NormalizeForAnalysis(changes);

        Assert.Equal(changes, normalized);
    }
}
