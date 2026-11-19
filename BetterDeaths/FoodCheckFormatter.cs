using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterDeaths;

internal static class FoodCheckFormatter
{
    public const uint WellFedStatusId = 48;

    public static StatusSnapshot? FindWellFedStatus(IEnumerable<StatusSnapshot> statuses) => statuses
        .Where(status => status.Id == WellFedStatusId)
        .OrderByDescending(status => float.IsFinite(status.RemainingTime) ? status.RemainingTime : float.NegativeInfinity)
        .FirstOrDefault();

    public static string FormatRemaining(StatusSnapshot? status)
    {
        if (status is null)
            return "Not active";
        return FormatRemainingSeconds(status.RemainingTime);
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

    public static float NormalizeHeightFraction(float value) => float.IsFinite(value)
        ? Math.Clamp(value, 0.20f, 1.0f)
        : DefaultHeightFraction;

    public static (float TopInset, float Height) GetCenteredVerticalBounds(float rowHeight, float heightFraction)
    {
        var safeRowHeight = float.IsFinite(rowHeight) ? MathF.Max(0, rowHeight) : 0;
        var tintHeight = safeRowHeight * NormalizeHeightFraction(heightFraction);
        return ((safeRowHeight - tintHeight) * 0.5f, tintHeight);
    }
}
