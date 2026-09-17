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

    private static float DrawWidgetSummaryMetric(Vector2 position, float width, string label, string value,
        float textScale, Vector4 labelColor, Vector4 valueColor)
    {
        var draw = ImGui.GetWindowDrawList();
        draw.AddText(position, ImGui.GetColorU32(labelColor), label);
        var valuePosition = position + new Vector2(0, ImGui.GetTextLineHeight() + 2);
        width = MathF.Max(1, width);
        var unwrapped = MeasureWidgetSummaryValue(value, textScale);
        var wrapWidth = unwrapped.X <= width ? 0 : width;
        var valueSize = wrapWidth == 0 ? unwrapped : MeasureWidgetSummaryValue(value, textScale, wrapWidth);
        draw.PushClipRect(valuePosition, valuePosition + new Vector2(width, valueSize.Y), true);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * textScale, valuePosition, ImGui.GetColorU32(valueColor), value, wrapWidth);
        draw.PopClipRect();
        return valuePosition.Y - position.Y + valueSize.Y;
    }
}
