using BetterDeaths.PartyCooldowns;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterDeaths.Tests;

public sealed class TemperaCooldownTrackerTests
{
    private static readonly PartyCooldownProfile Profile = new(34685, 22, 120, 1);
    private static TemperaEvent Added(uint status = 3686, double at = 0) =>
        new(TemperaEventKind.Applied, at) { StatusId = status, Remaining = 10, ServerFrame = 1 };
    private static TemperaEvent Lost(uint status = 3686, double at = 4) =>
        new(TemperaEventKind.Removed, at) { StatusId = status, Remaining = 6, ServerFrame = 2, Sequence = 42, Hp = 5000, HasHp = true };
    private static TemperaEvent Damage(double at = 3) => new(TemperaEventKind.Damage, at) { Sequence = 42 };
    private static TemperaEvent Landed(uint hp = 5000, double at = 4) =>
        new(TemperaEventKind.Landing, at) { Sequence = 42, Hp = hp, HasHp = true, ServerFrame = 2 };

    [Theory]
    [InlineData(3, 0, true)]
    [InlineData(5, 0, true)]
    [InlineData(6, 0, true)]
    [InlineData(74, 0, true)]
    [InlineData(3, 128, false)]
    [InlineData(5, 128, false)]
    [InlineData(4, 0, false)]
    [InlineData(7, 0, false)]
    public void OnlyDamageAppliedToTheShieldOwnerCanProveABreak(byte type, byte flags, bool expected) =>
        Assert.Equal(expected, TemperaCooldownTracker.IsTargetDamage(type, flags));

