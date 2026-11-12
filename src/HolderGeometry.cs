using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PlaqueRamen;

internal sealed class PlaqueDefinition
{
    public int Id = 0;
    public float[] Min = Array.Empty<float>(), Max = Array.Empty<float>();
    public int[] TriangleOffsets = Array.Empty<int>();
}

internal sealed class HolderDefinition
{
    public string MeshName = "", IndexSha256 = "";
    public int VertexCount = 0, SubmeshCount = 0, MaterialSlot = 0;
    public PlaqueDefinition[] Plaques = Array.Empty<PlaqueDefinition>();
    internal bool Dark => MeshName == "Mesh_Tanzaku_Shelf001V001";
    internal int Capacity => Dark ? 7 : 9;
    internal int BoardTriangles => Dark ? 12 : 20;
    internal int FastenerTriangles => Dark ? 300 : 86;
    internal int BoardVertices => Dark ? 24 : 32;
    internal int FastenerVertices => Dark ? 203 : 62;
    internal int FrameTriangles => Dark ? 10 : 16;
}

internal static class HolderGeometry
{
    internal static string Fingerprint(int[] indices)
    {
        var bytes = new byte[indices.Length * sizeof(int)];
        for (int i = 0; i < indices.Length; i++)
        {
            uint v = (uint)indices[i];
            bytes[i * 4] = (byte)v; bytes[i * 4 + 1] = (byte)(v >> 8);
            bytes[i * 4 + 2] = (byte)(v >> 16); bytes[i * 4 + 3] = (byte)(v >> 24);
        }
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
    }

    internal static void Validate(HolderDefinition holder, int vertexCount, int submeshCount, int[] indices)
    {
        if (vertexCount != holder.VertexCount || submeshCount != holder.SubmeshCount
            || indices.Length % 3 != 0 || Fingerprint(indices) != holder.IndexSha256)
            throw new InvalidDataException("Holder geometry differs from its reviewed native mesh.");
        if (holder.MeshName is not ("Mesh_Tanzaku_Shelf001V001" or "Mesh_Tanzaku_Shelf001V002")
            || holder.Plaques.Length != holder.Capacity || holder.Plaques.Select(p => p.Id).Distinct().Count() != holder.Capacity)
            throw new InvalidDataException("The holder must match a reviewed seven- or nine-plaque layout.");
        var seen = new HashSet<int>();
        foreach (var plaque in holder.Plaques)
        {
            if (plaque.Min.Length != 3 || plaque.Max.Length != 3 || plaque.TriangleOffsets.Length != holder.BoardTriangles + holder.FastenerTriangles)
                throw new InvalidDataException("A reviewed plaque must contain its board and clip.");
            foreach (int offset in plaque.TriangleOffsets)
                if (offset < 0 || offset >= indices.Length / 3 || !seen.Add(offset))
                    throw new InvalidDataException("Plaque triangle ranges overlap or leave the source mesh.");
        }
    }

}
