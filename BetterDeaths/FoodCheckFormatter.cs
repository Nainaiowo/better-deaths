using System;
using System.Collections.Generic;
using System.Numerics;

namespace BetterDeaths;

internal static class FoodCheckFormatter
{
    public const uint WellFedStatusId = 48;

    public static IEnumerable<string> PartyChatLines(IEnumerable<(string Name, PartyFoodStatusSample Status)> members,
        int warningMinutes)
    {
        var threshold = Math.Clamp(warningMinutes, 1, 60) * 60;
        var missing = new List<string>();
        var expiring = new List<string>();
        foreach (var (name, status) in members)
        {
            if (!status.HasWellFed || status.RemainingSeconds == 0)
                missing.Add(name);
            else if (status.RemainingSeconds > 0 &&
                FoodCheckIndicatorPolicy.Classify(true, true, status.RemainingSeconds, threshold) == FoodCheckIndicatorState.Expiring)
                expiring.Add(name);
        }
        if (missing.Count > 0) yield return "No food: " + string.Join(", ", missing);
        if (expiring.Count > 0) yield return "Food expiring soon: " + string.Join(", ", expiring);
    }

    public static string FormatRemainingSeconds(float remainingTime)
    {
        if (!float.IsFinite(remainingTime) || remainingTime <= 0.0f)
            return "Time unavailable";

        var totalSeconds = (int)Math.Ceiling(remainingTime);
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes}:{seconds:00}";
    }
}

internal enum FoodCheckIndicatorState : byte
{
    Hidden,
    Active,
    Expiring,
    Missing,
}

internal readonly record struct PartyFoodStatusSample(bool HasWellFed, float RemainingSeconds);

internal sealed class PartyFoodStatusSnapshot
{
    public static readonly PartyFoodStatusSnapshot Empty = new([]);
    private readonly Dictionary<uint, PartyFoodStatusSample> samples;
    private readonly PartyFoodStatusSample mostUrgent;

    public PartyFoodStatusSnapshot(Dictionary<uint, PartyFoodStatusSample> samples)
    {
        this.samples = samples;
        mostUrgent = new(true, float.PositiveInfinity);
        foreach (var sample in samples.Values)
        {
            if (!sample.HasWellFed)
            {
                mostUrgent = sample;
                break;
            }
            if (float.IsFinite(sample.RemainingSeconds) && sample.RemainingSeconds < mostUrgent.RemainingSeconds)
                mostUrgent = sample;
        }
    }

    // This uses the same threshold as individual markers, without walking the party on every draw.
    public bool HasVisibleMarkers(bool warningsOnly, int thresholdSeconds) =>
        FoodCheckIndicatorPolicy.ForDisplay(
            FoodCheckIndicatorPolicy.Classify(samples.Count != 0, mostUrgent.HasWellFed, mostUrgent.RemainingSeconds, thresholdSeconds),
            warningsOnly) != FoodCheckIndicatorState.Hidden;

    public bool TryGet(uint entityId, out PartyFoodStatusSample sample) => samples.TryGetValue(entityId, out sample);
}

internal static class FoodCheckIndicatorPolicy
{
    public static FoodCheckIndicatorState ForDisplay(FoodCheckIndicatorState state, bool warningsOnly) =>
        warningsOnly && state == FoodCheckIndicatorState.Active ? FoodCheckIndicatorState.Hidden : state;

    public static FoodCheckIndicatorState Classify(bool statusDataAvailable, bool hasWellFed, float remainingSeconds,
        int warningThresholdSeconds)
    {
        if (!statusDataAvailable)
            return FoodCheckIndicatorState.Hidden;
        if (!hasWellFed)
            return FoodCheckIndicatorState.Missing;
        if (!float.IsFinite(remainingSeconds))
            return FoodCheckIndicatorState.Active;
        return remainingSeconds <= Math.Clamp(warningThresholdSeconds, 60, 3600)
            ? FoodCheckIndicatorState.Expiring
            : FoodCheckIndicatorState.Active;
    }
}

internal static class FoodCheckTintPolicy
{
    public const float DefaultHeightFraction = 0.65f;
    public const float MaximumOffsetY = 50.0f;

    public static float NormalizeHeightFraction(float value) => float.IsFinite(value)
        ? Math.Clamp(value, 0.20f, 1.0f)
        : DefaultHeightFraction;

    public static float NormalizeOffsetY(float value) => float.IsFinite(value)
        ? Math.Clamp(value, -MaximumOffsetY, MaximumOffsetY)
        : 0.0f;

    public static (float TopInset, float Height) GetVerticalBounds(float rowHeight, float heightFraction, float offsetY = 0)
    {
        var safeRowHeight = float.IsFinite(rowHeight) ? MathF.Max(0, rowHeight) : 0;
        var tintHeight = safeRowHeight * NormalizeHeightFraction(heightFraction);
        return ((safeRowHeight - tintHeight) * 0.5f + NormalizeOffsetY(offsetY), tintHeight);
    }
}

internal static class FoodCheckMarkerPolicy
{
    public const float MinimumIconSize = 16.0f;
    public const float MaximumIconSize = 72.0f;
    public const float MaximumOffset = 300.0f;

    public static float NormalizeIconSize(float value, float fallback) => float.IsFinite(value)
        ? Math.Clamp(value, MinimumIconSize, MaximumIconSize)
        : float.IsFinite(fallback) ? Math.Clamp(fallback, MinimumIconSize, MaximumIconSize) : 24.0f;

    public static float NormalizeOffset(float value) => float.IsFinite(value)
        ? Math.Clamp(value, -MaximumOffset, MaximumOffset)
        : 0.0f;

    public static Vector2 PositionInCooldownSlot(Vector2 slotPosition, float slotSize, float iconSize, float scale,
        float offsetX, float offsetY)
    {
        var safeScale = float.IsFinite(scale) && scale > 0 ? scale : 1.0f;
        var slotSizePx = NormalizeIconSize(slotSize, 24.0f) * safeScale;
        var iconSizePx = NormalizeIconSize(iconSize, slotSize) * safeScale;
        var offset = new Vector2(NormalizeOffset(offsetX), NormalizeOffset(offsetY)) * safeScale;
        return slotPosition + new Vector2((slotSizePx - iconSizePx) * 0.5f) + offset;
    }
}
