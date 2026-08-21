using System;
using System.Collections.Generic;
using System.Numerics;

namespace BetterDeaths;

internal static class ReplayGeometry
{
    private const float Epsilon = 0.0001f;

    public static Vector2 WorldPointToScreen(
        float worldX,
        float worldZ,
        Vector2 canvasStart,
        Vector2 canvasSize,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        float zoom,
        Vector2 pan,
        float padding)
    {
        var innerWidth = MathF.Max(1.0f, canvasSize.X - (padding * 2.0f));
        var innerHeight = MathF.Max(1.0f, canvasSize.Y - (padding * 2.0f));
        var xRatio = (worldX - minX) / MathF.Max(1.0f, maxX - minX);
        var zRatio = (worldZ - minZ) / MathF.Max(1.0f, maxZ - minZ);
        var basePoint = new Vector2(
            canvasStart.X + padding + (innerWidth * xRatio),
            canvasStart.Y + padding + (innerHeight * zRatio));
        var center = canvasStart + (canvasSize * 0.5f);
        return center + ((basePoint - center) * zoom) + pan;
    }

    public static IReadOnlyList<Vector2> BuildConeArc(
        float originX,
        float originZ,
        float rotation,
        float length,
        float angleDegrees,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        ReplayArenaInfo? arena)
    {
        var clampedLength = MathF.Max(0.0f, length);
        var clampedAngle = Math.Clamp(angleDegrees, 10.0f, 360.0f);
        var segmentCount = Math.Clamp((int)MathF.Ceiling(clampedAngle / 5.0f), 4, 72);
        var halfAngle = clampedAngle * 0.5f * MathF.PI / 180.0f;
        var points = new List<Vector2>(segmentCount + 1);
        for (var index = 0; index <= segmentCount; index++)
        {
            var t = index / (float)segmentCount;
            var angle = rotation - halfAngle + (halfAngle * 2.0f * t);
            var direction = new Vector2(MathF.Sin(angle), MathF.Cos(angle));
            var visibleLength = ClipRayLength(
                originX,
                originZ,
                direction,
                clampedLength,
                minX,
                maxX,
                minZ,
                maxZ,
                arena);
            points.Add(new Vector2(
                originX + (direction.X * visibleLength),
                originZ + (direction.Y * visibleLength)));
        }

        return points;
    }

    private static float ClipRayLength(
        float originX,
        float originZ,
        Vector2 direction,
        float requestedLength,
        float minX,
        float maxX,
        float minZ,
        float maxZ,
        ReplayArenaInfo? arena)
    {
        if (requestedLength <= Epsilon)
        {
            return 0.0f;
        }

        if (arena is { Shape: ReplayArenaShape.Circle })
        {
            var offsetX = originX - arena.Value.CenterX;
            var offsetZ = originZ - arena.Value.CenterZ;
            var radiusSquared = arena.Value.Radius * arena.Value.Radius;
            if ((offsetX * offsetX) + (offsetZ * offsetZ) > radiusSquared + Epsilon)
            {
                return requestedLength;
            }

            var projection = (offsetX * direction.X) + (offsetZ * direction.Y);
            var discriminant = (projection * projection) -
                ((offsetX * offsetX) + (offsetZ * offsetZ) - radiusSquared);
            if (discriminant < 0.0f)
            {
                return 0.0f;
            }

            var boundaryDistance = -projection + MathF.Sqrt(discriminant);
            return Math.Clamp(boundaryDistance, 0.0f, requestedLength);
        }

        var boundsMinX = arena is null ? minX : arena.Value.CenterX - arena.Value.HalfWidth;
        var boundsMaxX = arena is null ? maxX : arena.Value.CenterX + arena.Value.HalfWidth;
        var boundsMinZ = arena is null ? minZ : arena.Value.CenterZ - arena.Value.HalfHeight;
        var boundsMaxZ = arena is null ? maxZ : arena.Value.CenterZ + arena.Value.HalfHeight;
        if (originX < boundsMinX - Epsilon || originX > boundsMaxX + Epsilon ||
            originZ < boundsMinZ - Epsilon || originZ > boundsMaxZ + Epsilon)
        {
            return requestedLength;
        }

        var boundaryLength = requestedLength;
        if (direction.X > Epsilon)
        {
            boundaryLength = MathF.Min(boundaryLength, (boundsMaxX - originX) / direction.X);
        }
        else if (direction.X < -Epsilon)
        {
            boundaryLength = MathF.Min(boundaryLength, (boundsMinX - originX) / direction.X);
        }

        if (direction.Y > Epsilon)
        {
            boundaryLength = MathF.Min(boundaryLength, (boundsMaxZ - originZ) / direction.Y);
        }
        else if (direction.Y < -Epsilon)
        {
            boundaryLength = MathF.Min(boundaryLength, (boundsMinZ - originZ) / direction.Y);
        }

        return Math.Clamp(boundaryLength, 0.0f, requestedLength);
    }
}
