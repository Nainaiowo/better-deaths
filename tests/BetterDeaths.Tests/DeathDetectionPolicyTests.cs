namespace BetterDeaths;

public sealed class DeathDetectionPolicyTests
{
    private static readonly DateTime FirstSeenAtUtc = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan ConfirmationDelay = TimeSpan.FromMilliseconds(2500);

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
