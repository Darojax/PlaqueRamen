using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlaqueRamen;

// Dark counterparts use the exact approved lettering/grain source images.
// Only their material changes: native dark stain and weathered off-white paint.
internal sealed class DarkPlaqueSurface
{
    internal const int X = 1478, Y = 948, Width = 273, Height = 837;
    internal const float Left = 1486.3132f, Right = 1742.3132f, Bottom = 956.9053f, Top = 1776.1052f;
    internal readonly WoodStatistics NativeWood;
    internal readonly Pixel Paint;
    private readonly Pixel[] _original, _gloss;
    private readonly byte _woodGloss, _paintGloss;

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
        double sourceMean = sourceWood.Mean[0] * .2126 + sourceWood.Mean[1] * .7152 + sourceWood.Mean[2] * .0722;
        double sourceDeviation = Math.Max(1, sourceWood.Deviation[0] * .2126 + sourceWood.Deviation[1] * .7152 + sourceWood.Deviation[2] * .0722);
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
        var layout = new FaceLayout(height, art.InkTop, art.InkBottom);
        var next = (Pixel[])_original.Clone(); var mask = (Pixel[])_gloss.Clone();
        float span = (Right - Left) * 635f / Width / FaceLayout.LetterWidthGain;
        for (int y = 0; y < Height; y++)
        {
            float v = (Y + y + .5f - Bottom) / (Top - Bottom);
            if (v < 0 || v > 1) continue;
            float sy = layout.SourceV(v) * (height - 1);
            var edgeA = PlaqueSurface.Sample(image, width, height, 90, sy);
            var edgeB = PlaqueSurface.Sample(image, width, height, 635, sy);
            if (PlaqueSurface.Kind(edgeA) != PaintKind.Wood) edgeA = wood[wood.Count / 2];
            if (PlaqueSurface.Kind(edgeB) != PaintKind.Wood) edgeB = edgeA;
            var beneath = Stain(new Pixel((byte)((edgeA.R + edgeB.R) / 2), (byte)((edgeA.G + edgeB.G) / 2), (byte)((edgeA.B + edgeB.B) / 2)));
            for (int x = 0; x < Width; x++)
            {
                if (!Inside(x, y)) continue;
                int i = y * Width + x; float u = (X + x + .5f - Left) / (Right - Left);
                var p = PlaqueSurface.Sample(image, width, height, (width - 1) * .5f + (u - .5f) * span, sy);
                var kind = PlaqueSurface.Kind(p);
                // The authored bounds exclude dark board-edge marks and knots.
                // Those stay wood in the clear pin/bottom margins, never chalk.
                if (v < FaceLayout.BottomMargin || v > 1 - FaceLayout.TopMargin) kind = PaintKind.Wood;
                if (kind == PaintKind.Wood)
                {
                    next[i] = Stain(p);
                    byte originalGloss = IsPaint(_original[i]) || _gloss[i].R > (_woodGloss + _paintGloss) / 2
                        || Math.Max(_original[i].R, Math.Max(_original[i].G, _original[i].B)) >= 105 ? _woodGloss : _gloss[i].R;
                    mask[i] = new Pixel(originalGloss, originalGloss, originalGloss, _gloss[i].A);
                }
                else
                {
                    // Invert pigment coverage, not the whole image: dark ink and
                    // red prices become the same native off-white brush paint.
                    var ink = kind == PaintKind.Black ? sourceBlack : sourceRed;
                    double coverage = Math.Max(0, Math.Min(1, (sourceWood.Mean[1] - p.G) / Math.Max(1, sourceWood.Mean[1] - ink.Mean[1])));
                    byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * coverage);
                    next[i] = new Pixel(Mix(beneath.R, Paint.R), Mix(beneath.G, Paint.G), Mix(beneath.B, Paint.B), _original[i].A);
                    byte g = Mix(_woodGloss, _paintGloss); mask[i] = new Pixel(g, g, g, _gloss[i].A);
                }
            }
        }
        return (next, mask);
    }

    internal static bool Inside(int x, int y) => X + x + .5f >= Left && X + x + .5f <= Right && Y + y + .5f >= Bottom && Y + y + .5f <= Top;
    internal static bool IsPaint(Pixel p) => Math.Min(p.R, Math.Min(p.G, p.B)) > 115
        && Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) < 28;
    private static byte Median(IEnumerable<byte> data) { var a = data.OrderBy(v => v).ToArray(); return a[a.Length / 2]; }
}
