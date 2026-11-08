namespace BetterDeaths.Tests;

public sealed class InstalledUpdateNoticeTests
{
    private static readonly Version Current = new(1, 0, 0, 61);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ExistingInstallationsReceiveTheFirstRolloutWithoutAPreviousVersionMarker(string? recorded)
    {
        var notice = new InstalledUpdateNotice(Current, recorded, hadSavedConfiguration: true);
        Assert.True(notice.Pending);
        Assert.True(notice.ShouldAnnounce);
        Assert.Equal("1.0.0.61", notice.VersionToRecord);
        var operations = new List<string>();
        Assert.True(notice.TryDeliver(true, version => operations.Add($"save:{version}"), version => operations.Add($"chat:{version}")));
        Assert.Equal(["save:1.0.0.61", "chat:1.0.0.61"], operations);
        Assert.False(notice.Pending);
    }

    [Fact]
    public void FirstInstallationRecordsItsBaselineSilentlyEvenIfInitializationCreatesSettingsLater()
    {
        var notice = new InstalledUpdateNotice(Current, null, hadSavedConfiguration: false);
        var saved = string.Empty;
        var printed = 0;
        Assert.False(notice.ShouldAnnounce);
        Assert.False(notice.TryDeliver(false, version => saved = version, _ => printed++));
        Assert.Equal("1.0.0.61", saved);
        Assert.Equal(0, printed);
        Assert.False(notice.Pending);
        var reload = new InstalledUpdateNotice(Current, saved, hadSavedConfiguration: true);
        Assert.False(reload.Pending);
        var update = new InstalledUpdateNotice(new(1, 0, 0, 62), saved, hadSavedConfiguration: true);
        Assert.True(update.ShouldAnnounce);
    }

    [Theory]
    [InlineData("1.0.0.61", "1.0.0.60")]
    [InlineData("1.0.0.100", "1.0.0.99")]
    [InlineData("1.0.1.0", "1.0.0.99")]
    [InlineData("1.1.0.0", "1.0.99.99")]
    [InlineData("2.0.0.0", "1.99.99.99")]
    public void NewerLoadedVersionsAnnounceUsingNumericVersionComparison(string current, string previous)
    {
        var notice = new InstalledUpdateNotice(new Version(current), previous, hadSavedConfiguration: true);
        Assert.True(notice.ShouldAnnounce);
        Assert.True(notice.Pending);
    }

    [Theory]
    [InlineData("1.0.0.61", "1.0.0.61")]
    [InlineData("1.2.0.0", "1.2")]
    [InlineData("1.2.3.0", "1.2.3")]
    public void NormalStartsAndReloadsDoNotPrintOrSave(string current, string previous)
    {
        var notice = new InstalledUpdateNotice(new Version(current), previous, hadSavedConfiguration: true);
        var operations = 0;
        Assert.False(notice.Pending);
        Assert.False(notice.TryDeliver(true, _ => operations++, _ => operations++));
        Assert.Equal(0, operations);
    }

    [Fact]
    public void DowngradesKeepTheHighWaterMarkAndReturningToAnAnnouncedVersionIsSilent()
    {
        var recorded = "1.0.0.61";
        var downgrade = new InstalledUpdateNotice(new(1, 0, 0, 60), recorded, hadSavedConfiguration: true);
        var printed = 0;
        Assert.False(downgrade.TryDeliver(true, version => recorded = version, _ => printed++));
        Assert.Equal("1.0.0.61", recorded);
        var restored = new InstalledUpdateNotice(Current, recorded, hadSavedConfiguration: true);
        Assert.False(restored.TryDeliver(true, version => recorded = version, _ => printed++));
        Assert.Equal(0, printed);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("1.0.0.bad")]
    [InlineData("v1.0.0.60")]
    public void CorruptMarkersAreRepairedSilentlyInsteadOfClaimingAnUnconfirmedUpdate(string recorded)
    {
        var notice = new InstalledUpdateNotice(Current, recorded, hadSavedConfiguration: true);
        var saved = string.Empty;
        var printed = 0;
        Assert.False(notice.ShouldAnnounce);
        Assert.False(notice.TryDeliver(false, version => saved = version, _ => printed++));
        Assert.Equal("1.0.0.61", saved);
        Assert.Equal(0, printed);
    }

