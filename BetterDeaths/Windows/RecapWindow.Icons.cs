using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System;
using System.Numerics;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private static Vector2 GetIconTextSize(FontAwesomeIcon icon)
    {
        using var font = Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push();
        return ImGui.CalcTextSize(icon.ToIconString());
    }

    private static float GetThemedIconButtonWidth(FontAwesomeIcon icon, string label)
    {
        var width = GetIconTextSize(icon).X;
        if (label.Length > 0)
            width += ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize(label).X;
        return width + ImGui.GetStyle().FramePadding.X * 2 + 12;
    }

    private static bool DrawThemedActionButton(FontAwesomeIcon icon, string label, string id, float width = 0)
    {
        var clicked = DrawThemedActionButton(string.Empty, id, width > 0 ? width : GetThemedIconButtonWidth(icon, label));
        DrawThemedButtonContents(icon, label, GetButtonTextColor(ModernNavButtonSelectedColor, selected: true));
        return clicked;
    }

    private static bool DrawThemedToggleButton(FontAwesomeIcon icon, string label, string id, bool selected, float width = 0)
    {
        var clicked = DrawThemedToggleButton(string.Empty, id, selected, width > 0 ? width : GetThemedIconButtonWidth(icon, label));
        DrawThemedButtonContents(icon, label, selected ? GetButtonTextColor(ModernNavButtonSelectedColor, selected: true) : ModernTextColor);
        return clicked;
    }

    private static void DrawThemedButtonContents(FontAwesomeIcon icon, string label, Vector4 color)
    {
        ImFontPtr iconFont;
        float fontSize;
        Vector2 iconSize;
        var glyph = icon.ToIconString();
        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            iconFont = ImGui.GetFont();
            fontSize = ImGui.GetFontSize();
            iconSize = ImGui.CalcTextSize(glyph);
        }
        var labelSize = ImGui.CalcTextSize(label);
        var gap = label.Length > 0 ? ImGui.GetStyle().ItemInnerSpacing.X : 0;
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var centerY = (min.Y + max.Y) * .5f;
        var x = min.X + MathF.Max(0, (max.X - min.X - iconSize.X - gap - labelSize.X) * .5f);
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(min, max, true);
        draw.AddText(iconFont, fontSize, new(x, centerY - iconSize.Y * .5f), ImGui.GetColorU32(color), glyph);
        if (label.Length > 0)
            draw.AddText(new(x + iconSize.X + gap, centerY - labelSize.Y * .5f), ImGui.GetColorU32(color), label);
        draw.PopClipRect();
    }
}
