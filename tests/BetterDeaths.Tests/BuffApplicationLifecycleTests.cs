namespace BetterDeaths.Tests;

using BetterDeaths.DamageParsing;

public sealed class BuffApplicationLifecycleTests
{
    private static readonly DateTime Start = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DamageActorIdentity Player = new(0x1001, "Recipient", 0, "", true, 41);
    private static readonly DamageActorIdentity Provider = new(0x1002, "Provider", 0, "", true, 33);
    private static readonly DamageActorIdentity Enemy = new(0x40000001, "Enemy", 0, "", false, 0);

    [Theory]
    [InlineData(3887u, 6)]
    [InlineData(3889u, 3)]
    [InlineData(1878u, 6)]
    [InlineData(2964u, 2)]
    [InlineData(1822u, 4)]
    [InlineData(2105u, 2)]
    public void BothTrackersRetainApplicationStrengthThroughIncompleteObservations(uint status, ushort strength)
    {
        foreach (var confirmed in new[] { false, true })
        {
            var tracker = new RaidBuffTracker(confirmed);
            tracker.Observe(Buff(0, status, strength) with
            { ActionId = 100, DurationSeconds = 0, ObservationKind = DamageStatusObservationKind.Announcement, ApplicationSequence = 1 });
            Assert.Empty(Active(tracker, .5));
            tracker.Observe(Buff(1, status) with
            { ObservationKind = DamageStatusObservationKind.Landing, ApplicationSequence = 1 });
            Snapshot(tracker, 2, status, 13);
            tracker.Observe(Buff(3, status) with { DurationSeconds = 0, ObservationKind = DamageStatusObservationKind.Observation });
            var buff = Assert.Single(Active(tracker, 4));
            Assert.Equal(strength, buff.Parameter);
            Assert.Equal(11, buff.RemainingTime);
            Assert.Empty(Active(tracker, 16));
        }
    }

    [Theory]
    [InlineData(3887u)]
    [InlineData(3889u)]
    [InlineData(2964u)]
    [InlineData(1822u)]
    public void EndedApplicationCannotSupplyStrengthToANewOne(uint status)
    {
        foreach (var removal in new[] { false, true })
        {
            var tracker = new RaidBuffTracker(true);
            tracker.Observe(Buff(0, status, 6));
            if (removal) tracker.Observe(Buff(1, status) with { IsRemoval = true });
            var at = removal ? 2 : 16;
            tracker.Observe(Buff(at, status));
            Assert.Equal(0, Assert.Single(Active(tracker, at + 1)).Parameter);
        }
    }

