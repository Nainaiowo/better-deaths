using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using System;
using System.Threading;

namespace BetterDeaths;

public sealed partial class Plugin
{
    private const uint InstalledUpdateChatLinkHandlerId = 1;
    private readonly CancellationTokenSource installedUpdateNoticeCts = new();
    private readonly DalamudLinkPayload installedUpdateChatLinkPayload;
    private InstalledUpdateNotice? installedUpdateNotice;
    private bool installedUpdateLoginSubscribed;
    private int installedUpdateNoticeScheduled;

    private void InitializeInstalledUpdateNotice(bool hadSavedConfiguration)
    {
        installedUpdateNotice = new(typeof(Plugin).Assembly.GetName().Version,
            Configuration.LastRecordedPluginVersion, hadSavedConfiguration);
        if (!installedUpdateNotice.Pending)
            return;
        if (!installedUpdateNotice.ShouldAnnounce)
        {
            try
            {
                installedUpdateNotice.TryDeliver(false, PersistInstalledPluginVersion, PrintInstalledUpdateNotice);
            }
            catch (Exception error)
            {
                Log.Warning(error, "Could not record the initial Better Deaths version.");
            }
            return;
        }
        ClientState.Login += OnInstalledUpdateLogin;
        installedUpdateLoginSubscribed = true;
        ScheduleInstalledUpdateNotice();
    }

    private void OnInstalledUpdateLogin() => ScheduleInstalledUpdateNotice();

    private void ScheduleInstalledUpdateNotice()
    {
        if (disposing || installedUpdateNotice?.Pending != true || Interlocked.CompareExchange(ref installedUpdateNoticeScheduled, 1, 0) != 0)
            return;
        try
        {
            _ = Framework.RunOnTick(DeliverInstalledUpdateNotice, delayTicks: 1, cancellationToken: installedUpdateNoticeCts.Token);
        }
        catch (Exception error)
        {
            Interlocked.Exchange(ref installedUpdateNoticeScheduled, 0);
            Log.Warning(error, "Could not schedule the Better Deaths installed-update notice.");
        }
    }

    private void DeliverInstalledUpdateNotice()
    {
        Interlocked.Exchange(ref installedUpdateNoticeScheduled, 0);
        if (disposing || installedUpdateNotice is not { Pending: true } notice)
            return;
        try
        {
            notice.TryDeliver(ClientState.IsLoggedIn, PersistInstalledPluginVersion, PrintInstalledUpdateNotice);
        }
        catch (Exception error)
        {
            Log.Warning(error, "Could not deliver the Better Deaths installed-update notice.");
        }
        finally
        {
            if (!notice.Pending)
                UnsubscribeInstalledUpdateLogin();
        }
    }

    private void PersistInstalledPluginVersion(string version)
    {
        var previous = Configuration.LastRecordedPluginVersion;
        Configuration.LastRecordedPluginVersion = version;
        try
        {
            SaveConfiguration();
        }
        catch
        {
            Configuration.LastRecordedPluginVersion = previous;
            throw;
        }
    }

    private void PrintInstalledUpdateNotice(string version)
    {
        if (disposing)
            return;
        ChatGui.Print(new XivChatEntry
        {
            Type = XivChatType.SystemMessage,
            Message = new SeString(
                new TextPayload($"[Better Deaths] Updated to v{version}. "),
                installedUpdateChatLinkPayload,
                new UIForegroundPayload(710),
                new TextPayload("[ View updates ]"),
                new UIForegroundPayload(0),
                RawPayload.LinkTerminator),
        });
    }

    private void OnInstalledUpdateChatLinkClick(uint commandId, SeString message)
    {
        if (!disposing)
            recapWindow.OpenUpdates();
    }

    private void UnsubscribeInstalledUpdateLogin()
    {
        if (!installedUpdateLoginSubscribed)
            return;
        ClientState.Login -= OnInstalledUpdateLogin;
        installedUpdateLoginSubscribed = false;
    }

    private void DisposeInstalledUpdateNotice()
    {
        installedUpdateNotice?.Cancel();
        UnsubscribeInstalledUpdateLogin();
        installedUpdateNoticeCts.Cancel();
        installedUpdateNoticeCts.Dispose();
    }
}
