namespace BetterDeaths;

using System;
using System.Collections.Generic;
using System.Globalization;
using BetterDeaths.DamageParsing;

internal static class DamageMeterDisplayPolicy
{
    private static readonly string[] Suffixes = ["", "k", "m", "b", "t"];

    public static string FormatPlayerName(DamageActorIdentity actor, IReadOnlyList<DamageSourceSummary> sources,
        bool redact)
    {
        if (!redact || !IsPlayerRow(actor))
        {
            return actor.Name;
        }

        // Number by captured identity, not damage rank or the separate death-review roster.
        var number = 1;
        foreach (var source in sources)
        {
            var other = source.Source;
            if (IsPlayerRow(other) && (other.EntityId < actor.EntityId ||
                (other.EntityId == actor.EntityId &&
                    string.Compare(other.Name, actor.Name, StringComparison.OrdinalIgnoreCase) < 0)))
            {
                number++;
            }
        }

        return $"Player {number}";
    }

    private static bool IsPlayerRow(DamageActorIdentity actor) =>
        !actor.IsLimitBreak && (actor.IsPlayer || actor.IsPartyMember);

    public static float GetWidgetBackgroundOpacity(float? opacity, float legacyOpacity)
    {
        var value = opacity ?? legacyOpacity;
        return Math.Clamp(float.IsFinite(value) && value > 0.0f ? value : 1.0f, 0.35f, 1.0f);
    }

    public static float GetWidgetIconSize(float size)
    {
        return Math.Clamp(float.IsFinite(size) && size > 0.0f ? size : 20.0f, 12.0f, 32.0f);
    }

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
