namespace BetterDeaths.Tests;

public sealed class ContentCapturePolicyTests
{
    [Theory]
    [InlineData(true, false, 0, 0, true)]
    [InlineData(true, false, 0, 1, true)]
    [InlineData(true, false, 0, 2, true)]
    [InlineData(false, false, 0, 0, false)]
    [InlineData(true, true, 0, 0, false)]
    [InlineData(true, true, 2, 3, false)]
    [InlineData(true, true, 4, 10, false)]
    [InlineData(true, true, 5, 17, false)]
    [InlineData(true, true, 28, 17, false)]
    [InlineData(true, true, 6, 0, false)]
    [InlineData(true, false, 0, 3, false)]
    [InlineData(true, false, 0, 4, false)]
    [InlineData(true, false, 0, 31, false)]
    [InlineData(true, false, 0, 57, false)]
    [InlineData(true, false, 0, 58, false)]
    [InlineData(true, false, 999, 0, false)]
    public void OverworldCaptureDoesNotReclassifyDuties(bool hasTerritory, bool hasDuty,
        uint category, uint intendedUse, bool expected)
    {
        Assert.Equal(expected, ContentCapturePolicy.SupportsOverworldMeter(hasTerritory, hasDuty, category, intendedUse));
    }

    [Theory]
    [InlineData(4, 3, true)]
    [InlineData(4, 10, true)]
    [InlineData(5, 8, true)]
    [InlineData(5, 17, true)]
    [InlineData(28, 17, true)]
    [InlineData(30, 4, true)]
    [InlineData(30, 57, true)]
    [InlineData(30, 58, true)]
    [InlineData(37, 36, true)]
    [InlineData(0, 4, true)]
    [InlineData(0, 57, true)]
    [InlineData(0, 58, true)]
    [InlineData(2, 3, true)]
    [InlineData(21, 31, true)]
    [InlineData(0, 3, true)]
    [InlineData(0, 31, true)]
    [InlineData(6, 0, false)]
    [InlineData(6, 3, false)]
    [InlineData(6, 31, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(0, 2, false)]
    [InlineData(999, 999, false)]
    public void PreDutyCalibrationOnlyAcceptsRecognizedSupportedContent(uint category, uint intendedUse, bool expected)
    {
        Assert.Equal(expected, ContentCapturePolicy.SupportsPreDutyCalibration(category, intendedUse));
    }

    [Theory]
    [InlineData(2, 3)]
    [InlineData(21, 31)]
    [InlineData(0, 3)]
    [InlineData(0, 31)]
    public void DungeonMeterCaptureAcceptsPreDutyAndActiveDutyPackets(uint category, uint intendedUse)
    {
        var supportsPreDutyCalibration = ContentCapturePolicy.SupportsPreDutyCalibration(category, intendedUse);

        Assert.True(CaptureTimingPolicy.ShouldAcceptDamageParserPackets(
            isDutyCaptureActive: false, isPvPCaptureBlocked: false, supportsPreDutyCalibration));
        Assert.True(CaptureTimingPolicy.ShouldAcceptDamageParserPackets(
            isDutyCaptureActive: true, isPvPCaptureBlocked: false, supportsPreDutyCalibration));
    }

    [Theory]
    [InlineData(30, 4)] // Variant.
    [InlineData(30, 57)] // Criterion.
    [InlineData(30, 58)] // Criterion Savage.
    [InlineData(0, 4)] // Variant/Criterion territories without a duty row.
    [InlineData(0, 57)]
    [InlineData(0, 58)]
    [InlineData(4, 10)] // Trials, including Extreme.
    [InlineData(4, 3)] // Battle on the Big Bridge / Battle in the Big Keep.
    [InlineData(5, 8)] // Alliance raids.
    [InlineData(5, 16)]
    [InlineData(5, 17)] // Raids, including Savage.
    [InlineData(28, 17)] // Ultimate, including DMU.
    [InlineData(37, 36)] // Chaotic alliance raid.
    [InlineData(2, 3)]
    [InlineData(21, 31)]
    [InlineData(0, 3)]
    [InlineData(0, 31)]
    public void SupportedContentStillHonorsPvpCaptureBlock(uint category, uint intendedUse)
    {
        var supportsPreDutyCalibration = ContentCapturePolicy.SupportsPreDutyCalibration(category, intendedUse);

        Assert.True(supportsPreDutyCalibration);
        Assert.False(CaptureTimingPolicy.ShouldAcceptDamageParserPackets(
            isDutyCaptureActive: false, isPvPCaptureBlocked: true, supportsPreDutyCalibration));
        Assert.False(CaptureTimingPolicy.ShouldAcceptDamageParserPackets(
            isDutyCaptureActive: true, isPvPCaptureBlocked: true, supportsPreDutyCalibration));
    }
}