    [Fact]
    public void NewSequenceReplacesStrengthButDuplicateLandingPreservesIt()
    {
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0, 2964, 6) with { ObservationKind = DamageStatusObservationKind.Landing, ApplicationSequence = 1 });
        Snapshot(tracker, 1, 2964, 14);
        tracker.Observe(Buff(1, 2964) with
        { DurationSeconds = 14, ObservationKind = DamageStatusObservationKind.Landing, ApplicationSequence = 1 });
        Assert.Equal(6, Assert.Single(Active(tracker, 2)).Parameter);
        tracker.Observe(Buff(3, 2964) with { ObservationKind = DamageStatusObservationKind.Landing, ApplicationSequence = 2 });
        Assert.Equal(0, Assert.Single(Active(tracker, 4)).Parameter);
        Assert.Equal(6, Assert.Single(Active(tracker, 2)).Parameter);
    }

    [Fact]
    public void DelayedIncompleteObservationPreservesTheHistoricalApplicationsStrength()
    {
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0, 3887, 6));
        Snapshot(tracker, 2, 3887, 13);
        Snapshot(tracker, 1, 3887, 14);
        Assert.Equal(6, Assert.Single(Active(tracker, 1.5)).Parameter);
        Assert.Equal(6, Assert.Single(Active(tracker, 3)).Parameter);
    }

    [Fact]
    public void PrePullCleanupPreservesConfirmedBuffsUsedByDotSnapshots()
    {
        var module = new DamageParsingModule();
        module.SetCombatActive(false, Start);
        module.ObserveStatus(Buff(0, 2964, 6));
        module.SetCombatActive(true, Start.AddSeconds(3));
        module.Process(new(1, Start.AddSeconds(4), 1, Player, 100, "Hit",
            [new(0, Enemy, [new(0, 3, 0, 0, 0, 0, 1060)])]) { DirectPotency = 100, CanCalibratePotency = true });
        var dot = new DamageStatusApplication(Enemy, Player, 50000, "DoT", 0, 100, "Application",
            Start.AddSeconds(5), 0, true, false, false) { PeriodicPotency = 20 };
        module.ObserveStatus(dot);
        module.ObserveStatus(dot with { SeenAtUtc = Start.AddSeconds(6), ActionId = 0, DurationSeconds = 30 });
        module.ProcessPeriodicTick(new(100, Start.AddSeconds(7), Enemy, 0, "", 0, 600, null));
        var first = Assert.Single(module.FlushPendingPeriodicTicks(Start.AddSeconds(7), true));
        Assert.Equal(1.06, first.PeriodicCompatibilityEstimate!.Inputs!.DamageMultiplier, 6);
        module.ObserveStatus(Buff(8, 2964) with { IsRemoval = true });
        module.ProcessPeriodicTick(new(101, Start.AddSeconds(10), Enemy, 0, "", 0, 600, null));
        var later = Assert.Single(module.FlushPendingPeriodicTicks(Start.AddSeconds(10), true));
        Assert.Equal(first.PeriodicCompatibilityEstimate, later.PeriodicCompatibilityEstimate);
    }

    [Fact]
    public void ObservationCannotConsumeOrActivatePendingReplacement()
    {
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0, 2964, 2));
        tracker.Observe(Buff(1, 2964, 6) with
        { ActionId = 100, DurationSeconds = 0, ObservationKind = DamageStatusObservationKind.Announcement, ApplicationSequence = 2 });
        tracker.Observe(Buff(2, 2964) with { DurationSeconds = 13, ObservationKind = DamageStatusObservationKind.Observation });
        Assert.Equal(2, Assert.Single(Active(tracker, 2.5)).Parameter);
        tracker.Observe(Buff(3, 2964) with { ObservationKind = DamageStatusObservationKind.Landing, ApplicationSequence = 2 });
        Assert.Equal(6, Assert.Single(Active(tracker, 4)).Parameter);
    }

    [Fact]
    public void MissingFieldOnDuplicateAnnouncementDoesNotEraseItsKnownStrength()
    {
        var tracker = new RaidBuffTracker(true);
        var announcement = Buff(0, 3887, 6) with
        { ActionId = 100, DurationSeconds = 0, ObservationKind = DamageStatusObservationKind.Announcement, ApplicationSequence = 1 };
        tracker.Observe(announcement);
        tracker.Observe(announcement with { SeenAtUtc = Start.AddSeconds(.1), Parameter = 0 });
        tracker.Observe(Buff(1, 3887) with { ObservationKind = DamageStatusObservationKind.Landing, ApplicationSequence = 1 });
        Assert.Equal(6, Assert.Single(Active(tracker, 2)).Parameter);
    }

    [Fact]
    public void MismatchedLandingCannotConsumeAnotherSequencesAnnouncement()
    {
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0, 2964, 6) with
        { ActionId = 100, DurationSeconds = 0, ObservationKind = DamageStatusObservationKind.Announcement, ApplicationSequence = 2 });
        tracker.Observe(Buff(1, 2964) with { ObservationKind = DamageStatusObservationKind.Landing, ApplicationSequence = 1 });
        Assert.Equal(0, Assert.Single(Active(tracker, 1.5)).Parameter);
        tracker.Observe(Buff(2, 2964) with { ObservationKind = DamageStatusObservationKind.Landing, ApplicationSequence = 2 });
        Assert.Equal(6, Assert.Single(Active(tracker, 3)).Parameter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void MissingDurationNeitherRestartsNorInventsAnApplication(float duration)
    {
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0, 3887, 6) with { DurationSeconds = duration });
        Assert.Empty(Active(tracker, 0));
        tracker.Observe(Buff(1, 3887, 6));
        tracker.Observe(Buff(14, 3887) with { DurationSeconds = duration });
        Assert.Equal(1, Assert.Single(Active(tracker, 15)).RemainingTime);
        Assert.Empty(Active(tracker, 16));
    }

    [Fact]
    public void ExplicitZeroIsNotAnOmittedParameter()
    {
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0, 3887, 6));
        tracker.Observe(Buff(1, 3887) with { HasParameter = true, DurationSeconds = 14 });
        Assert.Equal(0, Assert.Single(Active(tracker, 2)).Parameter);
    }

    [Theory]
    [InlineData(3887u)]
    [InlineData(3889u)]
    [InlineData(1822u)]
    public void SignedWireDurationsDecodeBeforeLandingAndSnapshotTracking(uint status)
    {
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0, status, 6) with
        { ActionId = 100, DurationSeconds = 0, ObservationKind = DamageStatusObservationKind.Announcement, ApplicationSequence = 1 });
        tracker.Observe(Buff(1, status) with
        { DurationSeconds = DamageStatusTiming.DecodeRemaining(-15), ObservationKind = DamageStatusObservationKind.Landing, ApplicationSequence = 1 });
        Snapshot(tracker, 2, status, DamageStatusTiming.DecodeRemaining(-14));
        var buff = Assert.Single(Active(tracker, 3));
        Assert.Equal(6, buff.Parameter);
        Assert.Equal(13, buff.RemainingTime);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ListedBuffWithUnavailableDurationDoesNotMeanAuthoritativeAbsence(float duration)
    {
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0, 3887, 6));
        Snapshot(tracker, 1, 3887, duration);
        Assert.Equal(13, Assert.Single(Active(tracker, 2)).RemainingTime);
        Assert.Empty(Active(tracker, 15));
    }

    [Fact]
    public void UnknownProviderDoesNotRefreshSeveralApplications()
    {
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0, 3887, 6));
        tracker.Observe(Buff(0, 3887, 3) with { Source = Provider with { EntityId = 0x1003 } });
        tracker.Observe(Buff(14, 3887) with { Source = Provider with { EntityId = 0 } });
        Assert.Empty(Active(tracker, 16));
    }

    [Fact]
    public void ExplicitRefreshCannotReviveARemovedApplication()
    {
        var tracker = new RaidBuffTracker(true);
        tracker.Observe(Buff(0, 3887, 6));
        tracker.Observe(Buff(1, 3887) with { IsRemoval = true });
        tracker.Refresh(Player.EntityId, 3887, Start.AddSeconds(2));
        Assert.Empty(Active(tracker, 3));
    }

    [Theory]
    [InlineData(3887u)]
    [InlineData(3889u)]
    [InlineData(2964u)]
    public void PrePullStagingCleanupDoesNotEraseActiveBuffs(uint status)
    {
        var module = new DamageParsingModule();
        module.SetCombatActive(false, Start);
        module.ObserveStatus(Buff(0, status, 6));
        module.SetCombatActive(true, Start.AddSeconds(3));
        var hit = Assert.Single(module.Process(new(1, Start.AddSeconds(4), 1, Player, 100, "Hit",
            [new(0, Enemy, [new(0, 3, 0, 0, 0, 0, 1000)])])));
        var buff = Assert.Single(hit.SourceStatuses);
        Assert.Equal(6, buff.Parameter);
        Assert.Equal(11, buff.RemainingTime);
    }

    [Theory]
    [InlineData(3887u, 19u, .06)]
    [InlineData(3887u, 41u, .06)]
    [InlineData(3887u, 24u, .03)]
    [InlineData(3887u, 42u, .03)]
    [InlineData(3889u, 19u, .03)]
    [InlineData(3889u, 41u, .03)]
    [InlineData(3889u, 24u, .06)]
    [InlineData(3889u, 42u, .06)]
    public void AstrologianCapturedStrengthSurvivesRegardlessOfRecipientRole(uint status, uint job, double strength)
    {
        Assert.Equal(strength, Assert.Single(RaidBuffPolicy.GetEffects(new DamageStatusSnapshot(status, Provider, 0, 15)
            { AppliedParameter = (byte)Math.Round(strength * 100) }, false,
            Player with { ClassJobId = job })).Amount);
    }

    private static DamageStatusApplication Buff(double at, uint status, ushort parameter = 0) =>
        new(Player, Provider, status, "Buff", 0, 0, "", Start.AddSeconds(at), 15, false, false, false) { Parameter = parameter };

    private static IReadOnlyList<DamageStatusSnapshot> Active(RaidBuffTracker tracker, double at) =>
        tracker.ApplyConfirmed(Buff(at, 3887) with { Source = Player }).SourceStatuses;

    private static void Snapshot(RaidBuffTracker tracker, double at, uint status, float duration) =>
        tracker.ObserveSnapshots(Buff(at, status) with { Source = Player, HasSourceStatusSnapshot = true,
            SourceStatuses = [new(status, Provider, 0, duration)] });
}
