using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace BetterDeaths.Windows;

internal static partial class MeterHeaderBackgroundRenderer
{
    private readonly struct TextureInk(ImDrawListPtr draw, Vector2 center, float radius, float rotation,
        Vector4 accent, Vector4 secondary, float alpha, bool thumbnail)
    {
        private readonly float sin = MathF.Sin(rotation);
        private readonly float cos = MathF.Cos(rotation);
        public bool Simple => thumbnail;

        private Vector2 Point(Vector2 p) => center + new Vector2(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos) * radius;
        private uint Color(float opacity, bool alternate = false) => ImGui.GetColorU32(
            (alternate ? secondary : accent) with { W = alpha * opacity });

        public void Line(Vector2 a, Vector2 b, float opacity = .7f, float width = .045f, bool alternate = false) =>
            draw.AddLine(Point(a), Point(b), Color(opacity, alternate), MathF.Max(.8f, radius * width));

        public void Oval(Vector2 offset, Vector2 radii, float opacity = .45f, bool fill = true, bool alternate = false)
        {
            draw.PathClear();
            var count = thumbnail ? 6 : 12;
            for (var i = 0; i < count; i++)
            {
                var angle = i * MathF.Tau / count;
                draw.PathLineTo(Point(offset + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radii));
            }
            if (fill) draw.PathFillConvex(Color(opacity, alternate));
            else draw.PathStroke(Color(opacity, alternate), ImDrawFlags.Closed, MathF.Max(.8f, radius * .045f));
        }

        public void PetalHalf(bool lower)
        {
            draw.PathClear();
            draw.PathLineTo(Point(new(-1.1f, 0)));
            if (lower) draw.PathLineTo(Point(new(.62f, 0)));
            var count = thumbnail ? 3 : 6;
            for (var i = 0; i <= count; i++)
            {
                var t = (lower ? count - i : i) / (float)count;
                var u = 1 - t;
                var point = new Vector2(-1.1f, 0) * (u * u * u) + new Vector2(-.1f, -.95f) * (3 * u * u * t) +
                    new Vector2(.8f, -.8f) * (3 * u * t * t) + new Vector2(.95f, -.32f) * (t * t * t);
                if (lower) point.Y = -point.Y;
                draw.PathLineTo(Point(point));
            }
            if (!lower) draw.PathLineTo(Point(new(.62f, 0)));
            draw.PathFillConvex(Color(lower ? .58f : .72f));
        }
    }

    private static void Petal(TextureInk ink)
    {
        ink.PetalHalf(false);
        ink.PetalHalf(true);
        if (!ink.Simple) ink.Line(new(-.8f, 0), new(.36f, 0), .3f, .06f, true);
    }

    private static void Leaf(TextureInk ink, Vector2 offset, float length, float width)
    {
        ink.Line(offset + new Vector2(-length, 0), offset + new Vector2(length, 0), .55f);
        ink.Line(offset + new Vector2(-length * .65f, 0), offset + new Vector2(0, -width), .42f);
        ink.Line(offset + new Vector2(0, -width), offset + new Vector2(length * .65f, 0), .42f);
    }
}
