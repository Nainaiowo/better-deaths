using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace BetterDeaths.Windows;

internal static class MeterHeaderBackgroundRenderer
{
    public static void Draw(BetterDeathsTheme theme, Vector2 position, Vector2 size, Vector4 accent, Vector4 secondary,
        bool animated, float strength, float opacity = 1, bool thumbnail = false)
    {
        strength = ThemeMotionPolicy.NormalizeStrength(strength);
        opacity = float.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 0;
        if (strength <= 0 || opacity <= 0 || !MeterHeaderMotionPolicy.ValidSize(size))
            return;

        var draw = ImGui.GetWindowDrawList();
        if (!MeterHeaderMotionPolicy.IntersectsClip(position, size, draw.GetClipRectMin(), draw.GetClipRectMax()))
            return;

        var phase = MeterHeaderMotionPolicy.Phase(ImGui.GetTime(), animated, Plugin.PluginInterface.UiBuilder.ShouldUseReducedMotion);
        var scale = MeterHeaderMotionPolicy.NormalizeScale(ImGui.GetTextLineHeight() / 17);
        var kind = MeterThemeStyle.Profile(theme).Background;
        var alpha = strength * opacity;
        draw.PushClipRect(position, position + size, true);

        var layered = kind is MeterBackgroundKind.Liquid or MeterBackgroundKind.Depth or MeterBackgroundKind.Clouds;
        var folded = kind is MeterBackgroundKind.Fabric or MeterBackgroundKind.Satin or MeterBackgroundKind.Ribbons or MeterBackgroundKind.Candy;
        var lanes = thumbnail ? 1 : kind is MeterBackgroundKind.Marble or MeterBackgroundKind.Brushed ? 7 : 3;
        var segments = thumbnail ? 6 : MeterHeaderMotionPolicy.Segments;
        for (var lane = 0; lane < lanes; lane++)
        {
            var color = lane % 2 == 0 ? accent : secondary;
            var packed = ImGui.GetColorU32(color with { W = alpha * (layered ? .08f : folded ? .10f : .12f) });
            var previous = position + MeterHeaderMotionPolicy.Contour(theme, size, lane, 0, phase, segments);
            for (var segment = 1; segment <= segments; segment++)
            {
                var next = position + MeterHeaderMotionPolicy.Contour(theme, size, lane, segment, phase, segments);
                if (layered)
                    draw.AddQuadFilled(previous, next, new Vector2(next.X, position.Y + size.Y), new Vector2(previous.X, position.Y + size.Y), packed);
                else if (folded)
                {
                    var thickness = new Vector2(0, (kind == MeterBackgroundKind.Candy ? 9 : 5) * scale);
                    draw.AddQuadFilled(previous, next, next + thickness, previous + thickness, packed);
                    draw.AddLine(previous, next, ImGui.GetColorU32(color with { W = alpha * .13f }), scale);
                }
                else
                    draw.AddLine(previous, next, packed, scale);
                previous = next;
            }
        }

        if (kind is MeterBackgroundKind.Moonlight or MeterBackgroundKind.Daylight or MeterBackgroundKind.Brushed or MeterBackgroundKind.Marble)
            DrawLight(draw, position, size, secondary, phase, alpha, thumbnail ? 1 : 3);
        if (kind == MeterBackgroundKind.Weave)
            DrawWeave(draw, position, size, accent, phase, alpha, scale, thumbnail ? 4 : 20);

        if (kind is MeterBackgroundKind.Petals or MeterBackgroundKind.Leaves or MeterBackgroundKind.Heat or
            MeterBackgroundKind.Fizz or MeterBackgroundKind.Citrus or MeterBackgroundKind.Candy or MeterBackgroundKind.Moonlight)
        {
            var count = thumbnail ? 3 : MeterHeaderMotionPolicy.ParticleCount;
            for (var index = 0; index < count; index++)
            {
                var particle = MeterHeaderMotionPolicy.Particle(theme, thumbnail ? index * 5 : index, size, phase, scale);
                var center = position + particle.Center;
                var color = index % 3 == 0 ? secondary : accent;
                var packed = ImGui.GetColorU32(color with { W = alpha * particle.Opacity * .36f });
                var r = particle.Radius;
                if (kind is MeterBackgroundKind.Petals or MeterBackgroundKind.Leaves)
                {
                    var length = kind == MeterBackgroundKind.Leaves ? 1.8f : 1.3f;
                    var sin = MathF.Sin(particle.Rotation);
                    var cos = MathF.Cos(particle.Rotation);
                    draw.PathClear();
                    var points = thumbnail ? 6 : 12;
                    for (var point = 0; point < points; point++)
                    {
                        var angle = point / (float)points * MathF.Tau;
                        var local = new Vector2(MathF.Cos(angle) * r * length, MathF.Sin(angle) * r * (.60f + .22f * MathF.Cos(angle)));
                        draw.PathLineTo(center + new Vector2(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos));
                    }
                    draw.PathFillConvex(packed);
                    draw.AddLine(center - new Vector2(cos, sin) * r * .8f, center + new Vector2(cos, sin) * r * .5f,
                        ImGui.GetColorU32(color with { W = alpha * particle.Opacity * .16f }), scale);
                }
                else if (kind is MeterBackgroundKind.Fizz or MeterBackgroundKind.Citrus)
                {
                    draw.AddCircle(center, r * .55f, packed, thumbnail ? 6 : 12, scale);
                    if (index % 3 == 0)
                        draw.AddLine(center + new Vector2(0, r * 2), center + new Vector2(0, r * 3.5f), packed, scale);
                }
                else if (kind == MeterBackgroundKind.Moonlight)
                {
                    draw.AddLine(center - new Vector2(r * .55f, 0), center + new Vector2(r * .55f, 0), packed, scale);
                    draw.AddLine(center - new Vector2(0, r), center + new Vector2(0, r), packed, scale);
                }
                else
                {
                    var tip = new Vector2(MathF.Cos(particle.Rotation), MathF.Sin(particle.Rotation)) * r;
                    draw.AddLine(center - tip * .5f, center + tip * .5f, packed, scale * (kind == MeterBackgroundKind.Candy ? 2 : 1));
                }
            }
        }
        draw.PopClipRect();
    }

    private static void DrawLight(ImDrawListPtr draw, Vector2 position, Vector2 size, Vector4 color, float phase, float alpha, int count)
    {
        // Soft, wide bands of light, not a flashing highlight or a moving foreground element.
        var drift = MathF.Sin(phase * MathF.Tau) * size.X * .06f;
        for (var band = 0; band < count; band++)
        {
            var x = position.X + size.X * (.48f + band * .19f) + drift;
            var width = size.X * .10f;
            var slope = size.X * .12f;
            draw.AddQuadFilled(new(x, position.Y), new(x + width, position.Y),
                new(x + width - slope, position.Y + size.Y), new(x - slope, position.Y + size.Y),
                ImGui.GetColorU32(color with { W = alpha * .055f }));
        }
    }

    private static void DrawWeave(ImDrawListPtr draw, Vector2 position, Vector2 size, Vector4 color, float phase, float alpha, float scale, int count)
    {
        var offset = MathF.Sin(phase * MathF.Tau) * 3 * scale;
        var packed = ImGui.GetColorU32(color with { W = alpha * .09f });
        for (var line = 0; line < count; line++)
        {
            var x = position.X + size.X * line / (count - 1) + offset;
            draw.AddLine(new(x, position.Y), new(x - size.Y, position.Y + size.Y), packed, scale);
            draw.AddLine(new(x, position.Y + size.Y), new(x - size.Y, position.Y), packed, scale);
        }
    }
}
