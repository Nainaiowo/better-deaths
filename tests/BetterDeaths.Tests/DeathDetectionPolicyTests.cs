namespace BetterDeaths;

public sealed class DeathDetectionPolicyTests
{
    private static readonly DateTime FirstSeenAtUtc = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan ConfirmationDelay = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan CandidateRetention = TimeSpan.FromSeconds(5);

    [Fact]
    public void MissingWorldObjectAndZeroPartyHpIsUnknown()
    {
        var observation = DeathDetectionPolicy.ClassifyPolledState(
            hasWorldObject: false,
            worldObjectIsDead: false,
            currentHp: 0,
            maxHp: 200_000);

        Assert.Equal(PlayerDeathObservation.Unknown, observation);
    }

    [Fact]
    public void MissingWorldObjectRemainsUnknownEvenWithPositivePartyHp()
    {
        var observation = DeathDetectionPolicy.ClassifyPolledState(
            hasWorldObject: false,
            worldObjectIsDead: false,
            currentHp: 100_000,
            maxHp: 200_000);

        Assert.Equal(PlayerDeathObservation.Unknown, observation);
    }

    [Fact]
    public void WorldObjectDeathTakesPrecedenceOverStalePositiveHp()
    {
        var observation = DeathDetectionPolicy.ClassifyPolledState(
            hasWorldObject: true,
            worldObjectIsDead: true,
            currentHp: 100_000,
            maxHp: 200_000);

        Assert.Equal(PlayerDeathObservation.WorldObjectDead, observation);
    }

    [Fact]
    public void PresentWorldObjectWithPositiveHpConfirmsAlive()
    {
        var observation = DeathDetectionPolicy.ClassifyPolledState(
            hasWorldObject: true,
            worldObjectIsDead: false,
            currentHp: 100_000,
            maxHp: 200_000);

        Assert.Equal(PlayerDeathObservation.Alive, observation);
    }

    [Theory]
    [InlineData(true, true, 0, 0, 200_000, true)]
    [InlineData(false, true, 0, 0, 200_000, false)]
    [InlineData(true, false, 0, 0, 200_000, false)]
    [InlineData(true, true, 1, 0, 200_000, false)]
    [InlineData(true, true, 0, 1, 200_000, false)]
    [InlineData(true, true, 0, 0, 0, false)]
    public void LethalResultRequiresAliveHistoryAndMatchedDamage(
        bool wasKnownAlive,
        bool hasMatchedDamage,
        uint currentHp,
        uint shieldHp,
        uint maxHp,
        bool expected)
    {
        Assert.Equal(
            expected,
            DeathDetectionPolicy.IsConfirmedLethalDamageResult(
                wasKnownAlive,
                hasMatchedDamage,
                currentHp,
                shieldHp,
                maxHp));
    }

    [Theory]
    [InlineData(1, 200_000, true)]
    [InlineData(0, 200_000, false)]
    [InlineData(1, 0, false)]
    public void PositiveEffectResultConfirmsAlive(uint currentHp, uint maxHp, bool expected)
    {
        Assert.Equal(expected, DeathDetectionPolicy.IsConfirmedAliveResult(currentHp, maxHp));
    }

    [Theory]
    [InlineData(2.499, false)]
    [InlineData(2.5, true)]
    [InlineData(3.0, true)]
    public void WorldObjectFallbackWaitsForConfirmationWindow(double elapsedSeconds, bool expected)
    {
        Assert.Equal(
            expected,
            DeathDetectionPolicy.ShouldConfirmPendingWorldObjectDeath(
                wasKnownAlive: true,
                worldObjectIsStillDead: true,
                FirstSeenAtUtc,
                FirstSeenAtUtc.AddSeconds(elapsedSeconds),
                ConfirmationDelay));
    }

    [Fact]
    public void WorldObjectFallbackRejectsUnknownPriorState()
    {
        Assert.False(DeathDetectionPolicy.ShouldConfirmPendingWorldObjectDeath(
            wasKnownAlive: false,
            worldObjectIsStillDead: true,
            FirstSeenAtUtc,
            FirstSeenAtUtc.AddSeconds(3),
            ConfirmationDelay));
    }

