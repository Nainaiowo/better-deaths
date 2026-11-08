using BetterDeaths.PartyCooldowns;

namespace BetterDeaths;

public sealed partial class Plugin
{
    private readonly PartyCooldownProfiler partyCooldownProfiler = new();
    private double nextPartyCooldownProfileAt;

    internal PartyCooldownProfiler.Sample MeasurePartyCooldown(PartyCooldownPhase phase) =>
        Configuration.ShowPartyCooldowns && Configuration.DebugProfilePartyCooldowns ? partyCooldownProfiler.Measure(phase) : default;

    internal PartyCooldownPerformance GetPartyCooldownPerformance(PartyCooldownPhase phase) => partyCooldownProfiler.Get(phase);

    internal void SetPartyCooldownProfiling(bool enabled)
    {
        Configuration.DebugProfilePartyCooldowns = enabled;
        partyCooldownProfiler.Reset();
        nextPartyCooldownProfileAt = PartyCooldownNow + 30;
        SaveConfiguration();
    }

    private void MaybeLogPartyCooldownPerformance()
    {
        if (!Configuration.ShowPartyCooldowns || !Configuration.DebugProfilePartyCooldowns)
            return;
        var now = PartyCooldownNow;
        if (nextPartyCooldownProfileAt == 0)
            nextPartyCooldownProfileAt = now + 30;
        if (now < nextPartyCooldownProfileAt)
            return;
        nextPartyCooldownProfileAt = now + 30;
        var update = partyCooldownProfiler.Get(PartyCooldownPhase.Update);
        var draw = partyCooldownProfiler.Get(PartyCooldownPhase.Draw);
        var capture = partyCooldownProfiler.Get(PartyCooldownPhase.Capture);
        Log.Information("Party cooldown CPU profile: update avg {Update:F4} ms, p99 {UpdateP99:F4} ms, max {UpdateMax:F4} ms, " +
            "{UpdateBytes:F1} B/call; draw avg {Draw:F4} ms, p99 {DrawP99:F4} ms, max {DrawMax:F4} ms, {DrawBytes:F1} B/call; " +
            "capture avg {Capture:F4} ms, max {CaptureMax:F4} ms; tracked members {Members}. GPU time is not included.",
            update.AverageMs, update.RecentP99Ms, update.MaximumMs, update.BytesPerCall,
            draw.AverageMs, draw.RecentP99Ms, draw.MaximumMs, draw.BytesPerCall,
            capture.AverageMs, capture.MaximumMs, PartyCooldownActorCount);
    }
}
