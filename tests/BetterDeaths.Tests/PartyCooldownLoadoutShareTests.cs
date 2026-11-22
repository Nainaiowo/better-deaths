using BetterDeaths.PartyCooldowns;
using System.Text;
using System.Text.Json;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownLoadoutShareTests
{
    [Fact]
    public void RoundTripsSelectedCooldownsForEveryJob()
    {
        var document = CreateDocument();
        document.Jobs[0].SelectedActionIds = [7531, 7535];

        var code = PartyCooldownLoadoutShareCodec.Encode(document);

        Assert.StartsWith("BDPC1:", code);
        Assert.True(PartyCooldownLoadoutShareCodec.TryDecode(code, out var decoded, out var error), error);
        Assert.Equal(document.Jobs.Count, decoded.Jobs.Count);
        Assert.Equal(document.Jobs[0].JobId, decoded.Jobs[0].JobId);
        Assert.Equal(new uint[] { 7531, 7535 }, decoded.Jobs[0].SelectedActionIds);
        Assert.All(decoded.Jobs.Skip(1), job => Assert.Empty(job.SelectedActionIds));
        Assert.True(PartyCooldownLoadoutShareCodec.ContainsEverySupportedJob(decoded));
    }

    [Fact]
    public void SingleJobCodeCanOnlyBeUsedForThatJobsImport()
    {
        var allJobs = CreateDocument();
        var document = new PartyCooldownLoadoutShareDocument
        {
            Jobs = [allJobs.Jobs[0]],
        };

        Assert.True(PartyCooldownLoadoutShareCodec.TryDecode(
            PartyCooldownLoadoutShareCodec.Encode(document), out var decoded, out var error), error);
        Assert.True(PartyCooldownLoadoutShareCodec.ContainsOnlyJob(decoded, document.Jobs[0].JobId));
        Assert.False(PartyCooldownLoadoutShareCodec.ContainsOnlyJob(decoded, PartyCooldownCatalog.Jobs[1].Id));
        Assert.False(PartyCooldownLoadoutShareCodec.ContainsEverySupportedJob(decoded));
    }

    [Fact]
    public void ShareCodeDoesNotContainPlacementOrAppearanceSettings()
    {
        var code = PartyCooldownLoadoutShareCodec.Encode(CreateDocument());
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(code["BDPC1:".Length..]));

        Assert.DoesNotContain("side", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("position", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("offset", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iconSize", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("spacing", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customRows", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rows", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MergeKeepsLocalOrderAndAddsNewSelectionsWithoutSharingOrder()
    {
        var merged = PartyCooldownLoadoutShareCodec.MergeSelectionPreservingLocalOrder(
            [20, 10, 30], [10, 20, 40]);

        Assert.Equal(new uint[] { 20, 10, 40 }, merged);
    }

    [Fact]
    public void RejectsEmptyJobSet()
    {
        var document = CreateDocument();
        document.Jobs.Clear();

        Assert.False(PartyCooldownLoadoutShareCodec.TryDecode(EncodeUnchecked(document), out _, out var error));
        Assert.Contains("one or more unique", error);
    }

    [Fact]
    public void RejectsDuplicateSelectedActions()
    {
        var document = CreateDocument();
        document.Jobs[0].SelectedActionIds = [7531, 7531];

        Assert.False(PartyCooldownLoadoutShareCodec.TryDecode(EncodeUnchecked(document), out _, out var error));
        Assert.Contains("invalid cooldown selection", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("some other text")]
    [InlineData("BDPC1:broken")]
    public void RejectsMalformedShareCodes(string code) =>
        Assert.False(PartyCooldownLoadoutShareCodec.TryDecode(code, out _, out _));

    [Fact]
    public void RejectsUnsupportedFormatVersion()
    {
        var document = CreateDocument();
        document.Version++;

        Assert.False(PartyCooldownLoadoutShareCodec.TryValidateDocument(document, out var error));
        Assert.Contains("unsupported format version", error);
    }

    private static PartyCooldownLoadoutShareDocument CreateDocument() => new()
    {
        Jobs = PartyCooldownCatalog.Jobs.Select(job => new PartyCooldownLoadoutShareJob
        {
            JobId = job.Id,
            SelectedActionIds = [],
        }).ToList(),
    };

    private static string EncodeUnchecked(PartyCooldownLoadoutShareDocument document)
    {
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        return "BDPC1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }
}
