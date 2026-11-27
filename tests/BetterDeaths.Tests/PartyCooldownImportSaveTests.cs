using BetterDeaths.PartyCooldowns;
using System.Text.Json;
using static BetterDeaths.Tests.PartyCooldownImportFixture;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownImportSaveTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public void FailedSaveKeepsThePlayersWholeOldSetup(int wireVersion, bool allJobs)
    {
        var live = Original();
        var before = JsonSerializer.Serialize(live);
        var actions = live.PartyCooldownActions;
        var rows = live.PartyCooldownRowLayouts;
        var placement = live.PartyCooldownPlacements;
        Configuration? attempted = null;
        Exception? logged = null;
        string? duringSave = null;
        var failure = new IOException("Simulated configuration write failure");
        var code = MakeCode(wireVersion, allJobs);
        var saves = 0;
        var ok = live.TrySaveCooldownImport(code, allJobs ? null : 19u, Catalog(), candidate =>
        {
            saves++;
            attempted = candidate;
            duringSave = JsonSerializer.Serialize(live);
            throw failure;
        }, ex => logged = ex, out var includedLayout, out var error);

        Assert.False(ok);
        Assert.False(includedLayout);
        Assert.Equal(CooldownImportProblem.SaveFailed, error.Problem);
        Assert.Contains("couldn't save", error.Reason);
        Assert.Equal(1, saves);
        Assert.Same(failure, logged);
        Assert.NotNull(attempted);
        Assert.NotSame(live, attempted);
        Assert.Equal(new uint[] { 17 }, attempted.PartyCooldownActions[19]);
        Assert.Equal(wireVersion == 1 ? PartyCooldownSide.Left : PartyCooldownSide.Right, attempted.PartyCooldownSide);
        Assert.Equal(before, duringSave);
        Assert.Equal(before, JsonSerializer.Serialize(live));
        Assert.Same(actions, live.PartyCooldownActions);
        Assert.Same(rows, live.PartyCooldownRowLayouts);
        Assert.Same(placement, live.PartyCooldownPlacements);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public void SuccessfulImportPublishesExactlyWhatWasSaved(int wireVersion, bool allJobs)
    {
        var live = Original();
        live.PartyCooldownPlacements!.Left!.FontSizePercent = 135;
        live.PartyCooldownPlacements.Right!.FontSizePercent = 60;
        var before = JsonSerializer.Serialize(live);
        var otherJob = live.PartyCooldownActions[21];
        string? persisted = null, duringSave = null;
        var saves = 0;
        Assert.True(live.TrySaveCooldownImport(MakeCode(wireVersion, allJobs), allJobs ? null : 19u, Catalog(), candidate =>
        {
            saves++;
            duringSave = JsonSerializer.Serialize(live);
            persisted = JsonSerializer.Serialize(candidate);
        }, _ => Assert.Fail("Unexpected save error"), out var included, out var error), error.Reason);

        Assert.Equal(1, saves);
        Assert.Equal(before, duringSave);
        Assert.Equal(persisted, JsonSerializer.Serialize(live));
        Assert.Equal(wireVersion != 1, included);
        Assert.Equal(wireVersion == 4 ? 157 : 135, live.PartyCooldownPlacements!.Left!.FontSizePercent);
        Assert.Equal(wireVersion == 4 ? 83 : 60, live.PartyCooldownPlacements.Right!.FontSizePercent);
        if (allJobs)
        {
            Assert.Equal(PartyCooldownCatalog.Jobs.Count, live.PartyCooldownActions.Count);
            Assert.Empty(live.PartyCooldownActions[21]);
        }
        else
            Assert.Same(otherJob, live.PartyCooldownActions[21]);
        Assert.True(live.ShowPartyCooldowns);
        Assert.False(live.PartyCooldownAutoCollapseJobs);
        Assert.Equal(11, live.PartyCooldownDefaultLoadoutsVersion);
        if (included)
        {
            Assert.Equal(PartyCooldownSide.Right, live.PartyCooldownSide);
            Assert.Equal(-29, live.PartyCooldownPlacements!.Left!.OffsetX);
            Assert.Equal((39f, 2f, 13f, -8f), (live.PartyCooldownIconSize, live.PartyCooldownIconGap,
                live.PartyCooldownOffsetX, live.PartyCooldownOffsetY));
            Assert.False(live.PartyCooldownRowLayouts[19].Enabled);
            Assert.Equal(new uint[] { 17 }, live.PartyCooldownRowLayouts[19].Rows[1]);
        }
        else
        {
            Assert.Equal(PartyCooldownSide.Left, live.PartyCooldownSide);
            Assert.Equal(64, live.PartyCooldownIconSize);
            Assert.True(live.PartyCooldownRowLayouts[19].Enabled);
        }
    }

    [Fact]
    public void InvalidImportNeverAttemptsToSave()
    {
        var live = Original();
        var before = JsonSerializer.Serialize(live);
        Assert.False(live.TrySaveCooldownImport(MakeCode(3, false), 21, Catalog(),
            _ => Assert.Fail("Must not save"), _ => Assert.Fail("Must not log a save failure"), out var included, out _));
        Assert.False(included);
        Assert.Equal(before, JsonSerializer.Serialize(live));
    }
}

