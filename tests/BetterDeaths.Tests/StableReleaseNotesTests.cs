namespace BetterDeaths.Tests;

public sealed class StableReleaseNotesTests
{
    [Fact]
    public void EmbeddedStableHistoryIsNewestFirstAndHasDisplayableText()
    {
        var entries = StableReleaseNotes.History;
        var versions = entries.Select(entry => Version.Parse(entry.Version)).ToArray();
        Assert.NotEmpty(versions);
        Assert.Equal(versions.Distinct().OrderDescending(), versions);
        Assert.All(entries, entry =>
        {
            Assert.NotEmpty(entry.Sections);
            Assert.All(entry.Sections, section =>
            {
                Assert.False(string.IsNullOrWhiteSpace(section.Title));
                Assert.NotEmpty(section.Bullets);
                Assert.All(section.Bullets, bullet => Assert.False(string.IsNullOrWhiteSpace(bullet.Text)));
            });
        });
        var iconRelease = Assert.Single(entries, entry => entry.Version == "1.0.0.73");
        Assert.Contains(iconRelease.Sections.SelectMany(section => section.Bullets),
            bullet => bullet.Highlight && bullet.Text.Contains("1,440"));
    }
}
