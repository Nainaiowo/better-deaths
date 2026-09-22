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

        // The header uses material and motion cues, not large illustrated objects behind the metrics.
        switch (kind)
        {
            case MeterBackgroundKind.Fizz:
            case MeterBackgroundKind.Citrus:
            case MeterBackgroundKind.Liquid:
            case MeterBackgroundKind.Depth:
            case MeterBackgroundKind.Satin:
            case MeterBackgroundKind.Ribbons:
            case MeterBackgroundKind.Fabric:
            case MeterBackgroundKind.Bubblegum:
            case MeterBackgroundKind.Candy:
                DrawBands(draw, theme, position, size, accent, secondary, phase, alpha, thumbnail);
                break;
            case MeterBackgroundKind.Marble:
            case MeterBackgroundKind.Brushed:
                DrawMineral(draw, position, size, accent, secondary, phase, alpha, kind, thumbnail);
                break;
            case MeterBackgroundKind.Daylight:
                DrawDaylight(draw, position, size, accent, phase, alpha, thumbnail);
                break;
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
        var count = thumbnail ? 2 : size.X < 7 * size.Y ? 3 : MeterHeaderMotionPolicy.ParticleCount;
        for (var index = 0; index < count; index++)
        {
            var particle = MeterHeaderMotionPolicy.Particle(theme, thumbnail ? index * 3 : index, size, phase, scale);
            var visibility = MeterHeaderMotionPolicy.DetailVisibility(particle.Center, size, thumbnail);
            var ink = new TextureInk(draw, position + particle.Center, particle.Radius, particle.Rotation,
                index % 3 == 0 ? secondary : accent, accent,
                MathF.Min(1, alpha * particle.Opacity * visibility * 1.4f), thumbnail);
            switch (kind)
            {
                case MeterBackgroundKind.Petals:
                    Petal(ink); break;
                case MeterBackgroundKind.Wisteria:
                    ink.Line(new(0, -1), new(0, .6f), .55f, .05f, true);
                    ink.Oval(new(0, .65f), new(.26f, .4f), .45f);
                    break;
                case MeterBackgroundKind.Leaves:
                case MeterBackgroundKind.Mint:
                    Leaf(ink, Vector2.Zero, 1.2f, kind == MeterBackgroundKind.Mint ? .55f : .3f);
                    break;
                case MeterBackgroundKind.Fizz:
                case MeterBackgroundKind.Liquid:
                case MeterBackgroundKind.Bubblegum:
                case MeterBackgroundKind.Depth:
                    ink.Oval(Vector2.Zero, new(.85f), .75f, false);
                    ink.Line(new(-.48f, -.25f), new(-.24f, -.49f), .45f, .05f, true);
                    break;
                case MeterBackgroundKind.Citrus:
                    ink.Oval(Vector2.Zero, new(.7f, .4f), .5f, false);
                    ink.Line(new(-.4f, .2f), new(.4f, -.2f), .38f, .04f, true);
                    break;
                case MeterBackgroundKind.Candy:
                    ink.Line(new(-.6f, .6f), new(.6f, -.6f), .6f, .12f, index % 2 == 0);
                    break;
                case MeterBackgroundKind.Hearts:
                    ink.Oval(Vector2.Zero, new(.7f, .35f), .5f, false);
                    break;
                case MeterBackgroundKind.Moonlight:
                    ink.Line(new(-.55f, 0), new(.55f, 0), .55f);
                    ink.Line(new(0, -.7f), new(0, .7f), .55f);
                    break;
                case MeterBackgroundKind.Heat:
                case MeterBackgroundKind.Spice:
                    ink.Line(new(-.35f, .8f), new(.1f, -.8f), .6f, .08f);
                    ink.Line(new(.1f, -.8f), new(.4f, -.3f), .45f, .06f, true);
                    break;
                case MeterBackgroundKind.Clouds:
                case MeterBackgroundKind.Cotton:
                    ink.Oval(Vector2.Zero, new(1.25f, .4f), .4f, false);
                    break;
                case MeterBackgroundKind.Weave:
                    ink.Line(new(-.7f, -.5f), new(.7f, .5f), .5f);
                    ink.Line(new(-.7f, .5f), new(.7f, -.5f), .5f, alternate: true);
                    break;
                case MeterBackgroundKind.Grapes:
                    ink.Oval(new(-.4f, 0), new(.25f), .45f, false);
                    ink.Oval(new(.3f, .3f), new(.25f), .45f, false, true);
                    break;
                case MeterBackgroundKind.Banana:
                    ink.Line(new(-.8f, .15f), new(0, -.35f), .55f);
                    ink.Line(new(0, -.35f), new(.8f, .15f), .55f);
                    break;
                case MeterBackgroundKind.Peach:
                    ink.Oval(Vector2.Zero, new(.4f, .7f), .4f, false);
                    break;
                default:
                    ink.Line(new(-.5f, 0), new(.5f, 0), .35f);
                    break;
            }
        }
    }

    private static void DrawDaylight(ImDrawListPtr draw, Vector2 position, Vector2 size,
        Vector4 accent, float phase, float alpha, bool thumbnail)
    {
        var count = thumbnail ? 2 : 5;
        var startX = size.X < 7 * size.Y ? .76f : .3f;
        var span = size.X < 7 * size.Y ? .17f : .48f;
        for (var i = 0; i < count; i++)
        {
            var x = position.X + size.X * (startX + span * i / count);
            var shimmer = .5f + .5f * MathF.Sin((phase * (1 + i % 3) + i * .27f) * MathF.Tau);
            draw.AddLine(new Vector2(x, position.Y + size.Y * .78f),
                new Vector2(x + size.Y * .09f, position.Y + size.Y),
                ImGui.GetColorU32(accent with { W = alpha * (.08f + shimmer * .1f) }), 1);
        }
    }

    private static void DrawBands(ImDrawListPtr draw, BetterDeathsTheme theme, Vector2 position, Vector2 size,
        Vector4 accent, Vector4 secondary, float phase, float alpha, bool thumbnail)
    {
        var kind = MeterThemeStyle.Profile(theme).Background;
        var fabric = kind is MeterBackgroundKind.Satin or MeterBackgroundKind.Ribbons or MeterBackgroundKind.Fabric or MeterBackgroundKind.Candy;
        var segments = thumbnail ? 5 : 24;
        var lanes = thumbnail ? 1 : fabric ? 2 : 3;
        var narrow = size.X < 7 * size.Y;
        var startX = size.X * (narrow ? .72f : .28f);
        var bandSize = new Vector2(size.X * (narrow ? .24f : .54f), size.Y);
        for (var lane = 0; lane < lanes; lane++)
        {
            var color = lane % 2 == 0 ? accent : secondary;
            var line = ImGui.GetColorU32(color with { W = alpha * (fabric ? .32f : .26f) });
            var previous = position + new Vector2(startX, 0) + MeterHeaderMotionPolicy.Contour(theme, bandSize, lane, 0, phase, segments);
            for (var segment = 1; segment <= segments; segment++)
            {
                var next = position + new Vector2(startX, 0) + MeterHeaderMotionPolicy.Contour(theme, bandSize, lane, segment, phase, segments);
                draw.AddLine(previous, next, line);
                previous = next;
            }
        }
    }

    private static void DrawMineral(ImDrawListPtr draw, Vector2 position, Vector2 size, Vector4 accent,
        Vector4 secondary, float phase, float alpha, MeterBackgroundKind kind, bool thumbnail)
    {
        var narrow = size.X < 7 * size.Y;
        draw.PushClipRect(position + new Vector2(size.X * (narrow ? .7f : .25f), 0),
            position + new Vector2(size.X * (narrow ? .98f : .86f), size.Y), true);
        var count = thumbnail ? 2 : 6;
        for (var lane = 0; lane < count; lane++)
        {
            var x = size.X * (.3f + lane * .13f);
            var previous = position + new Vector2(x, 0);
            var color = ImGui.GetColorU32((lane % 2 == 0 ? accent : secondary) with { W = alpha * .24f });
            var steps = thumbnail ? 4 : 12;
            for (var step = 1; step <= steps; step++)
            {
                var y = size.Y * step / steps;
                var offset = kind == MeterBackgroundKind.Brushed ? -y * .65f :
                    MathF.Sin(lane * 2.7f + y / size.Y * MathF.Tau) * size.Y * .12f;
                var next = position + new Vector2(x + offset, y);
                draw.AddLine(previous, next, color);
                previous = next;
            }
        }
        var drift = MathF.Sin(phase * MathF.Tau) * size.X * .08f;
        var start = position + new Vector2(size.X * .58f + drift, 0);
        draw.AddQuadFilled(start, start + new Vector2(size.X * .07f, 0),
            start + new Vector2(size.X * .07f - size.Y * .65f, size.Y), start + new Vector2(-size.Y * .65f, size.Y),
            ImGui.GetColorU32(secondary with { W = alpha * .07f }));
        draw.PopClipRect();
    }
}
