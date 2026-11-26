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
        document.Jobs[0].Rows = [[7535], [7531], []];
        document.Jobs[0].RowsEnabled = true;
        document.Layout!.Side = PartyCooldownSide.Left;
        document.Layout.Left!.IconSize = 48;
        document.Layout.Left.OffsetX = -77;
        document.Layout.Right!.Spacing = 6;

        var code = PartyCooldownLoadoutShareCodec.Encode(document);

        Assert.StartsWith("BDPC2:", code);
        Assert.True(PartyCooldownLoadoutShareCodec.TryDecode(code, out var decoded, out var error), error);
        Assert.Equal(document.Jobs.Count, decoded.Jobs.Count);
        Assert.Equal(document.Jobs[0].JobId, decoded.Jobs[0].JobId);
        Assert.Equal(new uint[] { 7531, 7535 }, decoded.Jobs[0].SelectedActionIds);
        Assert.True(decoded.Jobs[0].RowsEnabled);
        Assert.Equal(new uint[] { 7535 }, decoded.Jobs[0].Rows[0]);
        Assert.Equal(new uint[] { 7531 }, decoded.Jobs[0].Rows[1]);
        Assert.Equal(PartyCooldownSide.Left, decoded.Layout!.Side);
        Assert.Equal(48, decoded.Layout.Left!.IconSize);
        Assert.Equal(-77, decoded.Layout.Left.OffsetX);
        Assert.Equal(6, decoded.Layout.Right!.Spacing);
        Assert.All(decoded.Jobs.Skip(1), job => Assert.Empty(job.SelectedActionIds));
        Assert.True(PartyCooldownLoadoutShareCodec.ContainsEverySupportedJob(decoded));
    }

    [Fact]
    public void SingleJobCodeCanOnlyBeUsedForThatJobsImport()
    {
        var allJobs = CreateDocument();
        var document = new PartyCooldownLoadoutShareDocument
        {
            Layout = allJobs.Layout,
            Jobs = [allJobs.Jobs[0]],
        };

        Assert.True(PartyCooldownLoadoutShareCodec.TryDecode(
            PartyCooldownLoadoutShareCodec.Encode(document), out var decoded, out var error), error);
        Assert.True(PartyCooldownLoadoutShareCodec.ContainsOnlyJob(decoded, document.Jobs[0].JobId));
        Assert.False(PartyCooldownLoadoutShareCodec.ContainsOnlyJob(decoded, PartyCooldownCatalog.Jobs[1].Id));
        Assert.False(PartyCooldownLoadoutShareCodec.ContainsEverySupportedJob(decoded));
    }

    [Fact]
    public void ShareCodeAcceptsMaximumIconSizeWithOriginalOffsetRange()
    {
        var document = CreateDocument();
        document.Layout!.Left!.IconSize = 1440;
        document.Layout.Left.OffsetX = -300;
        document.Layout.Left.OffsetY = 300;

        Assert.True(PartyCooldownLoadoutShareCodec.TryDecode(
            PartyCooldownLoadoutShareCodec.Encode(document), out var decoded, out var error), error);
        Assert.Equal(1440, decoded.Layout!.Left!.IconSize);
        Assert.Equal(-300, decoded.Layout.Left.OffsetX);
        Assert.Equal(300, decoded.Layout.Left.OffsetY);
    }

    [Fact]
    public void ShareCodeContainsLayoutAndCustomRowsButNotVisibilityOrLiveTimers()
    {
        var document = CreateDocument();
        document.Jobs[0].SelectedActionIds = [7531];
        document.Jobs[0].Rows = [[7531]];
        var code = PartyCooldownLoadoutShareCodec.Encode(document);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(code["BDPC2:".Length..]));

        Assert.Contains("side", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("offsetX", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("iconSize", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("spacing", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rowsEnabled", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rows", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("showPartyCooldowns", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("remainingSeconds", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcceptsLegacySelectionOnlyCodeWithoutChangingItsMeaning()
    {
        var document = new PartyCooldownLoadoutShareDocument
        {
            Version = 1,
            Jobs = [new PartyCooldownLoadoutShareJob { JobId = PartyCooldownCatalog.Jobs[0].Id, SelectedActionIds = [7531] }],
        };
        var code = EncodeUnchecked(document, "BDPC1:");

        Assert.True(PartyCooldownLoadoutShareCodec.TryDecode(code, out var decoded, out var error), error);
        Assert.Equal(1, decoded.Version);
        Assert.Null(decoded.Layout);
        Assert.Equal(new uint[] { 7531 }, decoded.Jobs[0].SelectedActionIds);
    }

    [Fact]
    public void RejectsInvalidPlacementSettingsAndRowAssignments()
    {
        var invalidPlacement = CreateDocument();
        invalidPlacement.Layout!.Left!.IconSize = 2000;
        Assert.False(PartyCooldownLoadoutShareCodec.TryDecode(EncodeUnchecked(invalidPlacement), out _, out var layoutError));
        Assert.Contains("placement", layoutError);

        var invalidRows = CreateDocument();
        invalidRows.Jobs[0].SelectedActionIds = [7531];
        invalidRows.Jobs[0].Rows = [[]];
        Assert.False(PartyCooldownLoadoutShareCodec.TryDecode(EncodeUnchecked(invalidRows), out _, out var rowError));
        Assert.Contains("rows must contain", rowError);
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
        Layout = new PartyCooldownLoadoutShareLayout
        {
            Side = PartyCooldownSide.Right,
            Left = new() { IconSize = 48, Spacing = 3, OffsetX = -77, OffsetY = 9 },
            Right = new() { IconSize = 24, Spacing = 3, OffsetX = 0, OffsetY = 0 },
        },
        Jobs = PartyCooldownCatalog.Jobs.Select(job => new PartyCooldownLoadoutShareJob
        {
            JobId = job.Id,
            SelectedActionIds = [],
            RowsEnabled = false,
            Rows = [[]],
        }).ToList(),
    };

    private static string EncodeUnchecked(PartyCooldownLoadoutShareDocument document, string? prefix = null)
    {
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        prefix ??= document.Version == 1 ? "BDPC1:" : "BDPC2:";
        return prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }
}
