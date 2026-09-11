namespace BetterDeaths;

using System;
using System.Globalization;

internal static class DamageMeterDisplayPolicy
{
    private static readonly string[] Suffixes = ["", "k", "m", "b", "t"];

    public static string FormatNumber(double value, bool concise, IFormatProvider? provider = null)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            return "-";
        }

        provider ??= CultureInfo.CurrentCulture;
        if (!concise)
        {
            return value.ToString("N0", provider);
        }

        var unit = 0;
        while (value >= 1000.0 && unit < Suffixes.Length - 1)
        {
            value /= 1000.0;
            unit++;
        }

        var rounded = Math.Round(value, unit == 0 ? 0 : 1, MidpointRounding.AwayFromZero);
        if (rounded >= 1000.0 && unit < Suffixes.Length - 1)
        {
            rounded /= 1000.0;
            unit++;
        }

        return rounded.ToString(unit == 0 ? "N0" : "0.0", provider) + Suffixes[unit];
    }

    public static float GetColumnWidth(DamageMeterColumn column, float headerWidth, float contentWidth,
        bool concise, float scale)
    {
        var maximum = column is DamageMeterColumn.PlayerName or DamageMeterColumn.MaxHitName
            ? (concise ? 180.0f : 320.0f) * scale
            : float.MaxValue;
        var width = MathF.Max(headerWidth, MathF.Min(contentWidth, maximum)) + 12.0f * scale;
        // Use small width steps so changing digits do not move column boundaries every frame.
        var step = 8.0f * scale;
        return MathF.Ceiling(width / step) * step;
    }
}
