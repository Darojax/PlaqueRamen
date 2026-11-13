using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

namespace PlaqueRamen;

internal sealed class PlaqueTextures : IDisposable
{
    internal sealed class Pair
    {
        internal readonly Texture2D Albedo, Gloss;
        internal Pair(Texture2D albedo, Texture2D gloss) { Albedo = albedo; Gloss = gloss; }
    }
    private const int Size = 2048, X = 1180, Y = 924, Width = 250, Height = 884;
    private readonly string _folder;
    private readonly Color32[] _base, _gloss;
    private readonly PlaqueSurface _surface;
    private readonly DarkPlaqueSurface _dark;
    private readonly Dictionary<string, Pair> _cache = new(StringComparer.OrdinalIgnoreCase);

    internal PlaqueTextures(string folder)
    {
        _folder = folder;
        var albedo = Load(Path.Combine(folder, "T_Tanzaku001_BASE.original.png"), false);
        Texture2D? gloss = null;
        try
        {
            gloss = Load(Path.Combine(folder, "T_Tanzaku001_GLOSS.original.png"), true);
            if (albedo.width != Size || albedo.height != Size || gloss.width != Size || gloss.height != Size)
                throw new InvalidDataException("Original plaque atlas dimensions changed.");
            _base = albedo.GetPixels32(); _gloss = gloss.GetPixels32();
            Pixel[] Face(Color32[] atlas)
            {
                var face = new Pixel[Width * Height];
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    { var p = atlas[(Y + y) * Size + X + x]; face[y * Width + x] = new Pixel(p.r, p.g, p.b, p.a); }
                return face;
            }
            _surface = new PlaqueSurface(Face(_base), Face(_gloss));
            Pixel[] DarkFace(Color32[] atlas)
            {
                var face = new Pixel[DarkPlaqueSurface.Width * DarkPlaqueSurface.Height];
                for (int y = 0; y < DarkPlaqueSurface.Height; y++) for (int x = 0; x < DarkPlaqueSurface.Width; x++)
                {
                    var p = atlas[(DarkPlaqueSurface.Y + y) * Size + DarkPlaqueSurface.X + x];
                    face[y * DarkPlaqueSurface.Width + x] = new Pixel(p.r, p.g, p.b, p.a);
                }
                return face;
            }
            _dark = new DarkPlaqueSurface(DarkFace(_base), DarkFace(_gloss));
        }
        finally { UnityEngine.Object.Destroy(albedo); if (gloss) UnityEngine.Object.Destroy(gloss); }
    }

    internal bool Has(Artwork art) => _cache.ContainsKey(art.Id);
    internal Pair Get(Artwork art) => _cache[art.Id];

    // Only missing artwork is imported, once per recipe. The caller spaces first
    // imports across updates; ordinary menu changes reuse the finished textures.
    internal void Prepare(Artwork art)
    {
        if (Has(art)) return;
        var image = Load(Path.Combine(_folder, "art", art.File), false);
        Texture2D? albedo = null, gloss = null;
        try
        {
            if (image.width != 725 || image.height < 2169 || image.height > 2170)
                throw new InvalidDataException("Approved artwork dimensions changed: " + art.File);
            var raw = image.GetPixels32(); var pixels = new Pixel[raw.Length];
            for (int i = 0; i < raw.Length; i++) { var p = raw[i]; pixels[i] = new Pixel(p.r, p.g, p.b, p.a); }
            var face = art.Drink ? _surface.ComposeDrink(pixels, image.width, image.height, art) : _surface.Compose(pixels, image.width, image.height, art);
            var next = (Color32[])_base.Clone(); var mask = (Color32[])_gloss.Clone();
            // Both styles occupy separate atlas regions. One cached pair per
            // recipe serves both holder types, retaining the original cache size.
            var dark = art.Drink ? DrinkDark(art) : _dark.Compose(pixels, image.width, image.height, art);
            for (int y = 0; y < DarkPlaqueSurface.Height; y++) for (int x = 0; x < DarkPlaqueSurface.Width; x++)
            {
                int index = (DarkPlaqueSurface.Y + y) * Size + DarkPlaqueSurface.X + x, source = y * DarkPlaqueSurface.Width + x;
                var c = dark.Albedo[source]; var g = dark.Gloss[source];
                next[index] = new Color32(c.R, c.G, c.B, c.A); mask[index] = new Color32(g.R, g.G, g.B, g.A);
            }
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int index = (Y + y) * Size + X + x, source = y * Width + x;
                    var c = face.Albedo[source]; var g = face.Gloss[source];
                    next[index] = new Color32(c.R, c.G, c.B, c.A); mask[index] = new Color32(g.R, g.G, g.B, g.A);
                }
            albedo = Finish(next, false, "MenuPlaque_" + art.Id);
            gloss = Finish(mask, true, "MenuPlaque_" + art.Id + "_Gloss");
            _cache.Add(art.Id, new Pair(albedo, gloss)); albedo = gloss = null;
        }
        finally { UnityEngine.Object.Destroy(image); if (albedo) UnityEngine.Object.Destroy(albedo); if (gloss) UnityEngine.Object.Destroy(gloss); }
    }

    private (Pixel[] Albedo, Pixel[] Gloss) DrinkDark(Artwork art)
    {
        Texture2D? face = null, mask = null;
        try
        {
            face = Load(Path.Combine(_folder, "art", art.DarkFile), false);
            mask = Load(Path.Combine(_folder, "art", art.DarkMaskFile), true);
            if (mask.width != face.width || mask.height != face.height) throw new InvalidDataException("Drink face/mask dimensions differ.");
            Pixel[] Pixels(Texture2D t)
            {
                var colors = t.GetPixels32(); var result = new Pixel[colors.Length];
                for (int i = 0; i < result.Length; i++) { var p = colors[i]; result[i] = new Pixel(p.r, p.g, p.b, p.a); }
                return result;
            }
            GrainImage Grain(string file)
            {
                var texture = Load(Path.Combine(_folder, "art", file), false);
                try { return new GrainImage(Pixels(texture), texture.width, texture.height); }
                finally { UnityEngine.Object.Destroy(texture); }
            }
            var a = Grain(art.DarkGrainA); var b = Grain(art.DarkGrainB);
            return _dark.ComposeDrink(Pixels(face), Pixels(mask), face.width, face.height, a.Pixels, a.Width, a.Height, art, b);
        }
        finally { if (face) UnityEngine.Object.Destroy(face); if (mask) UnityEngine.Object.Destroy(mask); }
    }

    private static Texture2D Finish(Color32[] pixels, bool linear, string name)
    {
        var texture = new Texture2D(Size, Size, TextureFormat.RGB24, true, linear);
        try
        {
            texture.name = name; texture.filterMode = FilterMode.Trilinear; texture.anisoLevel = 2;
            texture.SetPixels32(pixels); texture.Apply(true, false); texture.Compress(true); texture.Apply(false, true);
            return texture;
        }
        catch { UnityEngine.Object.Destroy(texture); throw; }
    }

    private static Texture2D Load(string path, bool linear)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
        try { if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false)) throw new InvalidDataException("Could not load " + path); return texture; }
        catch { UnityEngine.Object.Destroy(texture); throw; }
    }

    public void Dispose()
    {
        foreach (var pair in _cache.Values) { UnityEngine.Object.Destroy(pair.Albedo); UnityEngine.Object.Destroy(pair.Gloss); }
        _cache.Clear();
    }
}
