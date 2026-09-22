using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private static Vector4 GetWidgetTableHeaderBackground() =>
        activeTheme.WidgetSurface == WidgetSurfaceStyle.Confection
            ? ConfectionWidgetStyle.ColumnHeader(activeTheme.WidgetWindowBackgroundColor)
            : activeTheme.Decoration == ThemeDecoration.Sakura ? SakuraWidgetStyle.ColumnHeader(activeTheme.WidgetWindowBackgroundColor)
            : WidgetSurfacePolicy.IsExpressive(activeTheme.WidgetSurface) ? Vector4.Zero : ModernPanelAltColor;

    private void DrawWidgetTableHeader(params string[] labels)
    {
        var color = activeTheme.WidgetSurface == WidgetSurfaceStyle.Confection || activeTheme.Decoration == ThemeDecoration.Sakura
            ? WidgetSurfacePolicy.ReadableText(ModernTextColor, GetWidgetTableHeaderBackground())
            : WidgetSurfacePolicy.IsExpressive(activeTheme.WidgetSurface)
            ? WidgetSurfacePolicy.ReadableText(ModernMutedTextColor, activeTheme.WidgetWindowBackgroundColor)
            : ModernTextColor;
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        DrawCenteredTableHeader(labels);
        ImGui.PopStyleColor();
    }

    private static float GetStyleWidgetGutter() => activeTheme.WidgetSurface == WidgetSurfaceStyle.Confection || activeTheme.Decoration == ThemeDecoration.Sakura ? 0 : WidgetSurfacePolicy.IsExpressive(activeTheme.WidgetSurface)
        ? WidgetSurfacePolicy.GetEdgeGutter(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight() / 17)
        : 0;

    private void DrawStyleThemeBackdrop(float opacity, bool widget = false)
    {
        // Meter backgrounds are drawn inside the summary, never beside the data rows.
        if (widget)
            return;
        var position = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();
        ThemeDecorationRenderer.DrawAtmosphere(activeTheme, position, size, configuration.AnimateStyleThemes,
            configuration.StyleThemeEffectStrength, opacity);
    }

    private void DrawStyleThemeEffectsSettings()
    {
        var animated = configuration.AnimateStyleThemes;
        if (DrawThemedCheckbox("Animate details", ref animated))
        {
            configuration.AnimateStyleThemes = animated;
            plugin.SaveConfiguration();
        }
        DrawSettingsTooltip("Gentle motion in meter headers and style-theme details. Turn off to keep them still. Respects reduced motion and does not affect combat recording.");
        ImGui.TextUnformatted("Effect strength");
        ImGui.SetNextItemWidth(-1);
        var strength = ThemeMotionPolicy.NormalizeStrength(configuration.StyleThemeEffectStrength);
        if (ImGui.SliderFloat("##StyleThemeEffectStrength", ref strength, 0, 1, "%.2f"))
            configuration.StyleThemeEffectStrength = strength;
        if (ImGui.IsItemDeactivatedAfterEdit())
            plugin.SaveConfiguration();
        DrawSettingsTooltip("Controls the header background details. Zero hides the effects.");

        var size = new Vector2(MathF.Max(1, ImGui.GetContentRegionAvail().X), 72);
        var position = ImGui.GetCursorScreenPos();
        var surface = MeterThemeStyle.Summary(activeTheme.Id, activeTheme.WidgetWindowBackgroundColor);
        ImGui.GetWindowDrawList().AddRectFilled(position, position + size, ImGui.GetColorU32(surface));
        MeterHeaderBackgroundRenderer.Draw(activeTheme.Id, position, size, ModernAccentColor, LeadUpGoldColor, animated, strength);
        ImGui.Dummy(size);
    }

    private void DrawStyledWidgetTitle(string title)
    {
        ImGui.Dummy(new Vector2(1, 6));
        var position = ImGui.GetCursorScreenPos();
        var width = MathF.Max(1, ImGui.GetContentRegionAvail().X - GetStyleWidgetGutter());
        const float signature = 0;
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(1, width - signature));
        DrawModernWidgetTitle(title, WidgetSurfacePolicy.ReadableText(ModernTextColor, activeTheme.WidgetWindowBackgroundColor));
        ImGui.PopTextWrapPos();
        var height = MathF.Max(ImGui.GetCursorScreenPos().Y - position.Y, signature > 0 ? 40 : 0);
        FinishStyledWidgetSummary(position, width, height, signature);
    }

    private void DrawStyledDamageMeterHeader(string state, double duration, double dps, bool showControls)
    {
        DrawCatalogDamageMeterHeader(state, duration, dps, showControls);
    }

    private void FinishStyledWidgetSummary(Vector2 position, float width, float contentHeight, float signature)
    {
        var height = MathF.Max(contentHeight, signature > 0 ? 48 : 0);
        var drawList = ImGui.GetWindowDrawList();
        var baseline = position + new Vector2(0, height + 4);
        drawList.AddLine(baseline, baseline + new Vector2(width, 0), ImGui.GetColorU32(ModernPanelBorderColor with { W = 0.20f }));
        drawList.AddLine(baseline, baseline + new Vector2(MathF.Min(32, width * 0.12f), 0), ImGui.GetColorU32(ModernAccentColor), 2);
        ImGui.SetCursorScreenPos(position);
        ImGui.Dummy(new Vector2(width, height + 9));
    }

    private static Vector4 GetStyledRowOverlay(bool detail = false) => WidgetSurfacePolicy.GetRowOverlay(
        activeTheme.WidgetWindowBackgroundColor, ImGui.TableGetRowIndex(), detail);

    private WidgetRowColors DrawDamageMeterRowSurface(uint classJobId, int playerIndex, bool detail = false)
    {
        var text = detail ? ModernMutedTextColor : ModernTextColor;
        var colors = MeterThemeStyle.Row(activeTheme.Id, activeTheme.WidgetWindowBackgroundColor, text,
            plugin.GetClassJobDisplayRole(classJobId), playerIndex, detail);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(colors.Background with { W = GetDamageMeterWidgetBackgroundOpacity() }));
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, 0);
        return colors;
    }
}
