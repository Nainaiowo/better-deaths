using System;

namespace BetterDeaths;

internal readonly record struct HudColumnLayout(bool Split, float EditorWidth, float PreviewWidth);
internal readonly record struct HudNavigationLayout(float Inset, int Columns, float ButtonWidth);

internal static class HudLayoutPolicy
{
    public static HudNavigationLayout NavigationLayout(float availableWidth, float buttonWidth, float gap, int count, float inset)
    {
        var width = float.IsFinite(availableWidth) ? MathF.Max(1, availableWidth) : 1;
        var minimum = float.IsFinite(buttonWidth) ? MathF.Max(1, buttonWidth) : 1;
        var spacing = float.IsFinite(gap) ? MathF.Max(0, gap) : 0;
        var padding = float.IsFinite(inset) ? Math.Clamp(inset, 0, (width - 1) * .5f) : 0;
        var inner = width - padding * 2;
        var columns = NavigationColumns(inner, minimum, spacing, count);
        return new(padding, columns, columns == 0 ? 0 : MathF.Min(minimum, (inner - (columns - 1) * spacing) / columns));
    }

    public static int NavigationColumns(float availableWidth, float buttonWidth, float gap, int count)
    {
        if (count <= 0)
            return 0;
        var width = float.IsFinite(availableWidth) ? MathF.Max(1, availableWidth) : 1;
        var minimum = float.IsFinite(buttonWidth) ? MathF.Max(1, buttonWidth) : 1;
        var spacing = float.IsFinite(gap) ? MathF.Max(0, gap) : 0;
        return Math.Clamp((int)MathF.Floor((width + spacing) / (minimum + spacing)), 1, count);
    }

    public static HudColumnLayout PartyCooldownColumns(float availableWidth, float lineHeight, float gap)
    {
        var width = float.IsFinite(availableWidth) ? MathF.Max(1, availableWidth) : 1;
        var line = float.IsFinite(lineHeight) ? MathF.Max(1, lineHeight) : 17;
        var spacing = float.IsFinite(gap) ? MathF.Max(0, gap) : 0;
        var editorMinimum = MathF.Max(420, line * 24);
        var previewMinimum = MathF.Max(300, line * 20);
        if (width < editorMinimum + previewMinimum + spacing)
            return new(false, width, width);
        var inner = width - spacing;
        var previewMaximum = MathF.Min(MathF.Max(420, previewMinimum), inner - editorMinimum);
        var preview = Math.Clamp(inner * .42f, previewMinimum, previewMaximum);
        return new(true, inner - preview, preview);
    }
}
