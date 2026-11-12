using System;
using System.IO;
using System.Linq;


namespace PlaqueRamen;

internal sealed class PlaqueParts
{
    public int Id = 0;
    public int[] BoardTriangleOffsets = Array.Empty<int>(), HookTriangleOffsets = Array.Empty<int>();
}

internal sealed class HolderParts
{
    public string MeshName = "", IndexSha256 = "";
    public PlaqueParts[] Plaques = Array.Empty<PlaqueParts>();

    internal void Validate(HolderDefinition holder, int[] source)
    {
        if (MeshName != holder.MeshName || IndexSha256 != holder.IndexSha256 || HolderGeometry.Fingerprint(source) != IndexSha256
            || Plaques.Length != holder.Capacity || !Plaques.Select(p => p.Id).OrderBy(id => id).SequenceEqual(holder.Plaques.Select(p => p.Id).OrderBy(id => id)))
            throw new InvalidDataException("Hook/board map does not match the reviewed holder.");
        foreach (var part in Plaques)
        {
            var original = holder.Plaques.Single(p => p.Id == part.Id);
            var all = part.BoardTriangleOffsets.Concat(part.HookTriangleOffsets).ToArray();
            if (part.BoardTriangleOffsets.Length != holder.BoardTriangles || part.HookTriangleOffsets.Length != holder.FastenerTriangles
                || all.Distinct().Count() != holder.BoardTriangles + holder.FastenerTriangles
                || !all.OrderBy(t => t).SequenceEqual(original.TriangleOffsets.OrderBy(t => t)))
                throw new InvalidDataException("Wood and hook triangles do not form the original plaque.");
            int[] Vertices(int[] offsets) => offsets.SelectMany(t => source.Skip(t * 3).Take(3)).Distinct().ToArray();
            var board = Vertices(part.BoardTriangleOffsets); var hook = Vertices(part.HookTriangleOffsets);
            if (board.Length != holder.BoardVertices || hook.Length != holder.FastenerVertices || board.Intersect(hook).Any())
                throw new InvalidDataException("Wood and hooks cannot be isolated safely.");
        }
    }
}
