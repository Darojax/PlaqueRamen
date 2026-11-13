using System;
using System.IO;
using System.Linq;

namespace PlaqueRamen;

internal static class CertificateSupport
{
    // The native wall certificate rests in a shallow wooden display ledge.
    // Widen only X so its overlap, depth, wall contact and UVs stay native.
    internal static float[][] Create(float[][] vertices,CertificateArt art)
    {
        if(vertices.Length!=36 || vertices.Any(v=>v.Length<3 || v.Take(3).Any(n=>float.IsNaN(n)||float.IsInfinity(n))))
            throw new InvalidDataException("Native display ledge vertices changed.");
        float low=vertices.Min(v=>v[0]),high=vertices.Max(v=>v[0]),width=high-low,centre=(low+high)/2;
        if(Math.Abs(width-.42624436f)>.002f)throw new InvalidDataException("Native display ledge width changed.");
        var copy=vertices.Select(v=>v.Take(3).ToArray()).ToArray();
        if(art.Mount=="Native")return copy;
        if((!art.DisplayLedge && art.Mount!="Table") || !(art.Width>=.15f && art.Width<=1))
            throw new InvalidDataException("The decoration has no display ledge.");
        float target=art.Width+(art.DisplayLedge?width-.3457062f:.04f);
        foreach(var v in copy)v[0]=centre+(v[0]-centre)*target/width;
        return copy;
    }
}
