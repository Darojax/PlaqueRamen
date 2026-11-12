using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlaqueRamen;

internal sealed class DiscoverySchedule
{
    private readonly Queue<double> _times = new();
    internal void Request(double now)
    {
        _times.Clear();
        foreach (double delay in new[] { .25, 1, 4, 12 }) _times.Enqueue(now + delay);
    }
    internal bool Due(double now)
    {
        if (_times.Count == 0 || now < _times.Peek()) return false;
        _times.Dequeue(); return true;
    }
}

internal static class AutoLayout
{
    internal static int[] RightToLeft(HolderDefinition holder)
    {
        // The reviewed mesh faces local -Z. Its front-view right is local +X.
        // Work in mesh space: moving/rotating the furniture or camera cannot
        // change which physical hook receives the first dish.
        var points = holder.Plaques.Select(p => new PlaquePoint(p.Id,
            (p.Min[0] + p.Max[0]) / 2, (p.Min[1] + p.Max[1]) / 2, (p.Min[2] + p.Max[2]) / 2)).ToArray();
        if (points.Length != holder.Capacity || points.Select(p => p.Id).Distinct().Count() != holder.Capacity)
            throw new InvalidDataException("The automatic holder needs its complete native fastener layout.");
        var row = RowSelection.Select(points, points[0].Id, 1, 0, holder.Capacity);
        if (row.Length != holder.Capacity) throw new InvalidDataException("The fixed front-view fastener order could not be confirmed.");
        return Enumerable.Reverse(row).ToArray();
    }
}
