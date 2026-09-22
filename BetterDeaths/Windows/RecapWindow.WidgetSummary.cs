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

    private static float DrawWidgetSummaryValue(Vector2 position, float width, string value, float textScale, Vector4 valueColor)
    {
        var draw = ImGui.GetWindowDrawList();
        width = MathF.Max(1, width);
        var unwrapped = MeasureWidgetSummaryValue(value, textScale);
        var wrapWidth = unwrapped.X <= width ? 0 : width;
        var valueSize = wrapWidth == 0 ? unwrapped : MeasureWidgetSummaryValue(value, textScale, wrapWidth);
        draw.PushClipRect(position, position + new Vector2(width, valueSize.Y), true);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * textScale, position, ImGui.GetColorU32(valueColor), value, wrapWidth);
        draw.PopClipRect();
        return valueSize.Y;
    }
}
