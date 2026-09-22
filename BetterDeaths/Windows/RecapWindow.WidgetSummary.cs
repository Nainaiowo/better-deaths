using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace BetterDeaths.Windows;

public sealed partial class RecapWindow
{
    private static Vector2 MeasureWidgetSummaryValue(string value, float textScale, float wrapWidth = 0)
    {
        // Measure at the actual draw font size; multiplying a rounded base-font width can clip the last digit.
        var size = ImGui.CalcTextSizeA(ImGui.GetFont(), ImGui.GetFontSize() * textScale, float.MaxValue, wrapWidth, value, out _);
        return wrapWidth > 0 ? size : size with { X = MathF.Ceiling(size.X) + 2 * textScale };
    }

    private static float DrawWidgetSummaryValue(Vector2 position, float width, string value, float textScale, Vector4 valueColor, bool outline = false)
    {
        var draw = ImGui.GetWindowDrawList();
        width = MathF.Max(1, width);
        var unwrapped = MeasureWidgetSummaryValue(value, textScale);
        var wrapWidth = unwrapped.X <= width ? 0 : width;
        var valueSize = wrapWidth == 0 ? unwrapped : MeasureWidgetSummaryValue(value, textScale, wrapWidth);
        var outlineOffset = outline ? MathF.Max(0.75f, textScale * 0.75f) : 0;
        draw.PushClipRect(position - new Vector2(outlineOffset), position + new Vector2(width + outlineOffset, valueSize.Y + outlineOffset), true);
        if (outline)
        {
            var outlineColor = ImGui.GetColorU32(new Vector4(0.04f, 0.05f, 0.06f, 0.68f));
            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize() * textScale;
            draw.AddText(font, fontSize, position + new Vector2(-outlineOffset, 0), outlineColor, value, wrapWidth);
            draw.AddText(font, fontSize, position + new Vector2(outlineOffset, 0), outlineColor, value, wrapWidth);
            draw.AddText(font, fontSize, position + new Vector2(0, -outlineOffset), outlineColor, value, wrapWidth);
            draw.AddText(font, fontSize, position + new Vector2(0, outlineOffset), outlineColor, value, wrapWidth);
        }
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * textScale, position, ImGui.GetColorU32(valueColor), value, wrapWidth);
        draw.PopClipRect();
        return valueSize.Y;
    }
}