    [Fact]
    public void CapturedAnonymizedBreakExpiryAndConversionTracesReplayCorrectly()
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var cases = JsonSerializer.Deserialize<CapturedCase[]>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "TemperaCooldownCaptured.json")), options)!;
        Assert.Equal(4, cases.Length);
        foreach (var capture in cases)
        {
            var tracker = new TemperaCooldownTracker();
            var cooldowns = new PartyCooldownTracker();
            tracker.Cast(1, 0);
            cooldowns.Observe(1, Profile, 0, 1);
            foreach (var observation in capture.Events)
                tracker.Observe(1, observation);
            var now = capture.Events.Max(observation => observation.SeenAt) + .21;
            var reduced = tracker.TryReduce(1, now, out var reduction);
            Assert.True(reduced == (capture.ExpectedReduction > 0), capture.Name);
            if (reduced)
            {
                Assert.Equal(capture.ExpectedReduction, reduction.Seconds);
                Assert.True(cooldowns.Reduce(1, Profile, reduction, now));
                Assert.False(tracker.TryReduce(1, now + 1, out _));
            }
            Assert.False(tracker.IsUncertain(1));
            Assert.Equal(120 - capture.ExpectedReduction - now, cooldowns.Get(1, Profile, now).RemainingSeconds, 3);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonlethalDamageResultMayArriveBeforeOrAfterTheRemoval(bool removalFirst)
    {
        var tracker = Started();
        tracker.Observe(1, Damage());
        tracker.Observe(1, removalFirst ? Lost() : Landed());
        tracker.Observe(1, removalFirst ? Landed() : Lost());
        Assert.False(tracker.TryReduce(1, 4.1, out _));
        Assert.True(tracker.TryReduce(1, 4.21, out var reduction));
        Assert.Equal(60, reduction.Seconds);
    }

    [Fact]
    public void NativeRemovalInsideTheMatchedDamageResultDoesNotNeedASeparateLandingEvent()
    {
        var tracker = Started();
        tracker.Observe(1, Damage());
        tracker.Observe(1, Lost() with { Sequence = 42, HasHp = true, Hp = 5000 });
        Assert.True(tracker.TryReduce(1, 4.21, out _));
    }

    [Fact]
    public void ObjectLookupIsNeededOnlyForASettledEligibleRefund()
    {
        var tracker = Started();
        Assert.False(tracker.HasPendingReduction(1, 1));
        tracker.Observe(1, Damage());
        Assert.False(tracker.HasPendingReduction(1, 3.1));
        tracker.Observe(1, Lost());
        Assert.False(tracker.HasPendingReduction(1, 4.1));
        Assert.True(tracker.HasPendingReduction(1, 4.21));
        Assert.True(tracker.TryReduce(1, 4.21, out _));
        Assert.False(tracker.HasPendingReduction(1, 5));
    }

    [Fact]
    public void NoScheduledCheckIsNeededUntilAMatchedEarlyClearExists()
    {
        var tracker = Started();
        Assert.Equal(double.PositiveInfinity, tracker.GetNextReductionCheckAt(1));
        Assert.Equal(double.PositiveInfinity, tracker.GetNextReductionCheckAt(2));
        tracker.Observe(1, Damage());
        Assert.Equal(double.PositiveInfinity, tracker.GetNextReductionCheckAt(1));
        tracker.Observe(1, Lost());
        Assert.Equal(4.2, tracker.GetNextReductionCheckAt(1), 6);
        Assert.False(tracker.HasPendingReduction(1, 4.199));
        Assert.True(tracker.HasPendingReduction(1, 4.2));
        tracker.Observe(1, Lost(at: 4.1) with { HasHp = false, Sequence = 0 });
        Assert.Equal(4.2, tracker.GetNextReductionCheckAt(1), 6);
        Assert.True(tracker.TryReduce(1, 4.2, out _));
        Assert.Equal(double.PositiveInfinity, tracker.GetNextReductionCheckAt(1));
    }

    [Fact]
    public void ConfirmedCoatUseShowsItsRunningCooldownBeforeAnyShieldMessage()
    {
        var cooldowns = new PartyCooldownTracker();
        var tracker = new TemperaCooldownTracker();
        Assert.True(cooldowns.Observe(1, Profile, 10, 42));
        tracker.Cast(1, 10);
        var status = cooldowns.Get(1, Profile, 10);
        Assert.True(status.IsKnown);
        Assert.True(status.IsOnCooldown);
        Assert.Equal("120", PartyCooldownLabels.Timer(status));
        Assert.False(tracker.IsUncertain(1, 10));
        Assert.Equal(double.PositiveInfinity, tracker.GetNextReductionCheckAt(1));
    }

    [Fact]
    public void MatchedBreakKeepsTheKnownTimerVisibleDuringTheShortSettlementWait()
    {
        var tracker = Started();
        tracker.Observe(1, Damage());
        tracker.Observe(1, Lost());
        Assert.False(tracker.TryReduce(1, 4.1, out _));
        Assert.False(tracker.IsUncertain(1, 4.1));
        Assert.True(tracker.TryReduce(1, 4.21, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeShieldAtRemovalSuppliesItsIdentityAndRemainingTimeWhenEarlierUpdatesWereMissed(bool grassa)
    {
        var tracker = new TemperaCooldownTracker();
        tracker.Cast(1, 0);
        tracker.Observe(1, Damage());
        tracker.Observe(1, Lost(grassa ? 3687u : 3686u));
        Assert.True(tracker.TryReduce(1, 4.21, out var reduction));
        Assert.Equal(grassa ? 30 : 60, reduction.Seconds);
        Assert.False(tracker.TryReduce(1, 5, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissedApplicationCannotTurnExpiryOrUnknownRemovalIntoARefund(bool unknown)
    {
        var tracker = new TemperaCooldownTracker();
        tracker.Cast(1, 0);
        tracker.Observe(1, Damage());
        tracker.Observe(1, Lost() with { Remaining = unknown ? double.NaN : 0 });
        Assert.False(tracker.TryReduce(1, 4.21, out _));
        Assert.Equal(unknown, tracker.IsUncertain(1, 5));
    }

    [Fact]
    public void ADelayedDamageObservationCanScheduleAnAlreadyCapturedClear()
    {
        var tracker = Started();
        tracker.Observe(1, Lost());
        Assert.Equal(double.PositiveInfinity, tracker.GetNextReductionCheckAt(1));
        tracker.Observe(1, Damage());
        Assert.Equal(4.2, tracker.GetNextReductionCheckAt(1), 6);
        Assert.True(tracker.TryReduce(1, 4.21, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeathOrConversionCancelsAScheduledCoatRefund(bool conversion)
    {
        var tracker = Started();
        tracker.Observe(1, Damage());
        tracker.Observe(1, Lost());
        Assert.Equal(4.2, tracker.GetNextReductionCheckAt(1), 6);
        tracker.Observe(1, new(conversion ? TemperaEventKind.Conversion : TemperaEventKind.Death, 4.1));
        Assert.Equal(double.PositiveInfinity, tracker.GetNextReductionCheckAt(1));
        Assert.False(tracker.TryReduce(1, 4.21, out _));
    }

    [Fact]
    public void TwoMembersDeadlinesRemainIndependentAndResetClearsOldWork()
    {
        var tracker = Started();
        tracker.Observe(1, Damage());
        tracker.Observe(1, Lost());
        tracker.Cast(2, 0);
        tracker.Observe(2, Added());
        tracker.Observe(2, Damage(4));
        tracker.Observe(2, Lost(at: 5) with { Remaining = 5 });
        Assert.Equal(4.2, tracker.GetNextReductionCheckAt(1), 6);
        Assert.Equal(5.2, tracker.GetNextReductionCheckAt(2), 6);
        Assert.True(tracker.TryReduce(1, 4.21, out _));
        Assert.Equal(double.PositiveInfinity, tracker.GetNextReductionCheckAt(1));
        Assert.Equal(5.2, tracker.GetNextReductionCheckAt(2), 6);
        tracker.Cast(2, 5.1);
        Assert.Equal(double.PositiveInfinity, tracker.GetNextReductionCheckAt(2));
        tracker.Clear();
        Assert.Equal(double.PositiveInfinity, tracker.GetNextReductionCheckAt(1));
        Assert.Equal(double.PositiveInfinity, tracker.GetNextReductionCheckAt(2));
    }

    [Fact]
    public void ExpiryConversionAndDeathNeverBecomeObjectLookupCandidates()
    {
        foreach (var kind in new[] { TemperaEventKind.Death, TemperaEventKind.Conversion })
        {
            var tracker = Started();
            tracker.Observe(1, Damage());
            tracker.Observe(1, Lost());
            tracker.Observe(1, new(kind, 4.1));
            Assert.False(tracker.HasPendingReduction(1, 5));
        }
        var expired = Started();
        expired.Observe(1, Damage(9.5));
        expired.Observe(1, Lost(at: 10.1) with { Remaining = 0 });
        Assert.False(expired.HasPendingReduction(1, 11));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeShieldCallbacksDoNotDependOnTheDamageParsersPacketClock(bool converted)
    {
        var tracker = new TemperaCooldownTracker();
        tracker.Cast(1, 0);
        tracker.Observe(1, Added() with { ServerFrame = 0 });
        if (converted)
        {
            tracker.Observe(1, new(TemperaEventKind.Conversion, 1));
            tracker.Observe(1, Added(3687, 1) with { ServerFrame = 0 });
        }
        tracker.Observe(1, Damage());
        tracker.Observe(1, Lost(converted ? 3687u : 3686u) with { ServerFrame = 0 });
        Assert.True(tracker.TryReduce(1, 4.21, out var reduction));
        Assert.Equal(converted ? 30 : 60, reduction.Seconds);
        Assert.False(tracker.TryReduce(1, 5, out _));
    }

    [Theory]
    [InlineData(0, 5000, true)]
    [InlineData(42, 0, true)]
    [InlineData(42, 5000, false)]
    [InlineData(999, 5000, true)]
    public void MissingPacketClockNeverRelaxesNativeDamageCausality(uint sequence, uint hp, bool hasHp)
    {
        var tracker = new TemperaCooldownTracker();
        tracker.Cast(1, 0);
        tracker.Observe(1, Added() with { ServerFrame = 0 });
        tracker.Observe(1, Damage());
        tracker.Observe(1, Lost() with { ServerFrame = 0, Sequence = sequence, Hp = hp, HasHp = hasHp });
        Assert.False(tracker.TryReduce(1, 4.21, out _));
    }

    [Fact]
    public void ExpiryAndConversionRemainNonRefundingWithoutAPacketClock()
    {
        var expiry = new TemperaCooldownTracker();
        expiry.Cast(1, 0);
        expiry.Observe(1, Added() with { ServerFrame = 0 });
        expiry.Observe(1, Damage(9.5));
        expiry.Observe(1, Lost(at: 10.1) with { ServerFrame = 0, Remaining = 0 });
        Assert.False(expiry.TryReduce(1, 10.5, out _));
        Assert.False(expiry.IsUncertain(1, 11));

        var conversion = new TemperaCooldownTracker();
        conversion.Cast(1, 0);
        conversion.Observe(1, Added() with { ServerFrame = 0 });
        conversion.Observe(1, Damage());
        conversion.Observe(1, Lost() with { ServerFrame = 0 });
        conversion.Observe(1, new(TemperaEventKind.Conversion, 4));
        conversion.Observe(1, Added(3687, 4) with { ServerFrame = 0 });
        Assert.False(conversion.TryReduce(1, 4.21, out _));
    }

    [Theory]
    [InlineData(0, 42)]
    [InlineData(5000, 999)]
    public void CausalLethalOrNonDamageResultCannotBeOverruledByAnotherHitInTheSameFrame(uint hp, uint sequence)
    {
        var tracker = Started();
        tracker.Observe(1, Damage());
        tracker.Observe(1, Landed());
        tracker.Observe(1, Lost() with { Sequence = sequence, HasHp = true, Hp = hp });
        Assert.False(tracker.TryReduce(1, 4.21, out _));
    }

    [Fact]
    public void ExpiryCannotRefundEvenWithDamageLandingInTheSameFrame()
    {
        var tracker = Started();
        tracker.Observe(1, Damage(9.5));
        tracker.Observe(1, Landed(at: 10));
        tracker.Observe(1, Lost(at: 10) with { Remaining = 0 });
        Assert.False(tracker.TryReduce(1, 10.5, out _));
        Assert.False(tracker.IsUncertain(1));
    }

    [Fact]
    public void LocalExpiryIsAllowedToFinishTheShieldButCannotAuthoriseARefund()
    {
        var tracker = Started();
        tracker.Observe(1, Lost(at: 10.1) with { ServerFrame = 0, Remaining = 0 });
        Assert.False(tracker.TryReduce(1, 10.5, out _));
        Assert.False(tracker.IsUncertain(1, 11));
    }

    [Fact]
    public void MissingShieldEndIsUnknownRatherThanAnAssumedUnchangedCooldown()
    {
        var tracker = Started();
        Assert.False(tracker.IsUncertain(1, 5));
        Assert.True(tracker.IsUncertain(1, 11));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConversionCannotRefundCoatEvenIfDamageSharesTheFrame(bool conversionFirst)
    {
        var tracker = Started();
        tracker.Observe(1, Damage());
        tracker.Observe(1, Landed());
        if (conversionFirst)
            tracker.Observe(1, new(TemperaEventKind.Conversion, 4));
        tracker.Observe(1, Lost());
        tracker.Observe(1, Added(3687, 4));
        if (!conversionFirst)
            tracker.Observe(1, new(TemperaEventKind.Conversion, 4));
        Assert.False(tracker.TryReduce(1, 4.21, out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeathDoesNotRefundRegardlessOfNotificationOrdering(bool deathFirst)
    {
        var tracker = Started();
        tracker.Observe(1, Damage());
        tracker.Observe(1, Landed(0));
        if (deathFirst)
            tracker.Observe(1, new(TemperaEventKind.Death, 4));
        tracker.Observe(1, Lost());
        if (!deathFirst)
            tracker.Observe(1, new(TemperaEventKind.Death, 4.1));
        Assert.False(tracker.TryReduce(1, 4.21, out _));
        Assert.False(tracker.IsUncertain(1));
    }

    [Fact]
    public void DelayedDeathVetoesAnOtherwiseNonlethalCandidateBeforeItIsCommitted()
    {
        var tracker = Started();
        tracker.Observe(1, Damage());
        tracker.Observe(1, Landed());
        tracker.Observe(1, Lost());
        tracker.Observe(1, new(TemperaEventKind.Death, 4.1));
        Assert.False(tracker.TryReduce(1, 4.21, out _));
    }

    [Fact]
    public void NearbyDamageAloneAndMismatchedSequencesAreNotBreakProof()
    {
        var tracker = Started();
        tracker.Observe(1, Damage());
        tracker.Observe(1, Lost() with { Sequence = 999 });
        tracker.Observe(1, Landed() with { Sequence = 999 });
        Assert.False(tracker.TryReduce(1, 4.21, out _));
        Assert.True(tracker.IsUncertain(1));
    }

    [Fact]
    public void SeparateLandingCannotSupplyMissingNativeRemovalContextRegardlessOfPacketClock()
    {
        foreach (var frame in new long[] { 0, 3 })
        {
            var tracker = Started();
            tracker.Observe(1, Damage());
            tracker.Observe(1, Lost() with { ServerFrame = frame, HasHp = false, Sequence = 0 });
            tracker.Observe(1, Landed());
            Assert.False(tracker.TryReduce(1, 4.21, out _));
            Assert.True(tracker.IsUncertain(1));
        }
    }

    [Fact]
    public void IdenticalTimestampWithoutNativeCausalityIsNotEnoughForARefund()
    {
        var tracker = Started();
        tracker.Observe(1, Damage());
        tracker.Observe(1, Landed());
        tracker.Observe(1, Lost() with { HasHp = false, Sequence = 0 });
        Assert.False(tracker.TryReduce(1, 4.21, out _));
        Assert.True(tracker.IsUncertain(1));
    }

    [Fact]
    public void NearExpiryAndMissingRemovalTimingRemainUncertainInsteadOfInventingARefund()
    {
        foreach (var remaining in new[] { .05, double.NaN })
        {
            var tracker = Started();
            tracker.Observe(1, Damage(9.5));
            tracker.Observe(1, Landed(at: 9.98));
            tracker.Observe(1, Lost(at: 9.98) with { Remaining = remaining });
            Assert.False(tracker.TryReduce(1, 10.5, out _));
            Assert.True(tracker.IsUncertain(1));
        }
    }

    [Fact]
    public void RecipientAndDifferentCasterShieldsCannotRefundTheOwner()
    {
        Assert.False(TemperaCooldownTracker.IsOwnShield(2, 1, 3687));
        Assert.False(TemperaCooldownTracker.IsOwnShield(1, 2, 3687));
        Assert.False(TemperaCooldownTracker.IsOwnShield(0, 0, 3686));
        Assert.False(TemperaCooldownTracker.IsOwnShield(0xE0000000, 0xE0000000, 3686));
        Assert.True(TemperaCooldownTracker.IsOwnShield(1, 1, 3687));
        var tracker = Started();
        tracker.Observe(2, Added());
        tracker.Observe(2, Damage());
        tracker.Observe(2, Landed());
        tracker.Observe(2, Lost());
        Assert.False(tracker.TryReduce(1, 4.21, out _));
    }

    [Fact]
    public void DuplicateLossesAndLandingsRefundOnlyOnce()
    {
        var tracker = Started();
        tracker.Observe(1, Damage());
        for (var index = 0; index < 8; index++)
        {
            tracker.Observe(1, Lost());
            tracker.Observe(1, Landed());
        }
        Assert.True(tracker.TryReduce(1, 4.21, out _));
        tracker.Observe(1, Lost());
        Assert.False(tracker.TryReduce(1, 5, out _));
    }

    [Fact]
    public void LaterOrdinaryLossNoticeDoesNotEraseExplicitNativeClearEvidence()
    {
        var tracker = Started();
        tracker.Observe(1, Damage());
        tracker.Observe(1, Lost());
        tracker.Observe(1, Lost(at: 4.05) with { HasHp = false, Sequence = 0, Remaining = double.NaN });
        Assert.True(tracker.TryReduce(1, 4.21, out _));
    }

    [Fact]
    public void ExplicitNativeClearCanUpgradeAnEarlierOrdinaryLossNotice()
    {
        var tracker = Started();
        tracker.Observe(1, Damage());
        tracker.Observe(1, Lost(at: 3.99) with { HasHp = false, Sequence = 0 });
        tracker.Observe(1, Lost());
        Assert.True(tracker.TryReduce(1, 4.21, out _));
    }

    [Fact]
    public void LethalDuplicateStillVetoesAPreviouslyNonlethalClear()
    {
        var tracker = Started();
        tracker.Observe(1, Damage());
        tracker.Observe(1, Lost());
        tracker.Observe(1, Lost(at: 4.1) with { Hp = 0 });
        Assert.False(tracker.TryReduce(1, 4.31, out _));
    }

    [Fact]
    public void ANewCastOrDutyResetInvalidatesPendingOldRefunds()
    {
        var tracker = Started();
        tracker.Observe(1, Damage());
        tracker.Observe(1, Landed());
        tracker.Observe(1, Lost());
        tracker.Cast(1, 5);
        Assert.False(tracker.TryReduce(1, 5.5, out _));
        tracker.Clear();
        Assert.False(tracker.TryReduce(1, 6, out _));
        Assert.False(tracker.IsUncertain(1));
        var cooldowns = new PartyCooldownTracker();
        cooldowns.Observe(1, Profile, 10, 1);
        Assert.False(cooldowns.Reduce(1, Profile, new(0, 60), 11));
        cooldowns.ConfirmReady(1, Profile, 12);
        Assert.False(cooldowns.Reduce(1, Profile, new(10, 60), 13));
    }

    [Fact]
    public void MemberDepartureClearsOnlyThatMembersShieldEvidence()
    {
        var tracker = Started();
        tracker.Cast(2, 0);
        tracker.Observe(2, Added());
        tracker.RetainMembers(new HashSet<ulong> { 2 });
        Assert.False(tracker.IsUncertain(1));
        tracker.Reset(2);
        Assert.False(tracker.IsUncertain(2));
    }

    private static TemperaCooldownTracker Started()
    {
        var tracker = new TemperaCooldownTracker();
        tracker.Cast(1, 0);
        tracker.Observe(1, Added());
        return tracker;
    }

    private sealed record CapturedCase(string Name, string SourceWindow, double ExpectedReduction, TemperaEvent[] Events);
}
