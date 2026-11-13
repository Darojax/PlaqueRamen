using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlaqueRamen;

// Dark counterparts use the exact approved lettering/grain source images.
// Native dark stain and off-white paint, with the original staggered layout.
internal sealed class DarkPlaqueSurface
{
    internal const int X = 1478, Y = 948, Width = 273, Height = 837;
    internal const float Left = 1486.3132f, Right = 1742.3132f, Bottom = 956.9053f, Top = 1776.1052f;
    internal readonly WoodStatistics NativeWood;
    internal readonly Pixel Paint;
    private readonly Pixel[] _original, _gloss;
    private readonly byte _woodGloss, _paintGloss;
    private readonly byte[] _paintGrain;

    internal DarkPlaqueSurface(Pixel[] original, Pixel[] gloss)
    {
        if (original.Length != Width * Height || gloss.Length != original.Length) throw new InvalidDataException("Invalid dark plaque face.");
        _original = (Pixel[])original.Clone(); _gloss = (Pixel[])gloss.Clone();
        var wood = new List<Pixel>(); var paint = new List<Pixel>();
        var woodGloss = new List<byte>(); var paintGloss = new List<byte>();
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
        {
            if (!Inside(x, y)) continue;
            int i = y * Width + x; var p = original[i];
            if (IsPaint(p) && Math.Min(p.R, Math.Min(p.G, p.B)) > 135) { paint.Add(p); paintGloss.Add(gloss[i].R); }
            else if (Math.Max(p.R, Math.Max(p.G, p.B)) < 105) { wood.Add(p); woodGloss.Add(gloss[i].R); }
        }
        if (wood.Count < 1000 || paint.Count < 100) throw new InvalidDataException("Original dark wood/white paint could not be measured.");
        NativeWood = WoodStatistics.Measure(wood);
        Paint = new Pixel(Median(paint.Select(p => p.R)), Median(paint.Select(p => p.G)), Median(paint.Select(p => p.B)));
        _woodGloss = Median(woodGloss); _paintGloss = Median(paintGloss);
        _paintGrain = paint.Select(p => p.G).ToArray();
    }

