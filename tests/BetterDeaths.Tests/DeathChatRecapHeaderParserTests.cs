using BetterDeaths;

namespace BetterDeaths.Tests;

public sealed class DeathChatRecapHeaderParserTests
{
    [Theory]
    [InlineData("[Better Deaths] Recap: 02:41 Nai La (MCH)", 161, "Nai La", "MCH")]
    [InlineData("Recap: 10:07 DPS 3 (VPR)", 607, "DPS 3", "VPR")]
    public void TryParse_ReadsNewRecapHeader(
        string text,
        int expectedSeconds,
        string expectedName,
        string expectedJob)
    {
        var parsed = DeathChatRecapHeaderParser.TryParse(text, out var header);

        Assert.True(parsed);
        Assert.Equal(expectedSeconds, header.ElapsedSeconds);
        Assert.Equal(expectedName, header.MemberName);
        Assert.Equal(expectedJob, header.ClassJobName);
    }

    [Theory]
    [InlineData("[Better Deaths] Recap: 02:41 Nai La (MCH): 132,732 damage.")]
    [InlineData("Fatal: 132,732 damage. HP: 274,123 (100%). Overkill: 0.")]
    [InlineData("Recap: 02:99 Nai La (MCH)")]
    [InlineData("Recap: Nai La (MCH)")]
    public void TryParse_RejectsNonHeaderLines(string text)
    {
        Assert.False(DeathChatRecapHeaderParser.TryParse(text, out _));
    }

    [Fact]
    public void TryCombineFatalLine_ReconstructsLegacyPostForExactMatching()
    {
        var header = new DeathChatRecapHeader(161, "Nai La", "MCH");

        var combined = DeathChatRecapHeaderParser.TryCombineFatalLine(
            header,
            "Fatal: 132,732 damage. HP: 274,123 (100%). Overkill: 0.",
            out var post);

        Assert.True(combined);
        Assert.Equal(
            "Recap: 02:41 Nai La (MCH): 132,732 damage. HP: 274,123 (100%). Overkill: 0.",
            post);
    }

    [Theory]
    [InlineData("Fatal:")]
    [InlineData("Active mits: none captured.")]
    public void TryCombineFatalLine_RejectsUnrelatedLines(string text)
    {
        var header = new DeathChatRecapHeader(161, "Nai La", "MCH");

        Assert.False(DeathChatRecapHeaderParser.TryCombineFatalLine(header, text, out _));
    }
}
