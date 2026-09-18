using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace BetterDeaths.Windows;

internal static partial class MeterHeaderBackgroundRenderer
{
    // A stack-only local drawing frame keeps motifs reusable without meshes, textures, or particle allocations.
    private readonly struct MotifInk(ImDrawListPtr draw, Vector2 center, float radius, float rotation,
        Vector4 accent, Vector4 secondary, float alpha, bool thumbnail)
    {
        private readonly float sin = MathF.Sin(rotation);
        private readonly float cos = MathF.Cos(rotation);
        public bool Simple => thumbnail;
        private Vector2 Point(Vector2 p) => center + new Vector2(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos) * radius;
        private uint Color(float opacity, bool alternate) => ImGui.GetColorU32(
            (alternate ? secondary : accent) with { W = alpha * opacity });

        public MotifInk At(Vector2 offset, float size = 1, float angle = 0) =>
            new(draw, Point(offset), radius * size, rotation + angle, accent, secondary, alpha, thumbnail);

        public void Line(Vector2 a, Vector2 b, float opacity = .7f, float width = .045f, bool alternate = false) =>
            draw.AddLine(Point(a), Point(b), Color(opacity, alternate), MathF.Max(.8f, radius * width));

        public void Triangle(Vector2 a, Vector2 b, Vector2 c, float opacity = .45f, bool alternate = false) =>
            draw.AddTriangleFilled(Point(a), Point(b), Point(c), Color(opacity, alternate));

        public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float opacity = .45f, bool alternate = false) =>
            draw.AddQuadFilled(Point(a), Point(b), Point(c), Point(d), Color(opacity, alternate));

