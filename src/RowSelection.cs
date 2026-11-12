using System;
using System.Collections.Generic;
using System.Linq;

namespace PlaqueRamen;

internal readonly struct PlaquePoint
{
    internal readonly int Id;
    internal readonly float X, Y, Z;
    internal PlaquePoint(int id, float x, float y, float z) { Id = id; X = x; Y = y; Z = z; }
}

internal static class RowSelection
{
    // A complete photographed row is required. Ambiguous or incomplete groups
    // are reported without modifying any renderer.
    internal static int[] Select(IReadOnlyList<PlaquePoint> points, int aimedId, float rightX, float rightZ, int expected = 9)
    {
        var anchor = points.Single(p => p.Id == aimedId);
        int[] best = Array.Empty<int>();
        float bestAlignment = -1;
        foreach (var neighbor in points)
        {
            float ax = neighbor.X - anchor.X, az = neighbor.Z - anchor.Z;
            float length = (float)Math.Sqrt(ax * ax + az * az);
            if (neighbor.Id == aimedId || length < 0.04f || length > 5f || Math.Abs(neighbor.Y - anchor.Y) > 0.16f) continue;
            ax /= length; az /= length;
            if (ax * rightX + az * rightZ < 0) { ax = -ax; az = -az; }
            var line = points.Where(p => Math.Abs(p.Y - anchor.Y) <= 0.16f
                && Math.Abs((p.X - anchor.X) * az - (p.Z - anchor.Z) * ax) <= 0.08f
                && Math.Abs((p.X - anchor.X) * ax + (p.Z - anchor.Z) * az) <= 5f)
                .OrderBy(p => (p.X - anchor.X) * ax + (p.Z - anchor.Z) * az).ToArray();
            int index = Array.FindIndex(line, p => p.Id == aimedId);
            int left = index, right = index;
            while (left > 0 && Distance(line[left], line[left - 1]) <= 0.6f) left--;
            while (right + 1 < line.Length && Distance(line[right], line[right + 1]) <= 0.6f) right++;
            if (right - left + 1 != expected) continue;
            float alignment = Math.Abs(ax * rightX + az * rightZ);
            if (alignment > bestAlignment)
            {
                bestAlignment = alignment;
                best = line.Skip(left).Take(expected).Select(p => p.Id).ToArray();
            }
        }
        return best;
    }

    private static float Distance(PlaquePoint a, PlaquePoint b)
        => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
}
