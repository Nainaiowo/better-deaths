using BetterDeaths.PartyCooldowns;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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

        var code = EncodeValid(document);

        Assert.StartsWith("BDPC3:", code);
        Assert.True(PartyCooldownLoadoutShareCodec.TryDecode(code, out var decoded, out var error), error.Reason);
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
            EncodeValid(document), out var decoded, out var error), error.Reason);
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
            EncodeValid(document), out var decoded, out var error), error.Reason);
        Assert.Equal(1440, decoded.Layout!.Left!.IconSize);
        Assert.Equal(-300, decoded.Layout.Left.OffsetX);
        Assert.Equal(300, decoded.Layout.Left.OffsetY);
    }

    [Fact]
    public void CompactAndOlderLayoutCodesKeepExactlyTheSameSettings()
    {
        var document = CreateDocument();
        document.Jobs[0].SelectedActionIds = [7531];
        document.Jobs[0].Rows = [[], [7531], []]; // Keep these rows even when custom rows are switched off.
        document.Layout!.Right!.OffsetY = -12.375f;
        var code = EncodeValid(document);
        var older = EncodeUnchecked(document);
        Assert.True(code.Length < older.Length / 2);
        foreach (var input in new[] { code, older })
        {
            Assert.True(PartyCooldownLoadoutShareCodec.TryDecode(input, out var decoded, out var error), error.Reason);
            Assert.Equal(JsonSerializer.Serialize(document), JsonSerializer.Serialize(decoded));
            Assert.Equal(code, EncodeValid(decoded));
        }
    }

    // A released wire format must not drift just because both encoder and decoder were edited together.
    private const string CompactPaladin = "BDPC3:AQAAQEIAAEBAAACawgAAEEEAAMBBAABAQAAAAAAAAAAAARMCEes6DgEBAOFF1Tw=";

    [Fact]
    public void StableCompactFixtureImportsWithItsOriginalOrderAndRows()
    {
        Assert.True(PartyCooldownLoadoutShareCodec.TryDecode(CompactPaladin, out var doc, out var error), error.Reason);
        var job = Assert.Single(doc.Jobs);
        Assert.Equal(19u, job.JobId);
        Assert.Equal(new uint[] { 17, 7531 }, job.SelectedActionIds);
        Assert.True(job.RowsEnabled);
        Assert.Equal(new uint[] { 7531 }, job.Rows[0]);
        Assert.Equal(new uint[] { 17 }, job.Rows[1]);
        Assert.Equal(CompactPaladin, EncodeValid(doc));
    }

    [Fact]
    public void DamagedOrIncompleteCompactCopiesAreNotAccepted()
    {
        var bytes = Convert.FromBase64String(CompactPaladin[6..]);
        bytes[1] ^= 1; // This still describes a valid placement, but is no longer the exported one.
        foreach (var code in new[] { "BDPC3:", CompactPaladin[..^4], "BDPC3:" + Convert.ToBase64String(bytes) })
            Assert.False(PartyCooldownLoadoutShareCodec.TryDecode(code, out _, out _));
    }

    [Theory]
    [InlineData(0, 9, "BD-CI-11")]     // Unknown side.
    [InlineData(33, 255, "BD-CI-06")]  // Job count.
    [InlineData(34, 0, "BD-CI-06")]    // Unsupported job.
    [InlineData(35, 25, "BD-CI-08")]   // Too many abilities.
    [InlineData(39, 0, "BD-CI-10")]    // No rows.
    [InlineData(39, 255, "BD-CI-10")]  // Unknown flags.
    [InlineData(40, 3, "BD-CI-10")]    // Row longer than the selection.
    [InlineData(41, 2, "BD-CI-10")]    // Index outside selection.
    [InlineData(42, 1, "BD-CI-10")]    // Both rows reference the same ability.
    public void CompactFieldsAreValidatedEvenWhenTheCopyCheckIsValid(int index, byte value, string supportCode)
    {
        var payload = Convert.FromBase64String(CompactPaladin[6..])[..^4];
        payload[index] = value;
        Assert.False(PartyCooldownLoadoutShareCodec.TryDecode(SealPacked(payload), out _, out var error));
        Assert.Equal(supportCode, error.SupportCode);
    }

    [Fact]
    public void ShortOrExtraCompactFieldsDoNotImport()
    {
        var payload = Convert.FromBase64String(CompactPaladin[6..])[..^4];
        foreach (var damaged in new[] { payload[..^1], payload[..6], payload.Concat(new byte[] { 0 }).ToArray() })
            Assert.False(PartyCooldownLoadoutShareCodec.TryDecode(SealPacked(damaged), out _, out _));
    }

    [Fact]
    public void CompactCodeFitsAndRoundTripsEvenWhenEveryJobHasAllSlotsFilled()
    {
        var doc = CreateDocument();
        foreach (var job in doc.Jobs)
        {
            job.SelectedActionIds = Enumerable.Range(0, PartyCooldownRows.MaximumAbilities)
                .Select(i => uint.MaxValue - (uint)i).ToList();
            job.Rows = job.SelectedActionIds.AsEnumerable().Reverse().Chunk(8).Select(row => row.ToList()).ToList();
            job.RowsEnabled = job.JobId % 2 == 0;
        }
        var code = EncodeValid(doc);
        Assert.True(code.Length < PartyCooldownLoadoutShareCodec.MaximumCodeLength);
        Assert.True(PartyCooldownLoadoutShareCodec.TryDecode(code, out var decoded, out var error), error.Reason);
        Assert.Equal(JsonSerializer.Serialize(doc), JsonSerializer.Serialize(decoded));
    }

    [Fact]
    public void ClipboardIsSizeCheckedInBytesBeforeMakingAManagedString()
    {
        Assert.False(PartyCooldownLoadoutShareCodec.TryReadClipboardCode(0, out _, out _));
        var limit = PartyCooldownLoadoutShareCodec.MaximumCodeLength;
        var oversized = Marshal.StringToCoTaskMemUTF8("BDPC3:" + new string('x', limit));
        try
        {
            PartyCooldownLoadoutShareCodec.TryReadClipboardCode(oversized, out _, out _);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var accepted = PartyCooldownLoadoutShareCodec.TryReadClipboardCode(oversized, out var rejected, out var error);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.False(accepted);
            Assert.True(allocated < 1024, $"Rejecting an oversized clipboard allocated {allocated} bytes.");
            Assert.Empty(rejected);
            Assert.Contains("too long", error.Reason);
            Marshal.WriteByte(oversized, limit, 0); // A terminator at exactly the limit is allowed.
            Assert.True(PartyCooldownLoadoutShareCodec.TryReadClipboardCode(oversized, out var boundary, out error), error.Reason);
            Assert.Equal(limit, boundary.Length);
        }
        finally { Marshal.FreeCoTaskMem(oversized); }
        var original = Marshal.StringToCoTaskMemUTF8(CompactPaladin);
        try
        {
            Assert.True(PartyCooldownLoadoutShareCodec.TryReadClipboardCode(original, out var code, out var error), error.Reason);
            Assert.Equal(CompactPaladin, code);
        }
        finally { Marshal.FreeCoTaskMem(original); }
    }

    private static string SealPacked(byte[] payload) =>
        "BDPC3:" + Convert.ToBase64String([.. payload, .. SHA256.HashData(payload)[..4]]);

    [Fact]
    public void AcceptsLegacySelectionOnlyCodeWithoutChangingItsMeaning()
    {
        var code = Wrap("""{"format":"better-deaths-party-cooldowns","version":1,"jobs":[{"jobId":19,"selectedActionIds":[7531]}]}""", "BDPC1:");

        Assert.True(PartyCooldownLoadoutShareCodec.TryDecode(code, out var decoded, out var error), error.Reason);
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
        Assert.Contains("Left icon size", layoutError.Reason);
        Assert.Contains(PartyCooldownLayout.MaximumIconSize.ToString(), layoutError.Reason);

        var invalidRows = CreateDocument();
        invalidRows.Jobs[0].SelectedActionIds = [7531];
        invalidRows.Jobs[0].Rows = [[]];
        Assert.False(PartyCooldownLoadoutShareCodec.TryDecode(EncodeUnchecked(invalidRows), out _, out var rowError));
        Assert.Contains("rows must contain", rowError.Reason);
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
        Assert.Contains("one or more unique", error.Reason);
    }

    [Fact]
    public void RejectsDuplicateSelectedActions()
    {
        var document = CreateDocument();
        document.Jobs[0].SelectedActionIds = [7531, 7531];

        Assert.False(PartyCooldownLoadoutShareCodec.TryDecode(EncodeUnchecked(document), out _, out var error));
        Assert.Contains("invalid cooldown selection", error.Reason);
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
        Assert.Contains("unsupported format version", error.Reason);
    }

    [Theory]
    [InlineData("root", "format")]
    [InlineData("root", "version")]
    [InlineData("root", "jobs")]
    [InlineData("root", "layout")]
    [InlineData("layout", "left")]
    [InlineData("layout", "right")]
    [InlineData("layout", "side")]
    [InlineData("left", "iconSize")]
    [InlineData("left", "spacing")]
    [InlineData("left", "offsetX")]
    [InlineData("left", "offsetY")]
    [InlineData("job", "selectedActionIds")]
    [InlineData("job", "rowsEnabled")]
    [InlineData("job", "rows")]
    [InlineData("job", "jobId")]
    public void MissingFieldsAreNotTreatedAsDefaults(string container, string field)
    {
        var root = JsonSerializer.SerializeToNode(CreateDocument(), JsonSerializerOptions.Web)!.AsObject();
        var parent = container switch
        {
            "root" => root,
            "job" => root["jobs"]![0]!.AsObject(),
            "layout" => root["layout"]!.AsObject(),
            _ => root["layout"]![container]!.AsObject(),
        };
        Assert.True(parent.Remove(field));
        Assert.False(PartyCooldownLoadoutShareCodec.TryDecode(Wrap(root.ToJsonString()), out _, out _));
    }

    [Theory]
    [InlineData("""{"jobId":19}""")]
    [InlineData("""{"jobId":19,"selectedActionIds":null}""")]
    [InlineData("""{"jobId":19,"selectedActionIds":[],"selectedActionIds":[7531]}""")]
    [InlineData("""{"jobId":19,"selectedActionIds":[],"showPartyCooldowns":false}""")]
    public void RejectsIncompleteOrAmbiguousLegacyCodes(string job)
    {
        var code = Wrap($$"""{"format":"better-deaths-party-cooldowns","version":1,"jobs":[{{job}}]}""", "BDPC1:");
        Assert.False(PartyCooldownLoadoutShareCodec.TryDecode(code, out _, out _));
    }

    [Fact]
    public void InvalidLastJobCannotPartiallyImportEarlierJobsOrPlacement()
    {
        var document = CreateDocument();
        document.Jobs[^1].SelectedActionIds = [7531]; // Available on Paladin, not this job.
        document.Jobs[^1].Rows = [[7531]];
        List<uint> previous = [7531];
        PartyCooldownRows previousRows = new() { Enabled = true, Rows = [[], [7531]] };
        Dictionary<uint, List<uint>> selections = new() { [19] = previous };
        Dictionary<uint, PartyCooldownRows> rows = new() { [19] = previousRows };
        Assert.False(PartyCooldownLoadoutShareCodec.TryImport(EncodeValid(document), null, TestCatalog(),
            selections, rows, out var layout, out var error));
        Assert.Contains("unavailable", error.Reason);
        Assert.Null(layout);
        Assert.Same(previous, Assert.Single(selections).Value);
        Assert.Same(previousRows, Assert.Single(rows).Value);
        Assert.Equal(new uint[] { 7531 }, rows[19].Rows[1]);
    }

    [Fact]
    public void OnlyAnExplicitEmptySelectionClearsTheRequestedJob()
    {
        var document = CreateDocument();
        document.Jobs = [document.Jobs[0]];
        Dictionary<uint, List<uint>> selections = new() { [19] = [7531], [21] = [7531] };
        Dictionary<uint, PartyCooldownRows> rows = new() { [19] = new() { Rows = [[7531]] } };
        var code = EncodeValid(document);
        Assert.False(PartyCooldownLoadoutShareCodec.TryImport(code, 21, TestCatalog(), selections, rows, out var rejectedLayout, out _));
        Assert.Null(rejectedLayout);
        Assert.Equal(new uint[] { 7531 }, selections[19]);
        Assert.True(PartyCooldownLoadoutShareCodec.TryImport(code, 19, TestCatalog(), selections, rows, out var layout, out var error), error.Reason);
        Assert.NotNull(layout);
        Assert.Empty(selections[19]);
        Assert.Empty(Assert.Single(rows[19].Rows));
        Assert.Equal(new uint[] { 7531 }, selections[21]);
    }

    [Fact]
    public void LegacyImportKeepsLocalRowAndDoesNotReturnAPlacement()
    {
        var code = Wrap("""{"format":"better-deaths-party-cooldowns","version":1,"jobs":[{"jobId":19,"selectedActionIds":[7531]}]}""", "BDPC1:");
        Dictionary<uint, List<uint>> selections = new() { [19] = [17, 7531] };
        Dictionary<uint, PartyCooldownRows> rows = new() { [19] = new() { Enabled = true, Rows = [[17], [7531]] } };
        Assert.True(PartyCooldownLoadoutShareCodec.TryImport(code, 19, TestCatalog(), selections, rows, out var layout, out var error), error.Reason);
        Assert.Null(layout);
        Assert.Equal(new uint[] { 7531 }, selections[19]);
        Assert.Empty(rows[19].Rows[0]);
        Assert.Equal(new uint[] { 7531 }, rows[19].Rows[1]);
    }

    [Fact]
    public void OversizedCodesAndLayoutsFailWithoutExportingOrImporting()
    {
        Assert.False(PartyCooldownLoadoutShareCodec.TryDecode(new string('x', PartyCooldownLoadoutShareCodec.MaximumCodeLength + 1), out _, out _));
        var document = CreateDocument();
        document.Layout!.Right!.OffsetX = PartyCooldownLayout.MaximumOffset + 1;
        Assert.False(PartyCooldownLoadoutShareCodec.TryEncode(document, out var code, out var error));
        Assert.Empty(code);
        Assert.Contains("Right horizontal offset", error);
        Assert.False(PartyCooldownLoadoutShareCodec.TryDecode(EncodeUnchecked(document), out _, out var importError));
        Assert.Equal(error, importError.Reason);
    }

    [Fact]
    public void CurrentImportAppliesValidatedRowsAndBothSideLayouts()
    {
        var document = CreateDocument();
        document.Jobs[0].SelectedActionIds = [7531, 17];
        document.Jobs[0].RowsEnabled = true;
        document.Jobs[0].Rows = [[17], [], [7531]];
        Dictionary<uint, List<uint>> selections = [];
        Dictionary<uint, PartyCooldownRows> rows = [];

        Assert.True(PartyCooldownLoadoutShareCodec.TryImport(EncodeValid(document), null, TestCatalog(),
            selections, rows, out var layout, out var error), error.Reason);
        Assert.Equal(PartyCooldownCatalog.Jobs.Count, selections.Count);
        Assert.Equal(new uint[] { 17, 7531 }, selections[19]);
        Assert.Empty(rows[19].Rows[1]);
        Assert.Equal(new uint[] { 7531 }, rows[19].Rows[2]);
        Assert.Equal(PartyCooldownSide.Right, layout!.Side);
        Assert.Equal(-77, layout.Left!.OffsetX);
        Assert.Equal(24, layout.Right!.IconSize);
    }

    [Fact]
    public void RejectsJobsExceedingEitherRowOrAbilityLimit()
    {
        var document = CreateDocument();
        document.Jobs[0].Rows = Enumerable.Range(0, PartyCooldownRows.MaximumRows + 1).Select(_ => new List<uint>()).ToList();
        Assert.False(PartyCooldownLoadoutShareCodec.TryDecode(EncodeUnchecked(document), out _, out _));
        var ids = Enumerable.Range(1, PartyCooldownRows.MaximumAbilities + 1).Select(id => (uint)id).ToList();
        document.Jobs[0].Rows = [ids];
        document.Jobs[0].SelectedActionIds = ids;
        Assert.False(PartyCooldownLoadoutShareCodec.TryDecode(EncodeUnchecked(document), out _, out _));
    }

    [Fact]
    public void DamagedClipboardContentsLeaveExistingJobAndRowsUntouched()
    {
        var document = CreateDocument();
        document.Jobs = [document.Jobs[0]];
        var root = JsonSerializer.SerializeToNode(document, JsonSerializerOptions.Web)!.AsObject();
        var invalid = new List<string> { "not a cooldown code", "BDPC2:!", Wrap("null"), Wrap("[]") };
        foreach (var (owner, field, badValue) in new (JsonObject, string, JsonNode?)[]
        {
            (root["jobs"]![0]!.AsObject(), "selectedActionIds", null),
            (root["jobs"]![0]!.AsObject(), "rows", new JsonArray((JsonNode?)null)),
            (root["layout"]!.AsObject(), "side", 255),
            (root["layout"]!["left"]!.AsObject(), "spacing", "3"),
            (root["layout"]!["right"]!.AsObject(), "offsetY", PartyCooldownLayout.MaximumOffset + 1),
        })
        {
            var previous = owner[field];
            owner[field] = badValue;
            invalid.Add(Wrap(root.ToJsonString()));
            owner[field] = previous;
        }
        root["jobs"]![0]!.AsObject().Remove("selectedActionIds");
        invalid.Add(Wrap(root.ToJsonString()));
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        bytes[2] = 0xff;
        invalid.Add("BDPC2:" + Convert.ToBase64String(bytes));

        var catalog = TestCatalog();
        Dictionary<uint, List<uint>> selections = new() { [19] = [7531] };
        Dictionary<uint, PartyCooldownRows> rows = new() { [19] = new() { Enabled = true, Rows = [[], [7531]] } };
        foreach (var code in invalid)
        {
            Assert.False(PartyCooldownLoadoutShareCodec.TryImport(code, 19, catalog, selections, rows, out var layout, out var error));
            Assert.False(string.IsNullOrWhiteSpace(error.Reason));
            Assert.Null(layout);
            Assert.Equal(new uint[] { 7531 }, selections[19]);
            Assert.True(rows[19].Enabled);
            Assert.Equal(new uint[] { 7531 }, rows[19].Rows[1]);
        }
    }

    private static PartyCooldownCatalog TestCatalog() => new([
        new(7531, "Rampart", 1, 1, 1, 90, 1, [19, 21]), new(17, "Sentinel", 2, 1, 2, 120, 1, [19])]);

    private static string EncodeValid(PartyCooldownLoadoutShareDocument document)
    {
        Assert.True(PartyCooldownLoadoutShareCodec.TryEncode(document, out var code, out var error), error);
        return code;
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
        return Wrap(json, prefix);
    }

    private static string Wrap(string json, string prefix = "BDPC2:") => prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
}