        public void Oval(Vector2 offset, Vector2 radii, float opacity = .45f, bool fill = true, bool alternate = false)
        {
            draw.PathClear();
            var count = thumbnail ? 6 : 16;
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
            var count = thumbnail ? 3 : 8;
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
            draw.PathFillConvex(Color(lower ? .7f : .85f, false));
        }
    }

    private static void Can(MotifInk ink)
    {
        ink.Quad(new(-.53f, -.78f), new(.53f, -.78f), new(.53f, .78f), new(-.53f, .78f), .32f);
        ink.Oval(new(0, .78f), new(.53f, .16f), .38f);
        ink.Oval(new(0, -.78f), new(.53f, .16f), .62f);
        ink.Quad(new(-.53f, -.03f), new(.53f, -.35f), new(.53f, .02f), new(-.53f, .34f), .65f, true);
        if (ink.Simple) return;
        ink.Line(new(-.53f, -.78f), new(-.53f, .78f), .75f);
        ink.Line(new(.53f, -.78f), new(.53f, .78f), .75f);
        ink.Oval(new(0, -.78f), new(.53f, .16f), .8f, false);
        ink.Oval(new(.08f, -.79f), new(.12f, .08f), .9f, false, true);
        ink.Line(new(-.32f, -.57f), new(-.32f, -.13f), .75f, .07f);
        ink.Line(new(-.32f, .39f), new(-.32f, .64f), .55f, .07f);
        ink.Oval(new(0, .78f), new(.53f, .16f), .65f, false);
    }

    private static void Ice(MotifInk ink)
    {
        ink.Quad(new(-.65f, -.25f), new(.35f, -.6f), new(.55f, .3f), new(-.45f, .65f), .22f);
        ink.Quad(new(-.65f, -.25f), new(-.38f, -.58f), new(.58f, -.9f), new(.35f, -.6f), .32f);
        ink.Quad(new(.35f, -.6f), new(.58f, -.9f), new(.78f, .01f), new(.55f, .3f), .28f, true);
        ink.Line(new(-.52f, -.12f), new(-.38f, .38f), .7f);
        ink.Line(new(-.52f, -.12f), new(.14f, -.34f), .7f);
    }

    private static void Citrus(MotifInk ink)
    {
        ink.Oval(Vector2.Zero, new(.9f), .22f);
        ink.Oval(Vector2.Zero, new(.9f), .75f, false);
        if (ink.Simple) return;
        ink.Oval(Vector2.Zero, new(.74f), .62f, false, true);
        for (var i = 0; i < 8; i++)
        {
            var angle = i * MathF.Tau / 8;
            var a = new Vector2(MathF.Cos(angle + .07f), MathF.Sin(angle + .07f));
            var b = new Vector2(MathF.Cos(angle + .7f), MathF.Sin(angle + .7f));
            ink.Triangle((a + b) * .1f, a * .68f, b * .68f, .34f, i % 2 == 0);
        }
        Leaf(ink.At(new(.73f, -.8f), .5f, -.6f), Vector2.Zero, 1, .45f);
    }

    private static void Petal(MotifInk ink)
    {
        // Two convex halves leave the characteristic notch without a concave fill.
        ink.PetalHalf(false);
        ink.PetalHalf(true);
        if (!ink.Simple) ink.Line(new(-.8f, 0), new(.36f, 0), .38f, .07f, true);
    }

    private static void Blossom(MotifInk ink, bool rose)
    {
        var petals = rose ? 7 : 5;
        for (var i = 0; i < petals; i++)
        {
            var angle = i * MathF.Tau / petals;
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * .5f;
            var petal = ink.At(offset, .42f, angle);
            if (rose) petal.Oval(Vector2.Zero, new(1, .75f), .45f);
            else Petal(petal);
        }
        ink.Oval(Vector2.Zero, new(.14f), .8f, true, true);
        if (rose && !ink.Simple)
        {
            ink.Oval(new(.05f, -.03f), new(.35f, .27f), .7f, false, true);
            ink.Line(new(-.2f, 0), new(.15f, .12f), .7f, alternate: true);
        }
        ink.Line(new(.55f, .55f), new(1.4f, 1), .45f, alternate: true);
        if (!ink.Simple) Leaf(ink.At(new(1.08f, .77f), .35f, -.75f), Vector2.Zero, 1, .5f);
    }

    private static void Leaf(MotifInk ink, Vector2 offset, float length, float width)
    {
        ink.Quad(offset + new Vector2(-length, 0), offset + new Vector2(-.1f, -width),
            offset + new Vector2(length, 0), offset + new Vector2(.1f, width), .48f);
        if (!ink.Simple) ink.Line(offset + new Vector2(-length * .8f, 0), offset + new Vector2(length * .8f, 0), .75f);
    }

    private static void Sprig(MotifInk ink, bool mint)
    {
        ink.Line(new(0, 1.1f), new(0, -1), .65f, alternate: true);
        var pairs = ink.Simple ? 1 : 3;
        for (var i = 0; i < pairs; i++)
        {
            var y = .5f - i * .58f;
            Leaf(ink.At(new(-.37f, y - .2f), .55f, .6f), Vector2.Zero, 1, mint ? .7f : .4f);
            Leaf(ink.At(new(.37f, y - .35f), .55f, -.6f), Vector2.Zero, 1, mint ? .7f : .4f);
        }
    }

    private static void Candy(MotifInk ink)
    {
        ink.Triangle(new(-.48f, 0), new(-1.12f, -.43f), new(-1.12f, .43f), .63f, true);
        ink.Triangle(new(.48f, 0), new(1.12f, .43f), new(1.12f, -.43f), .63f, true);
        ink.Oval(Vector2.Zero, new(.65f, .48f), .5f);
        if (ink.Simple) return;
        ink.Oval(Vector2.Zero, new(.65f, .48f), .7f, false);
        ink.Line(new(-.2f, -.43f), new(.12f, .43f), .8f, .17f, true);
        ink.Line(new(.14f, -.4f), new(.4f, .32f), .7f, .12f, true);
        ink.Line(new(-.72f, -.18f), new(-.95f, -.27f), .7f);
        ink.Line(new(.72f, .18f), new(.95f, .27f), .7f);
    }

    private static void Lollipop(MotifInk ink)
    {
        ink.Line(new(0, .2f), new(0, 1.2f), .7f, .09f, true);
        ink.Oval(new(0, -.4f), new(.65f), .45f);
        ink.Oval(new(0, -.4f), new(.65f), .7f, false);
        if (ink.Simple) return;
        var previous = new Vector2(0, -.4f);
        for (var i = 1; i <= 24; i++)
        {
            var angle = i * .45f;
            var next = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (i * .023f) + new Vector2(0, -.4f);
            ink.Line(previous, next, .85f, .07f, true);
            previous = next;
        }
    }

    private static void Flask(MotifInk ink)
    {
        ink.Oval(new(0, .3f), new(.75f, .7f), .18f);
        ink.Oval(new(0, .53f), new(.62f, .38f), .55f, alternate: true);
        ink.Quad(new(-.22f, -.9f), new(.22f, -.9f), new(.26f, -.25f), new(-.26f, -.25f), .4f);
        ink.Line(new(-.32f, -.92f), new(.32f, -.92f), .8f, .15f);
        if (ink.Simple) return;
        ink.Oval(new(0, .3f), new(.75f, .7f), .65f, false);
        ink.Oval(new(0, .3f), new(.62f, .11f), .65f, false, true);
        ink.Line(new(-.47f, .15f), new(-.5f, .5f), .8f, .07f);
        ink.Oval(new(.17f, .53f), new(.09f), .8f, false);
    }

    private static void Bubble(MotifInk ink)
    {
        ink.Oval(Vector2.Zero, new(.86f), .23f);
        ink.Oval(Vector2.Zero, new(.86f), .75f, false);
        if (ink.Simple) return;
        ink.Oval(new(-.33f, -.4f), new(.19f, .12f), .85f);
        ink.Oval(new(.69f, .58f), new(.3f), .55f, false, true);
    }

    private static void Wisteria(MotifInk ink)
    {
        ink.Line(new(-.7f, -.9f), new(.7f, -.9f), .7f, alternate: true);
        var strands = ink.Simple ? 1 : 3;
        for (var j = 0; j < strands; j++)
        {
            var x = (j - 1) * .6f;
            ink.Line(new(x, -.9f), new(x, .8f), .38f, alternate: true);
            var rows = ink.Simple ? 2 : 4;
            for (var i = 0; i < rows; i++)
            {
                var r = .28f - i * .04f;
                ink.Oval(new(x - r * .65f, -.65f + i * .4f), new(r, r * 1.2f), .52f);
                ink.Oval(new(x + r * .65f, -.52f + i * .4f), new(r, r * 1.2f), .45f);
            }
        }
    }

    private static void Heart(MotifInk ink)
    {
        ink.Oval(new(-.32f, -.24f), new(.4f), .43f);
        ink.Oval(new(.32f, -.24f), new(.4f), .43f);
        ink.Triangle(new(-.68f, -.08f), new(.68f, -.08f), new(0, .8f), .43f);
        if (!ink.Simple) ink.Line(new(-.47f, -.37f), new(-.27f, -.47f), .7f, .08f, true);
    }

    private static void Peach(MotifInk ink)
    {
        ink.Oval(new(-.22f, .12f), new(.58f, .72f), .38f);
        ink.Oval(new(.22f, .12f), new(.58f, .72f), .38f);
        ink.Line(new(.05f, -.47f), new(.2f, .2f), .65f, alternate: true);
        Leaf(ink.At(new(.36f, -.74f), .48f, -.4f), Vector2.Zero, 1, .45f);
        ink.Line(new(0, -.48f), new(-.08f, -.92f), .7f, alternate: true);
    }

    private static void Grapes(MotifInk ink)
    {
        var rows = ink.Simple ? 2 : 3;
        for (var row = 0; row < rows; row++)
        for (var col = 0; col < 3 - row; col++)
        {
            var offset = new Vector2((col - (2 - row) * .5f) * .57f, -.3f + row * .52f);
            ink.Oval(offset, new(.33f), .42f);
            if (!ink.Simple) ink.Oval(offset, new(.33f), .65f, false);
        }
        Leaf(ink.At(new(.35f, -.85f), .45f, -.3f), Vector2.Zero, 1, .55f);
        ink.Line(new(0, -.5f), new(-.15f, -1), .7f, alternate: true);
    }

    private static void Cotton(MotifInk ink)
    {
        ink.Line(new(0, .3f), new(.15f, 1.1f), .6f, alternate: true);
        ink.Triangle(new(-.75f, .3f), new(.7f, .3f), new(0, .8f), .3f, true);
        ink.Oval(new(-.4f, .03f), new(.48f), .35f);
        ink.Oval(new(.4f, .03f), new(.48f), .35f);
        ink.Oval(new(0, -.34f), new(.52f), .45f);
    }

    private static void Banana(MotifInk ink)
    {
        var previousOuter = new Vector2(-.83f, -.4f);
        var previousInner = previousOuter;
        var segments = ink.Simple ? 4 : 12;
        for (var i = 1; i <= segments; i++)
        {
            var t = i / (float)segments;
            var outer = new Vector2(-.83f + t * 1.66f, -.4f + MathF.Sin(t * MathF.PI) * 1.1f);
            var inner = new Vector2(outer.X, -.4f + MathF.Sin(t * MathF.PI) * .5f);
            ink.Quad(previousOuter, outer, inner, previousInner, .6f);
            if (!ink.Simple) ink.Line(previousOuter, outer, .7f, alternate: true);
            previousOuter = outer;
            previousInner = inner;
        }
        ink.Line(new(.8f, -.4f), new(.9f, -.7f), .8f, .13f, true);
    }

    private static void Seed(MotifInk ink)
    {
        ink.Quad(new(0, -1), new(.5f, -.15f), new(.25f, .8f), new(-.25f, .8f), .4f);
        ink.Triangle(new(0, -1), new(-.25f, .8f), new(-.5f, -.15f), .4f);
        ink.Line(new(0, -.65f), new(0, .65f), .75f, .09f, true);
        if (!ink.Simple)
        {
            ink.Line(new(-.16f, -.3f), new(-.18f, .5f), .65f, alternate: true);
            ink.Line(new(.16f, -.3f), new(.18f, .5f), .65f, alternate: true);
        }
    }

    private static void Pillow(MotifInk ink)
    {
        ink.Oval(Vector2.Zero, new(1, .7f), .3f);
        ink.Quad(new(-.95f, -.63f), new(.95f, -.63f), new(.95f, .63f), new(-.95f, .63f), .2f);
        if (ink.Simple) return;
        for (var i = 0; i < 7; i++)
        {
            var x = -.7f + i * .22f;
            ink.Line(new(x, -.48f), new(x + .1f, -.48f), .65f);
            ink.Line(new(x, .48f), new(x + .1f, .48f), .65f);
        }
        ink.Line(new(-.74f, -.35f), new(-.74f, .35f), .5f);
        ink.Line(new(.74f, -.35f), new(.74f, .35f), .5f);
    }

    private static void Cloud(MotifInk ink)
    {
        ink.Oval(new(-.62f, .24f), new(.52f, .4f), .32f);
        ink.Oval(new(-.08f, -.03f), new(.62f), .32f);
        ink.Oval(new(.57f, .18f), new(.5f, .44f), .32f);
        ink.Quad(new(-.63f, .22f), new(.63f, .22f), new(.63f, .61f), new(-.63f, .61f), .2f);
        if (!ink.Simple) ink.Line(new(-.66f, .71f), new(.71f, .71f), .45f, alternate: true);
    }

    private static void Flame(MotifInk ink)
    {
        ink.Triangle(new(-.7f, .9f), new(.8f, .9f), new(.5f, -.65f), .38f);
        ink.Quad(new(-.7f, .9f), new(-.85f, .25f), new(-.2f, -1.1f), new(.24f, .36f), .55f);
        ink.Triangle(new(-.34f, .9f), new(.48f, .9f), new(.08f, -.3f), .6f, true);
    }

    private static void Chili(MotifInk ink)
    {
        ink.Oval(new(-.25f, -.35f), new(.36f, .48f), .5f);
        ink.Quad(new(-.57f, -.14f), new(.1f, -.14f), new(.27f, .57f), new(-.08f, .43f), .5f);
        ink.Triangle(new(-.08f, .43f), new(.27f, .57f), new(.86f, .66f), .5f);
        ink.Line(new(-.28f, -.76f), new(-.13f, -1.06f), .8f, .13f, true);
        ink.Line(new(-.13f, -1.06f), new(.13f, -1.03f), .8f, .13f, true);
        if (!ink.Simple) ink.Line(new(-.4f, -.39f), new(-.36f, -.1f), .8f, .08f);
    }

    private static void Crescent(MotifInk ink)
    {
        // Strip between two arcs, rather than erasing a disc with an assumed background color.
        var outer = new Vector2(.35f, -.93f);
        var inner = outer;
        var count = ink.Simple ? 5 : 16;
        for (var i = 1; i <= count; i++)
        {
            var t = i / (float)count;
            var a = new Vector2(.35f - MathF.Sin(t * MathF.PI) * 1.25f, -.93f + t * 1.86f);
            var b = new Vector2(.35f - MathF.Sin(t * MathF.PI) * .62f, a.Y);
            ink.Quad(outer, a, b, inner, .6f);
            outer = a;
            inner = b;
        }
    }

    private static void Sun(MotifInk ink, float phase)
    {
        ink.Oval(Vector2.Zero, new(.48f), .55f);
        var rays = ink.Simple ? 6 : 12;
        var spun = ink.At(Vector2.Zero, 1, phase * MathF.Tau);
        for (var i = 0; i < rays; i++)
        {
            var a = i * MathF.Tau / rays;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            spun.Line(dir * .67f, dir * 1.15f, .55f, .06f, true);
        }
    }
}
