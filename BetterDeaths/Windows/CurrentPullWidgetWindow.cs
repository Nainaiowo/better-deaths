using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace BetterDeaths.Windows;

public sealed class CurrentPullWidgetWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly RecapWindow recapWindow;
    private bool stylePushed;

    public CurrentPullWidgetWindow(Plugin plugin, RecapWindow recapWindow)
        : base("Better Deaths Widget###BetterDeathsCurrentPullWidget")
    {
        this.plugin = plugin;
        this.recapWindow = recapWindow;

        Size = new Vector2(620.0f, 360.0f);
        SizeCondition = ImGuiCond.FirstUseEver;
        ApplyScrollbarWindowFlag();
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
        ApplyScrollbarWindowFlag();
        var opacity = Math.Clamp(
            plugin.Configuration.CurrentPullWidgetBackgroundOpacity <= 0.0f
                ? Plugin.CurrentPullWidgetMaxBackgroundOpacity
                : plugin.Configuration.CurrentPullWidgetBackgroundOpacity,
            Plugin.CurrentPullWidgetMinBackgroundOpacity,
            Plugin.CurrentPullWidgetMaxBackgroundOpacity);
        ImGui.SetNextWindowBgAlpha(opacity);
        PushWidgetStyle();
    }

    public override void PostDraw()
    {
        PopWidgetStyle();
    }

    public override void Draw()
    {
        recapWindow.DrawCurrentPullWidgetContent();
        DrawBottomLeftResizeGrip(BetterDeathsThemeCatalog.GetTheme(plugin.Configuration));
    }

    public override void OnClose()
    {
        plugin.NotifyCurrentPullWidgetClosed();
    }

    private void PushWidgetStyle()
    {
        if (stylePushed)
        {
            return;
        }

        var theme = BetterDeathsThemeCatalog.GetTheme(plugin.Configuration);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, theme.WidgetWindowBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, theme.WidgetTitleBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, theme.WidgetTitleActiveBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, theme.WidgetTitleBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Border, theme.WidgetBorderColor);
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, theme.WidgetResizeGripColor);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, theme.WidgetResizeGripHoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, theme.WidgetResizeGripActiveColor);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, theme.ModernPopupBgColor);
        ImGui.PushStyleColor(ImGuiCol.Text, theme.ModernTextColor);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, theme.ModernMutedTextColor);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, GetScrollbarBackgroundColor(theme));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, GetScrollbarGrabColor(theme));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, GetScrollbarGrabHoveredColor(theme));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, GetScrollbarGrabActiveColor(theme));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f);
        stylePushed = true;
    }

    private void PopWidgetStyle()
    {
        if (!stylePushed)
        {
            return;
        }

        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(15);
        stylePushed = false;
    }

    private void ApplyScrollbarWindowFlag()
    {
        if (plugin.Configuration.ShowScrollbars)
        {
            Flags &= ~ImGuiWindowFlags.NoScrollbar;
            return;
        }

        Flags |= ImGuiWindowFlags.NoScrollbar;
    }

    private static Vector4 GetScrollbarBackgroundColor(BetterDeathsUiTheme theme)
    {
        return UsesLightPanels(theme)
            ? BlendColors(theme.ModernPanelColor, theme.ModernPanelBorderColor, 0.16f) with { W = 0.76f }
            : BlendColors(theme.ModernShellColor, theme.ModernPanelColor, 0.58f) with { W = 0.70f };
    }

    private static Vector4 GetScrollbarGrabColor(BetterDeathsUiTheme theme)
    {
        return UsesLightPanels(theme)
            ? BlendColors(theme.ModernPanelBorderColor, theme.ModernAccentColor, 0.30f) with { W = 0.84f }
            : BlendColors(theme.ModernPanelBorderColor, theme.ModernAccentColor, 0.40f) with { W = 0.78f };
    }

    private static Vector4 GetScrollbarGrabHoveredColor(BetterDeathsUiTheme theme)
    {
        return BlendColors(GetScrollbarGrabColor(theme), theme.ModernAccentColor, 0.34f) with { W = 0.94f };
    }

    private static Vector4 GetScrollbarGrabActiveColor(BetterDeathsUiTheme theme)
    {
        return BlendColors(theme.ModernAccentColor, theme.ModernTextColor, UsesLightPanels(theme) ? 0.08f : 0.04f) with { W = 1.0f };
    }

    private static bool UsesLightPanels(BetterDeathsUiTheme theme)
    {
        return GetColorLuminance(theme.ModernPanelColor) >= 0.55f;
    }

    private static float GetColorLuminance(Vector4 color)
    {
        return (color.X * 0.2126f) + (color.Y * 0.7152f) + (color.Z * 0.0722f);
    }

    private static Vector4 BlendColors(Vector4 first, Vector4 second, float amount)
    {
        var clampedAmount = Math.Clamp(amount, 0.0f, 1.0f);
        return new Vector4(
            first.X + ((second.X - first.X) * clampedAmount),
            first.Y + ((second.Y - first.Y) * clampedAmount),
            first.Z + ((second.Z - first.Z) * clampedAmount),
            first.W + ((second.W - first.W) * clampedAmount));
    }

    private static void DrawBottomLeftResizeGrip(BetterDeathsUiTheme theme)
    {
        const float gripSize = 18.0f;
        const float inset = 5.0f;
        const float lineSpacing = 4.0f;
        const float thickness = 1.3f;

        var position = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var mousePosition = ImGui.GetIO().MousePos;
        var gripMin = new Vector2(position.X, position.Y + size.Y - gripSize);
        var gripMax = new Vector2(position.X + gripSize, position.Y + size.Y);
        var hovered = mousePosition.X >= gripMin.X &&
            mousePosition.X <= gripMax.X &&
            mousePosition.Y >= gripMin.Y &&
            mousePosition.Y <= gripMax.Y;
        var color = ImGui.GetColorU32(
            hovered
                ? ImGui.IsMouseDown(ImGuiMouseButton.Left)
                    ? theme.WidgetResizeGripActiveColor
                    : theme.WidgetResizeGripHoveredColor
                : theme.WidgetResizeGripColor);
        var origin = new Vector2(position.X + inset, position.Y + size.Y - inset);
        var drawList = ImGui.GetWindowDrawList();

        for (var lineIndex = 0; lineIndex < 3; lineIndex++)
        {
            var offset = lineIndex * lineSpacing;
            drawList.AddLine(
                new Vector2(origin.X + offset, origin.Y),
                new Vector2(origin.X, origin.Y - offset),
                color,
                thickness);
        }
    }
}