    [Fact]
    public void MissingOrPlaceholderAssemblyVersionsCannotProduceAFalseNotice()
    {
        foreach (var current in new Version?[] { null, new(0, 0, 0, 0) })
        {
            var notice = new InstalledUpdateNotice(current, null, hadSavedConfiguration: true);
            Assert.False(notice.Pending);
            Assert.False(notice.ShouldAnnounce);
        }
    }

    [Fact]
    public void LoggedOutUpdatesWaitForLoginAndRepeatedLoginCallbacksDoNotRepeatTheMessage()
    {
        var notice = new InstalledUpdateNotice(Current, "1.0.0.60", hadSavedConfiguration: true);
        var saved = "1.0.0.60";
        var prints = 0;
        Assert.False(notice.TryDeliver(false, version => saved = version, _ => prints++));
        Assert.Equal("1.0.0.60", saved);
        Assert.True(notice.Pending);
        Assert.True(notice.TryDeliver(true, version => saved = version, _ => prints++));
        foreach (var loggedIn in new[] { true, false, true, true })
            Assert.False(notice.TryDeliver(loggedIn, _ => throw new InvalidOperationException(), _ => prints++));
        Assert.Equal("1.0.0.61", saved);
        Assert.Equal(1, prints);
        Assert.False(new InstalledUpdateNotice(Current, saved, hadSavedConfiguration: true).Pending);
    }

    [Fact]
    public void ReloadingBeforeLoginDoesNotLoseAnUnsentUpdateNotice()
    {
        var recorded = "1.0.0.60";
        var first = new InstalledUpdateNotice(Current, recorded, hadSavedConfiguration: true);
        Assert.False(first.TryDeliver(false, version => recorded = version, _ => throw new InvalidOperationException()));
        first.Cancel();
        var reload = new InstalledUpdateNotice(Current, recorded, hadSavedConfiguration: true);
        var prints = 0;
        Assert.True(reload.TryDeliver(true, version => recorded = version, _ => prints++));
        Assert.Equal(1, prints);
    }

    [Fact]
    public void DisposingCancelsPendingDeliveryAndDoesNotRecordAnUnsentVersion()
    {
        var notice = new InstalledUpdateNotice(Current, "1.0.0.60", hadSavedConfiguration: true);
        notice.Cancel();
        Assert.False(notice.Pending);
        Assert.False(notice.TryDeliver(true, _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void CancelingDuringPersistenceDoesNotPrintAfterDisposal()
    {
        var notice = new InstalledUpdateNotice(Current, "1.0.0.60", hadSavedConfiguration: true);
        Assert.False(notice.TryDeliver(true, _ => notice.Cancel(), _ => throw new InvalidOperationException()));
        Assert.False(notice.Pending);
    }

    [Fact]
    public void SaveFailuresDoNotPrintOrConsumeTheNotice()
    {
        var notice = new InstalledUpdateNotice(Current, "1.0.0.60", hadSavedConfiguration: true);
        var prints = 0;
        Assert.Throws<IOException>(() => notice.TryDeliver(true, _ => throw new IOException(), _ => prints++));
        Assert.True(notice.Pending);
        Assert.Equal(0, prints);
        Assert.True(notice.TryDeliver(true, _ => { }, _ => prints++));
        Assert.Equal(1, prints);
    }

    [Fact]
    public void AnExceptionAfterPersistenceCannotCauseRepeatedChatAttempts()
    {
        var notice = new InstalledUpdateNotice(Current, "1.0.0.60", hadSavedConfiguration: true);
        var recorded = "1.0.0.60";
        Assert.Throws<InvalidOperationException>(() => notice.TryDeliver(true, version => recorded = version,
            _ => throw new InvalidOperationException()));
        Assert.Equal("1.0.0.61", recorded);
        Assert.False(notice.Pending);
        Assert.False(notice.TryDeliver(true, _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void ReentrantCallbacksCannotPostMoreThanOnce()
    {
        var notice = new InstalledUpdateNotice(Current, "1.0.0.60", hadSavedConfiguration: true);
        var prints = 0;
        Assert.True(notice.TryDeliver(true,
            _ => Assert.False(notice.TryDeliver(true, _ => throw new InvalidOperationException(), _ => prints++)),
            _ => prints++));
        Assert.Equal(1, prints);
    }
}