    [Theory]
    [InlineData(true, true, (int)PlayerDeathObservation.WorldObjectDead, 1.0, true)]
    [InlineData(true, true, (int)PlayerDeathObservation.WorldObjectDead, 5.0, true)]
    [InlineData(true, true, (int)PlayerDeathObservation.WorldObjectDead, 5.001, false)]
    [InlineData(false, true, (int)PlayerDeathObservation.WorldObjectDead, 1.0, false)]
    [InlineData(true, false, (int)PlayerDeathObservation.WorldObjectDead, 1.0, false)]
    [InlineData(true, true, (int)PlayerDeathObservation.Alive, 1.0, false)]
    [InlineData(true, true, (int)PlayerDeathObservation.Unknown, 1.0, false)]
    public void PendingDeathRequiresSameObservableDeadActor(
        bool wasKnownAlive, bool isSameActor, int observation,
        double elapsedSeconds, bool expected)
    {
        Assert.Equal(expected, DeathDetectionPolicy.ShouldRetainPendingWorldObjectDeath(
            wasKnownAlive, isSameActor, (PlayerDeathObservation)observation, FirstSeenAtUtc,
            FirstSeenAtUtc.AddSeconds(elapsedSeconds), CandidateRetention));
    }

    [Theory]
    [InlineData(false, false, 0, 200_000)] // Player disappears but remains in the party list.
    [InlineData(false, true, 0, 200_000)] // Missing actor with a stale dead flag.
    [InlineData(false, false, 0, 0)] // Player is no longer in the tracked members.
    [InlineData(true, false, 0, 200_000)] // Zero HP alone is not death evidence.
    [InlineData(true, false, 100_000, 200_000)] // Fresh reset snapshot is alive.
    public void MissingUnknownOrAliveSnapshotCannotConfirmPendingDeath(
        bool hasWorldObject, bool worldObjectIsDead, uint currentHp, uint maxHp)
    {
        var observation = DeathDetectionPolicy.ClassifyPolledState(
            hasWorldObject, worldObjectIsDead, currentHp, maxHp);

        Assert.False(DeathDetectionPolicy.ShouldRetainPendingWorldObjectDeath(
            wasKnownAlive: true, isSameActor: true, observation, FirstSeenAtUtc,
            FirstSeenAtUtc.AddSeconds(3), CandidateRetention));
    }

    [Fact]
    public void DisappearingAndReturningDoesNotCompleteTheOldConfirmationWindow()
    {
        Assert.False(DeathDetectionPolicy.ShouldRetainPendingWorldObjectDeath(
            wasKnownAlive: true, isSameActor: true, PlayerDeathObservation.Unknown,
            FirstSeenAtUtc, FirstSeenAtUtc.AddSeconds(1), CandidateRetention));

        var returnedAtUtc = FirstSeenAtUtc.AddSeconds(2);
        Assert.False(DeathDetectionPolicy.ShouldConfirmPendingWorldObjectDeath(
            wasKnownAlive: true, worldObjectIsStillDead: true, returnedAtUtc,
            FirstSeenAtUtc.AddSeconds(3), ConfirmationDelay));
        Assert.True(DeathDetectionPolicy.ShouldConfirmPendingWorldObjectDeath(
            wasKnownAlive: true, worldObjectIsStillDead: true, returnedAtUtc,
            returnedAtUtc + ConfirmationDelay, ConfirmationDelay));
    }

    [Fact]
    public void FreshDeadActorCanStillBeRetainedForAnImmediateDutyReset()
    {
        var observation = DeathDetectionPolicy.ClassifyPolledState(
            hasWorldObject: true, worldObjectIsDead: true, currentHp: 100_000, maxHp: 200_000);

        Assert.True(DeathDetectionPolicy.ShouldRetainPendingWorldObjectDeath(
            wasKnownAlive: true, isSameActor: true, observation, FirstSeenAtUtc,
            FirstSeenAtUtc.AddMilliseconds(100), CandidateRetention));
    }

    [Fact]
    public void DiscardingAnUnobservableCandidateDoesNotRejectConfirmedLethalDamage()
    {
        Assert.False(DeathDetectionPolicy.ShouldRetainPendingWorldObjectDeath(
            wasKnownAlive: true, isSameActor: true, PlayerDeathObservation.Unknown,
            FirstSeenAtUtc, FirstSeenAtUtc.AddSeconds(1), CandidateRetention));

        Assert.True(DeathDetectionPolicy.IsConfirmedLethalDamageResult(
            wasKnownAlive: true, hasMatchedDamage: true, resultCurrentHp: 0,
            resultShieldHp: 0, resultMaxHp: 200_000));
    }

    [Theory]
    [InlineData(5.0, false)]
    [InlineData(5.001, true)]
    public void PendingCandidateExpiresOnlyAfterRetention(double elapsedSeconds, bool expected)
    {
        Assert.Equal(
            expected,
            DeathDetectionPolicy.IsPendingCandidateExpired(
                FirstSeenAtUtc,
                FirstSeenAtUtc.AddSeconds(elapsedSeconds),
                TimeSpan.FromSeconds(5)));
    }
}
