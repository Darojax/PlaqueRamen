using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


namespace PlaqueRamen;

internal readonly struct HookPoint
{
    internal readonly float X, Y, Z;
    internal HookPoint(float x, float y, float z) { X = x; Y = y; Z = z; }
}

internal static class HookMount
{
    // Extend only the terminal ring/cap of the rear mounting shaft. Its front
    // bend and plaque contact point stay native. Work from original coordinates
    // once per private mesh, independently of the number of menu dishes.
    internal static float[] Connect(HolderDefinition holder, HolderParts parts, int[] indices, HookPoint[] points)
    {
        parts.Validate(holder, indices);
        if (points.Length != holder.VertexCount || points.Any(p => !Finite(p.X) || !Finite(p.Y) || !Finite(p.Z)))
            throw new InvalidDataException("Invalid holder positions for mounting hooks.");
        var original = points.Select(p => p.Z).ToArray();
        var occupied = holder.Plaques.SelectMany(p => p.TriangleOffsets).ToHashSet();
        var rail = Enumerable.Range(0, indices.Length / 3).Where(t => !occupied.Contains(t)).ToArray();
        if (rail.Length != 16) throw new InvalidDataException("Could not confirm the original frame surface.");
        // The native frame is open-backed against the wall. Its existing rear
        // edge supplies the depth limit even where there is no back-face hit.
        float rearEdge = rail.SelectMany(t => indices.Skip(t * 3).Take(3)).Max(i => points[i].Z);
        foreach (var part in parts.Plaques)
        {
            var hook = part.HookTriangleOffsets.SelectMany(t => indices.Skip(t * 3).Take(3)).Distinct().ToArray();
            float rear = hook.Max(i => points[i].Z), front = hook.Min(i => points[i].Z), depth = rear - front;
            var cap = hook.Where(i => points[i].Z >= rear - depth * .05f).ToArray();
            if (depth <= 0 || cap.Length < 3 || cap.Length > hook.Length / 3)
                throw new InvalidDataException("Could not isolate the hook's rear mounting cap.");
            float frameFront = float.NegativeInfinity, frameBack = rearEdge;
            foreach (int i in cap)
            {
                var hits = new List<float>();
                foreach (int triangle in rail)
                {
                    int o = triangle * 3;
                    float? z = SurfaceZ(points[i].X, points[i].Y, points[indices[o]], points[indices[o + 1]], points[indices[o + 2]]);
                    if (z.HasValue) hits.Add(z.Value);
                }
                if (hits.Count == 0) throw new InvalidDataException("Hook rear does not line up with the frame.");
                frameFront = Math.Max(frameFront, hits.Min());
            }
            if (frameBack <= frameFront) throw new InvalidDataException("Could not confirm solid wood behind the hook.");
            float capFront = cap.Min(i => points[i].Z), capBack = cap.Max(i => points[i].Z);
            if (capFront >= frameFront && capBack < frameBack) continue;
            float inset = Math.Min(depth * .025f, (frameBack - frameFront) * .1f);
            float shift = frameFront + inset - capFront;
            if (shift <= 0 || shift > depth * 2 || capBack + shift >= frameBack)
                throw new InvalidDataException("Hook mount extension is outside the reviewed range.");
            foreach (int i in cap) original[i] = points[i].Z + shift;
        }
        return original;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static float? SurfaceZ(float x, float y, HookPoint a, HookPoint b, HookPoint c)
    {
        double denominator = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (Math.Abs(denominator) < 1e-12) return null;
        double u = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / denominator;
        double v = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / denominator;
        double w = 1 - u - v;
        return Math.Min(u, Math.Min(v, w)) < -1e-6 ? null : (float)(u * a.Z + v * b.Z + w * c.Z);
    }
}
