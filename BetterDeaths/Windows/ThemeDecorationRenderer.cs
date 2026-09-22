using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace BetterDeaths.Windows;

internal static class ThemeDecorationRenderer
{
    public static void DrawAtmosphere(BetterDeathsUiTheme theme, Vector2 position, Vector2 size,
        bool animated, float strength, float opacity = 1, bool compact = false)
    {
        if (theme.Decoration == ThemeDecoration.None || size.X < 160 || size.Y < 60)
            return;
        // Main-window accents stay at the edges; the meter has its own clipped header background.
        var lane = new Vector2(MathF.Min(30, size.X * .05f), size.Y);
        MeterHeaderBackgroundRenderer.Draw(theme.Id, position, lane, theme.ModernAccentColor, theme.LeadUpGoldColor,
            animated, strength, opacity * .2f);
        MeterHeaderBackgroundRenderer.Draw(theme.Id, position + new Vector2(size.X - lane.X, 0), lane,
            theme.ModernAccentColor, theme.LeadUpGoldColor, animated, strength, opacity * .2f);
    }

    public static void DrawSwatch(BetterDeathsUiTheme theme, Vector2 position, Vector2 size) =>
        MeterHeaderBackgroundRenderer.Draw(theme.Id, position, size, theme.ModernAccentColor, theme.LeadUpGoldColor, false, 1, thumbnail: true);
}
