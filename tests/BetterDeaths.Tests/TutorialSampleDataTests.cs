using BetterDeaths;
using BetterDeaths.WtfDig;

namespace BetterDeaths.Tests;

public sealed class TutorialSampleDataTests
{
    [Fact]
    public async Task BundledPullSupportsReviewReplayAndAnalyzer()
    {
        var pull = await TutorialSampleData.LoadPullAsync();

        Assert.Equal(0, pull.PullNumber);
        Assert.Equal("tutorial", pull.CapturedPluginVersion);
        Assert.True(pull.Deaths.Count >= 8);
        Assert.True(pull.ReplayPositions.Count > 1000);
        Assert.NotEmpty(pull.ReplayMechanics);
        Assert.NotEmpty(pull.ReplayAnalyzerEvents);
        Assert.NotEmpty(pull.ReplayWorldMarkers);
        Assert.NotEmpty(pull.ReplayMitigations);
        Assert.NotEmpty(pull.ReplayDebuffs);
        Assert.Contains(pull.ReplayPositions, position => position.PullElapsedSeconds < 30);
        Assert.Contains(pull.ReplayPositions, position => position.PullElapsedSeconds > pull.PullElapsedSeconds - 30);
        Assert.All(pull.Deaths, death => Assert.StartsWith("Player ", death.MemberName));
        Assert.All(pull.ReplayPositions.Where(position => position.ActorKind == ReplayActorKind.Player),
            position => Assert.StartsWith("Player ", position.ActorName));
        Assert.All(pull.ReplayMarkers.Where(marker => marker.ActorKind == ReplayActorKind.Player),
            marker => Assert.StartsWith("Player ", marker.ActorName));
        Assert.All(pull.ReplayMitigations, mitigation => Assert.StartsWith("Player ", mitigation.MemberName));
        Assert.All(pull.ReplayDebuffs, debuff => Assert.StartsWith("Player ", debuff.MemberName));

        var analyzer = LocalPullEventSource.Create(pull);
        Assert.Contains(analyzer.AnalyzerAvailability.Values,
            availability => availability.Quality != WtfDigLocalDataQuality.Unavailable);
        Assert.NotEqual(WtfDigLocalDataQuality.Unavailable, analyzer.AnalyzerAvailability["forsaken"].Quality);
        var forsaken = await new ForsakenAnalyzer(analyzer)
            .AnalyzeAsync(analyzer.Report, analyzer.Fight, CancellationToken.None);
        Assert.NotEmpty(forsaken.Resolutions);
        Assert.NotEqual(WtfDigLocalDataQuality.Unavailable, analyzer.AnalyzerAvailability["black-hole"].Quality);
        var blackHole = await new BlackHoleAnalyzer(analyzer)
            .AnalyzeAsync(analyzer.Report, analyzer.Fight, CancellationToken.None);
        Assert.NotEmpty(blackHole.Tethers);

        var summary = TutorialSampleData.CreatePullSummary(pull);
        Assert.True(TutorialSampleData.IsTutorialSummary(summary));
        Assert.Equal(pull.Deaths.Count, summary.DeathCount);
    }

    [Fact]
    public async Task BundledMeterHasRedactedPlayerResults()
    {
        var meter = await TutorialSampleData.LoadMeterAsync();
        var pull = await TutorialSampleData.LoadPullAsync();

        Assert.True(meter.Sources.Count >= 8);
        Assert.True(meter.EffectiveMeterDamage > 0);
        Assert.All(meter.Sources, source =>
        {
            Assert.True(source.Source.IsPlayer);
            Assert.StartsWith("Player ", source.Source.Name);
            Assert.Contains(pull.Deaths, death => death.MemberName == source.Source.Name);
        });
    }
}