    internal (Pixel[] Albedo, Pixel[] Gloss) Compose(Pixel[] image, int width, int height, Artwork art)
    {
        if (image.Length != width * height || width != 725 || height < 2169 || height > 2170) throw new InvalidDataException("Unexpected approved artwork dimensions.");
        var wood = new List<Pixel>(); var black = new List<Pixel>(); var red = new List<Pixel>();
        for (int y = 0; y < height; y += 2) for (int x = 120; x < 600; x += 2)
        {
            var p = image[y * width + x]; var kind = PlaqueSurface.Kind(p);
            if (kind == PaintKind.Wood) wood.Add(p); else if (kind == PaintKind.Black) black.Add(p); else red.Add(p);
        }
        var sourceWood = WoodStatistics.Measure(wood); var sourceBlack = WoodStatistics.Measure(black); var sourceRed = WoodStatistics.Measure(red);
        var layout = new FaceLayout(height, art.InkTop, art.InkBottom);
        var lettering = new DarkLettering(image, width, height, art, sourceWood, sourceBlack, sourceRed);
        var clearWood = new List<Pixel>();
        for (int y = 0; y < 208; y++) for (int x = 0; x < 64; x++)
            clearWood.Add(lettering.SampleGrain((x + .5f) / 64, layout.SourceV((y + .5f) / 208) * (height - 1)));
        var grain = WoodStatistics.Measure(clearWood);
        double sourceMean = grain.Mean[0] * .2126 + grain.Mean[1] * .7152 + grain.Mean[2] * .0722;
        double sourceDeviation = Math.Max(1, Math.Max(
            grain.Deviation[0] * .2126 + grain.Deviation[1] * .7152 + grain.Deviation[2] * .0722,
            sourceWood.Deviation[0] * .2126 + sourceWood.Deviation[1] * .7152 + sourceWood.Deviation[2] * .0722));
        Pixel Stain(Pixel p)
        {
            double tone = (p.R * .2126 + p.G * .7152 + p.B * .0722 - sourceMean) / sourceDeviation;
            // The same spatial grain, in the native warm stain. Compress only
            // extreme highlights/shadows smoothly rather than clipping them flat.
            if (tone > 2) tone = 2 + .75 * (1 - Math.Exp(-(tone - 2) / .75));
            if (tone < -1.5) tone = -1.5 - .6 * (1 - Math.Exp((tone + 1.5) / .6));
            byte C(int k) => (byte)Math.Max(0, Math.Min(255, Math.Round(NativeWood.Mean[k] + tone * NativeWood.Deviation[k])));
            return new Pixel(C(0), C(1), C(2), p.A);
        }
        var next = (Pixel[])_original.Clone(); var mask = (Pixel[])_gloss.Clone();
        for (int y = 0; y < Height; y++)
        {
            float v = (Y + y + .5f - Bottom) / (Top - Bottom);
            if (v < 0 || v > 1) continue;
            float sy = layout.SourceV(v) * (height - 1);
            for (int x = 0; x < Width; x++)
            {
                if (!Inside(x, y)) continue;
                int i = y * Width + x; float u = (X + x + .5f - Left) / (Right - Left);
                var beneath = Stain(lettering.SampleGrain(u, sy));
                float coverage = lettering.Coverage(u, v);
                byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * coverage);
                next[i] = new Pixel(Mix(beneath.R, Paint.R), Mix(beneath.G, Paint.G), Mix(beneath.B, Paint.B), _original[i].A);
                byte originalGloss = IsPaint(_original[i]) || _gloss[i].R > (_woodGloss + _paintGloss) / 2
                    || Math.Max(_original[i].R, Math.Max(_original[i].G, _original[i].B)) >= 105 ? _woodGloss : _gloss[i].R;
                byte g = Mix(originalGloss, _paintGloss); mask[i] = new Pixel(g, g, g, _gloss[i].A);
            }
        }
        return (next, mask);
    }

    internal static bool Inside(int x, int y) => X + x + .5f >= Left && X + x + .5f <= Right && Y + y + .5f >= Bottom && Y + y + .5f <= Top;

    // Keep the approved drink strokes and palette, but compose them over the
    // same material treatment as ramen. Paint wear affects only its own mask.
    internal (Pixel[] Albedo, Pixel[] Gloss) ComposeDrink(Pixel[] face, Pixel[] paintMask, int width, int height,
        Pixel[] grainSource, int grainWidth, int grainHeight, Artwork art, GrainImage? secondGrain = null)
    {
        if (width != 256 || height != 819 || face.Length != width * height || paintMask.Length != face.Length)
            throw new InvalidDataException("Invalid dark drink face or paint mask.");
        var next = (Pixel[])_original.Clone(); var gloss = (Pixel[])_gloss.Clone();
        var wood = new DarkDrinkWood(grainSource, grainWidth, grainHeight, art, NativeWood, secondGrain);
        var gold = new Pixel(191, 151, 79);
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
        {
            if (!Inside(x, y)) continue;
            int i = y * Width + x, source = (y - 9) * width + x - 8;
            float u = (X + x + .5f - Left) / (Right - Left), v = (Y + y + .5f - Bottom) / (Top - Bottom);
            var beneath = wood.Sample(u, v);
            uint hash = unchecked((uint)(x * 73856093 ^ y * 19349663 ^ (art.DrinkPriority + 1) * 83492791));
            byte pigment = _paintGrain[hash % (uint)_paintGrain.Length];
            double wear = Math.Max(.75, Math.Min(1, .96 * pigment / Math.Max(1, (int)Paint.G)));
            // Sparse worn flecks, stable per plaque and derived from the native
            // paint sample, give the same dry pigment feel as the dish signs.
            if ((hash >> 8) % 53 == 0) wear *= .65;
            double a = paintMask[source].R / 255.0 * wear;
            var paint = source % width >= width * .46 ? gold : Paint;
            byte Mix(byte b, byte p) => (byte)Math.Round(b + (p - b) * a);
            next[i] = new Pixel(Mix(beneath.R, paint.R), Mix(beneath.G, paint.G), Mix(beneath.B, paint.B), _original[i].A);
            byte g = (byte)Math.Round(_woodGloss + (_paintGloss - _woodGloss) * a);
            gloss[i] = new Pixel(g, g, g, _gloss[i].A);
        }
        return (next, gloss);
    }
    internal static bool IsPaint(Pixel p) => Math.Min(p.R, Math.Min(p.G, p.B)) > 115
        && Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) < 28;
    private static byte Median(IEnumerable<byte> data) { var a = data.OrderBy(v => v).ToArray(); return a[a.Length / 2]; }
}
