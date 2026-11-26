using BetterDeaths.PartyCooldowns;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using static BetterDeaths.Tests.PartyCooldownImportFixture;

namespace BetterDeaths.Tests;

public sealed class PartyCooldownImportFeedbackTests
{
    [Theory]
    [InlineData(null, "BD-CI-01", "Copy a cooldown share code")]
    [InlineData("  ", "BD-CI-01", "Copy a cooldown share code")]
    [InlineData("private clipboard note", "BD-CI-03", "isn't a Better Deaths cooldown code")]
    [InlineData("BDPC3:broken", "BD-CI-04", "Copy the full code and try again")]
    [InlineData("BDPC9:abcd", "BD-CI-05", "Check for a Better Deaths update")]
    public void BadClipboardGivesOneNoticeAndOneLocalError(string? text, string id, string reason)
    {
        var result = Click(text, 19);
        CheckFailure(result, id, reason);
        Assert.Equal(0, result.Saves);
        if (!string.IsNullOrWhiteSpace(text))
        {
            Assert.DoesNotContain(text, result.SystemErrors[0]);
            Assert.DoesNotContain(text, result.Notices[0].Text);
        }
    }

    [Fact]
    public void OversizedClipboardStopsBeforeTryingToApplyAnything()
    {
        var result = Click(new string('x', PartyCooldownLoadoutShareCodec.MaximumCodeLength + 1), null);
        CheckFailure(result, "BD-CI-02", "too long");
        Assert.Equal(0, result.ApplyCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WrongImportButtonIdentifiesWhereToUseTheCode(bool allJobs)
    {
        var code = MakeCode(3, allJobs);
        var result = Click(code, allJobs ? 19u : null);
        CheckFailure(result, "BD-CI-07", allJobs ? "above the job list" : "inside Paladin");
        Assert.DoesNotContain(code, result.SystemErrors[0]);
        Assert.Equal(0, result.Saves);
    }

    [Fact]
    public void UnavailableAbilityAndOutOfRangeLayoutGiveSpecificReasons()
    {
        var doc = LegacyJson();
        doc["jobs"]![0]!["jobId"] = 21; // Sentinel is not available to Warrior.
        CheckFailure(Click(Wrap(doc), 21), "BD-CI-09", "Warrior");
        doc = LegacyJson();
        doc["layout"]!["left"]!["offsetX"] = -500;
        CheckFailure(Click(Wrap(doc), 19), "BD-CI-11", "Left horizontal offset: -500 px");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveFailureReportsOnceWithoutPrintingTheLoadoutOrException(bool allJobs)
    {
        var live = Original();
        var previous = live.PartyCooldownActions;
        var code = MakeCode(3, allJobs);
        var failure = new IOException("private filesystem details");
        var result = Click(code, allJobs ? null : 19u, live, _ => throw failure);
        CheckFailure(result, "BD-CI-14", "couldn't save");
        Assert.Equal(1, result.Saves);
        Assert.Same(failure, Assert.Single(result.LocalLog));
        Assert.DoesNotContain(failure.Message, result.SystemErrors[0]);
        Assert.DoesNotContain(code, result.SystemErrors[0]);
        Assert.Same(previous, live.PartyCooldownActions);
    }

    [Fact]
    public void AFailedBackupIsNotMisreportedAsADamagedCodeOrFailedSave()
    {
        var live = Original();
        live.PartyCooldownPlacements!.Left!.Spacing = -1;
        var result = Click(MakeCode(3, false), 19, live);
        CheckFailure(result, "BD-CI-13", "backed up");
        Assert.Equal(0, result.Saves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableDataAndUnexpectedErrorsUseDistinctCodes(bool unexpected)
    {
        bool Apply(string code, uint? job, out bool layout, out CooldownImportError error)
        {
            if (unexpected)
                throw new InvalidOperationException("private details");
            layout = false;
            error = new(CooldownImportProblem.CatalogNotReady);
            return false;
        }
        var result = Click(MakeCode(3, false), 19, substitute: Apply);
        CheckFailure(result, unexpected ? "BD-CI-15" : "BD-CI-12", unexpected ? "Check your current setup" : "abilities list");
        Assert.DoesNotContain("private details", result.SystemErrors[0]);
        Assert.Equal(unexpected ? 1 : 0, result.LocalLog.Count);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void SuccessfulImportsKeepTheExistingNoticeAndNeverPrintToChat(int format, bool allJobs)
    {
        var result = Click(MakeCode(format, allJobs), allJobs ? null : 19u);
        Assert.True(result.Success);
        var notice = Assert.Single(result.Notices);
        Assert.False(notice.IsError);
        Assert.Equal(format == 1 ? "Cooldown selections imported; local layout kept." : "Cooldown loadout and layout imported.", notice.Text);
        Assert.Empty(result.SystemErrors);
        Assert.Empty(result.LocalLog);
        Assert.Equal(1, result.Saves);
    }

    private static void CheckFailure(Feedback result, string id, string reason)
    {
        Assert.False(result.Success);
        var notice = Assert.Single(result.Notices);
        Assert.True(notice.IsError);
        Assert.Contains(id, notice.Text);
        Assert.Contains(reason, notice.Text);
        if (id != "BD-CI-15")
            Assert.Contains("Nothing was changed.", notice.Text);
        var chat = Assert.Single(result.SystemErrors);
        Assert.StartsWith("[Better Deaths] Cooldown import failed: " + notice.Text.Split('\n')[0], chat);
        Assert.Contains($"you can share {id} in the Better Deaths channel on the Punish Discord.", chat);
        Assert.Equal(chat.IndexOf(id, StringComparison.Ordinal), chat.LastIndexOf(id, StringComparison.Ordinal));
        Assert.DoesNotContain("\n", chat);
    }

    private static JsonNode LegacyJson() =>
        JsonNode.Parse(Convert.FromBase64String(MakeCode(2, false)["BDPC2:".Length..]))!;

    private static string Wrap(JsonNode json) => "BDPC2:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json.ToJsonString()));

    private sealed class Feedback
    {
        public bool Success;
        public int ApplyCalls, Saves;
        public readonly List<(string Text, bool IsError)> Notices = [];
        public readonly List<string> SystemErrors = [];
        public readonly List<Exception> LocalLog = [];
    }

    private static Feedback Click(string? text, uint? target, Configuration? live = null,
        Action<Configuration>? save = null, PartyCooldownImportFeedback.ApplyImport? substitute = null)
    {
        live ??= Original();
        var result = new Feedback();
        bool Apply(string code, uint? job, out bool layout, out CooldownImportError error)
        {
            result.ApplyCalls++;
            return live.TrySaveCooldownImport(code, job, Catalog(), candidate =>
            {
                result.Saves++;
                save?.Invoke(candidate);
            }, result.LocalLog.Add, out layout, out error);
        }
        var pointer = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            result.Success = PartyCooldownImportFeedback.FromClipboard(() => pointer, target, substitute ?? Apply,
                (message, isError) => result.Notices.Add((message, isError)), result.SystemErrors.Add, result.LocalLog.Add);
            return result;
        }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }
}