internal static class PartyCooldownImportFixture
{
    internal static Configuration Original() => new()
    {
        ShowPartyCooldowns = true,
        PartyCooldownAutoCollapseJobs = false,
        PartyCooldownDefaultLoadoutsVersion = 11,
        PartyCooldownSide = PartyCooldownSide.Left,
        PartyCooldownPlacements = PartyCooldownPlacements.CreateDefault(),
        PartyCooldownIconSize = 64, PartyCooldownIconGap = 5, PartyCooldownOffsetX = 37, PartyCooldownOffsetY = -21,
        PartyCooldownActions = new() { [19] = [7531, 17], [21] = [7531] },
        PartyCooldownRowLayouts = new() { [19] = new() { Enabled = true, Rows = [[7531, 17], []] } },
    };

    internal static PartyCooldownCatalog Catalog() => new([
        new(7531, "Rampart", 1, 1, 1, 90, 1, [19, 21]), new(17, "Sentinel", 2, 1, 2, 120, 1, [19])]);

    internal static string MakeCode(int wireVersion, bool allJobs)
    {
        var doc = new PartyCooldownLoadoutShareDocument
        {
            Layout = new()
            {
                Side = PartyCooldownSide.Right,
                Left = new() { IconSize = 41, OffsetX = -29 },
                Right = new() { IconSize = 39, Spacing = 2, OffsetX = 13, OffsetY = -8 },
            },
            Jobs = PartyCooldownCatalog.Jobs.Where(job => allJobs || job.Id == 19)
                .Select(job => new PartyCooldownLoadoutShareJob
                {
                    JobId = job.Id, SelectedActionIds = job.Id == 19 ? [17] : [],
                    Rows = job.Id == 19 ? [[], [17], []] : [[]],
                }).ToList(),
        };
        if (wireVersion == 4)
            doc.Layout.FontSizes = new(157, 83);
        if (wireVersion is 3 or 4)
        {
            Assert.True(PartyCooldownLoadoutShareCodec.TryEncode(doc, out var code, out var error), error);
            Assert.StartsWith($"BDPC{wireVersion}:", code);
            return code;
        }
        var bytes = wireVersion == 2 ? JsonSerializer.SerializeToUtf8Bytes(doc, JsonSerializerOptions.Web) :
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                doc.Format, Version = 1, Jobs = doc.Jobs.Select(job => new { job.JobId, job.SelectedActionIds }),
            }, JsonSerializerOptions.Web);
        return $"BDPC{wireVersion}:" + Convert.ToBase64String(bytes);
    }
}
