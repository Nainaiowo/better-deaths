using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace BetterDeaths.Windows;

internal static partial class MeterHeaderBackgroundRenderer
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

        // Each family owns its scene. Only liquid/fabric themes use flowing bands.
        switch (kind)
        {
            case MeterBackgroundKind.Fizz:
            case MeterBackgroundKind.Citrus:
            case MeterBackgroundKind.Liquid:
            case MeterBackgroundKind.Depth:
            case MeterBackgroundKind.Satin:
            case MeterBackgroundKind.Ribbons:
                DrawBands(draw, theme, position, size, accent, secondary, phase, alpha, thumbnail);
                break;
            case MeterBackgroundKind.Marble:
            case MeterBackgroundKind.Brushed:
                DrawMineral(draw, position, size, accent, secondary, phase, alpha, kind, thumbnail);
                break;
        }

        var count = MeterHeaderMotionPolicy.MotifCount(size, scale, thumbnail);
        for (var index = 0; index < count; index++)
        {
            var motif = MeterHeaderMotionPolicy.Motif(theme, index, size, phase, scale);
            var ink = new MotifInk(draw, position + motif.Center, motif.Radius, motif.Rotation,
                accent, secondary, alpha * motif.Opacity, thumbnail);
            switch (kind)
            {
                case MeterBackgroundKind.Fizz:
                    if (index == 1) Ice(ink); else Can(ink);
                    break;
                case MeterBackgroundKind.Citrus:
                    if (index == 1 && theme != BetterDeathsTheme.Lemonade) Can(ink);
                    else Citrus(ink);
                    break;
                case MeterBackgroundKind.Petals:
                    if (index != 1) Blossom(ink, theme == BetterDeathsTheme.Rose);
                    break;
                case MeterBackgroundKind.Candy:
                    if (index == 1) Lollipop(ink); else Candy(ink);
                    break;
                case MeterBackgroundKind.Liquid: Flask(ink); break;
                case MeterBackgroundKind.Bubblegum: Bubble(ink); break;
                case MeterBackgroundKind.Leaves: Sprig(ink, false); break;
                case MeterBackgroundKind.Mint: Sprig(ink, true); break;
                case MeterBackgroundKind.Wisteria: Wisteria(ink); break;
                case MeterBackgroundKind.Hearts: Heart(ink); break;
                case MeterBackgroundKind.Peach: Peach(ink); break;
                case MeterBackgroundKind.Grapes: Grapes(ink); break;
                case MeterBackgroundKind.Cotton: Cotton(ink); break;
                case MeterBackgroundKind.Banana: Banana(ink); break;
                case MeterBackgroundKind.Weave: Seed(ink); break;
                case MeterBackgroundKind.Fabric: Pillow(ink); break;
                case MeterBackgroundKind.Clouds: Cloud(ink); break;
                case MeterBackgroundKind.Heat: Flame(ink); break;
                case MeterBackgroundKind.Spice: Chili(ink); break;
                case MeterBackgroundKind.Moonlight:
                    if (index == 1 || count == 1) Crescent(ink);
                    break;
                case MeterBackgroundKind.Daylight:
                    if (index == 1 || count == 1) Sun(ink, phase);
                    else Cloud(ink);
                    break;
            }
        }

        DrawParticles(draw, theme, kind, position, size, accent, secondary, phase, alpha, scale, thumbnail);
        draw.PopClipRect();
    }

    private static void DrawParticles(ImDrawListPtr draw, BetterDeathsTheme theme, MeterBackgroundKind kind,
        Vector2 position, Vector2 size, Vector4 accent, Vector4 secondary, float phase, float alpha, float scale, bool thumbnail)
    {
        if (kind is MeterBackgroundKind.Satin or MeterBackgroundKind.Ribbons or MeterBackgroundKind.Brushed or
            MeterBackgroundKind.Marble or MeterBackgroundKind.Fabric or MeterBackgroundKind.Daylight)
            return;
        var count = thumbnail ? 2 : MeterHeaderMotionPolicy.ParticleCount;
        for (var index = 0; index < count; index++)
        {
            var particle = MeterHeaderMotionPolicy.Particle(theme, thumbnail ? index * 7 : index, size, phase, scale);
            var visibility = MeterHeaderMotionPolicy.DetailVisibility(particle.Center, size, thumbnail);
            var ink = new MotifInk(draw, position + particle.Center, particle.Radius, particle.Rotation,
                index % 3 == 0 ? secondary : accent, accent, alpha * particle.Opacity * visibility, thumbnail);
            switch (kind)
            {
                case MeterBackgroundKind.Petals:
                case MeterBackgroundKind.Wisteria: Petal(ink); break;
                case MeterBackgroundKind.Leaves:
                case MeterBackgroundKind.Mint: Leaf(ink, Vector2.Zero, 1.5f, .65f); break;
                case MeterBackgroundKind.Fizz:
                case MeterBackgroundKind.Citrus:
                case MeterBackgroundKind.Liquid:
                case MeterBackgroundKind.Bubblegum:
                case MeterBackgroundKind.Depth:
                    ink.Oval(Vector2.Zero, new(.6f), .65f, false);
                    break;
                case MeterBackgroundKind.Candy:
                    if (index % 3 == 0) Candy(ink);
                    else ink.Line(new(-.6f, 0), new(.6f, 0), .7f, .35f);
                    break;
                case MeterBackgroundKind.Hearts: Heart(ink); break;
                case MeterBackgroundKind.Moonlight:
                    ink.Line(new(-.7f, 0), new(.7f, 0), .8f);
                    ink.Line(new(0, -1), new(0, 1), .8f);
                    break;
                case MeterBackgroundKind.Heat:
                case MeterBackgroundKind.Spice:
                    ink.Quad(new(0, -1), new(.4f, 0), new(0, 1), new(-.4f, 0), .8f);
                    break;
                case MeterBackgroundKind.Clouds:
                case MeterBackgroundKind.Cotton:
                    ink.Oval(Vector2.Zero, new(1.5f, .55f), .25f);
                    break;
                default:
                    if (index % 3 == 0) Leaf(ink, Vector2.Zero, 1, .6f);
                    break;
            }
        }
    }

    private static void DrawBands(ImDrawListPtr draw, BetterDeathsTheme theme, Vector2 position, Vector2 size,
        Vector4 accent, Vector4 secondary, float phase, float alpha, bool thumbnail)
    {
        var kind = MeterThemeStyle.Profile(theme).Background;
        var fabric = kind is MeterBackgroundKind.Satin or MeterBackgroundKind.Ribbons;
        var segments = thumbnail ? 5 : 24;
        var lanes = thumbnail ? 1 : 3;
        for (var lane = 0; lane < lanes; lane++)
        {
            var color = lane % 2 == 0 ? accent : secondary;
            var fill = ImGui.GetColorU32(color with { W = alpha * (fabric ? .15f : .13f) });
            var line = ImGui.GetColorU32(color with { W = alpha * .3f });
            var previous = position + MeterHeaderMotionPolicy.Contour(theme, size, lane, 0, phase, segments);
            for (var segment = 1; segment <= segments; segment++)
            {
                var next = position + MeterHeaderMotionPolicy.Contour(theme, size, lane, segment, phase, segments);
                var bottom = fabric ? MathF.Min(size.Y * .16f, 14) : size.Y;
                draw.AddQuadFilled(previous, next, new(next.X, fabric ? next.Y + bottom : position.Y + bottom),
                    new(previous.X, fabric ? previous.Y + bottom : position.Y + bottom), fill);
                draw.AddLine(previous, next, line);
                previous = next;
            }
        }
    }

    private static void DrawMineral(ImDrawListPtr draw, Vector2 position, Vector2 size, Vector4 accent,
        Vector4 secondary, float phase, float alpha, MeterBackgroundKind kind, bool thumbnail)
    {
        var count = thumbnail ? 2 : 6;
        for (var lane = 0; lane < count; lane++)
        {
            var x = size.X * (.3f + lane * .13f);
            var previous = position + new Vector2(x, 0);
            var color = ImGui.GetColorU32((lane % 2 == 0 ? accent : secondary) with { W = alpha * .24f });
            for (var step = 1; step <= (thumbnail ? 3 : 7); step++)
            {
                var y = size.Y * step / (thumbnail ? 3 : 7);
                var offset = kind == MeterBackgroundKind.Brushed ? -y * .65f :
                    MathF.Sin(lane * 2.7f + step * 1.6f) * size.Y * .2f;
                var next = position + new Vector2(x + offset, y);
                draw.AddLine(previous, next, color);
                if (!thumbnail && kind == MeterBackgroundKind.Marble && step % 2 == 0)
                    draw.AddLine(next, next + new Vector2(size.Y * .23f, -size.Y * .14f), color);
                previous = next;
            }
        }
        var drift = MathF.Sin(phase * MathF.Tau) * size.X * .08f;
        var start = position + new Vector2(size.X * .58f + drift, 0);
        draw.AddQuadFilled(start, start + new Vector2(size.X * .07f, 0),
            start + new Vector2(size.X * .07f - size.Y * .65f, size.Y), start + new Vector2(-size.Y * .65f, size.Y),
            ImGui.GetColorU32(secondary with { W = alpha * .07f }));
    }
}
