using System;
using System.Globalization;

namespace BetterDeaths.PartyCooldowns;

// Player-facing support IDs. Keep existing numbers stable when adding new reasons.
internal enum CooldownImportProblem
{
    None = 0,
    EmptyClipboard = 1,
    TooLong = 2,
    NotCooldownCode = 3,
    Unreadable = 4,
    UnsupportedVersion = 5,
    InvalidJobs = 6,
    WrongScope = 7,
    InvalidSelection = 8,
    UnavailableAbility = 9,
    InvalidRows = 10,
    InvalidPlacement = 11,
    CatalogNotReady = 12,
    BackupFailed = 13,
    SaveFailed = 14,
    Unexpected = 15,
}

internal readonly record struct CooldownImportError(CooldownImportProblem Problem, string? Detail = null)
{
    public string SupportCode => Problem == CooldownImportProblem.None ? string.Empty :
        "BD-CI-" + ((int)Problem).ToString("D2", CultureInfo.InvariantCulture);

    public string Reason => Detail ?? (Problem switch
    {
        CooldownImportProblem.None => string.Empty,
        CooldownImportProblem.EmptyClipboard => "No code was copied. Copy a cooldown share code first.",
        CooldownImportProblem.TooLong => "The copied text is too long. Copy only the cooldown share code.",
        CooldownImportProblem.NotCooldownCode => "This isn't a Better Deaths cooldown code. Copy the code from Export.",
        CooldownImportProblem.Unreadable => "The code couldn't be read. Copy the full code and try again.",
        CooldownImportProblem.UnsupportedVersion => "This code uses an unsupported format version. Check for a Better Deaths update.",
        CooldownImportProblem.InvalidJobs => "The code must include one or more unique, supported jobs. Ask for a fresh export.",
        CooldownImportProblem.WrongScope => "Use a code for the selected job.",
        CooldownImportProblem.InvalidSelection => "One job has an invalid cooldown selection. Ask for a fresh export.",
        CooldownImportProblem.UnavailableAbility => "The code includes an unavailable or repeated ability.",
        CooldownImportProblem.InvalidRows => "A job's custom rows are invalid. Ask for a fresh export.",
        CooldownImportProblem.InvalidPlacement => "The code doesn't contain valid left and right layouts.",
        CooldownImportProblem.CatalogNotReady => "Cooldown data isn't available yet. Try again once the abilities list is ready.",
        CooldownImportProblem.BackupFailed => "Your current setup couldn't be backed up for Undo. The import was canceled.",
        CooldownImportProblem.SaveFailed => "Better Deaths couldn't save the imported cooldown settings.",
        _ => "An unexpected error interrupted the import. Check your current setup before retrying.",
    });
}

internal static class PartyCooldownImportFeedback
{
    internal delegate bool ApplyImport(string code, uint? jobId, out bool includedLayout, out CooldownImportError error);

    internal static bool FromClipboard(Func<nint> readClipboard, uint? jobId, ApplyImport apply,
        Action<string, bool> showNotice, Action<string> printRedSystemMessage, Action<Exception> logUnexpected)
    {
        var includedLayout = false;
        bool success;
        CooldownImportError error;
        try
        {
            success = PartyCooldownLoadoutShareCodec.TryReadClipboardCode(readClipboard(), out var code, out error) &&
                apply(code, jobId, out includedLayout, out error);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or AccessViolationException or StackOverflowException))
        {
            logUnexpected(ex);
            success = false;
            error = new(CooldownImportProblem.Unexpected);
        }

        if (success)
        {
            showNotice(includedLayout ? "Cooldown loadout and layout imported." : "Cooldown selections imported; local layout kept.", false);
            return true;
        }

        if (error.Problem == CooldownImportProblem.None)
            error = new(CooldownImportProblem.Unexpected);
        var reason = error.Reason;
        var id = error.SupportCode;
        var status = error.Problem == CooldownImportProblem.Unexpected ? id : $"Nothing was changed. {id}";
        showNotice($"{reason}\n{status}", true);
        printRedSystemMessage($"[Better Deaths] Cooldown import failed: {reason} If it keeps failing, you can share {id} in the Better Deaths channel on the Punish Discord.");
        return false;
    }
}
