using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


namespace PlaqueRamen;

internal enum PaintKind { Wood, Black, Red }

// Pure CPU material composition, shared by the game and offline preview.
// Each dish keeps its own authored grain. Only its colour/contrast is matched
// to unpainted pixels of the game's original plaque material.
internal sealed class PlaqueSurface
{
    internal const int X = 1180, Y = 924, Width = 250, Height = 884;
    internal readonly Pixel Black, Red;
    internal readonly WoodStatistics NativeWood;
    private readonly WoodStatistics _nativeBlack, _nativeRed;
    private readonly Pixel[] _original, _gloss;
    private readonly byte[] _woodGloss;
    private readonly byte _blackGloss, _redGloss;

    internal PlaqueSurface(Pixel[] original, Pixel[] gloss)
    {
        if (original.Length != Width * Height || gloss.Length != original.Length) throw new InvalidDataException("Invalid original plaque face.");
        _original = (Pixel[])original.Clone(); _gloss = (Pixel[])gloss.Clone();
        var black = new List<Pixel>(); var red = new List<Pixel>(); var wood = new List<Pixel>();
        var blackGloss = new List<byte>(); var redGloss = new List<byte>();
        _woodGloss = new byte[Height];
        for (int y = 0; y < Height; y++)
        {
            var row = new List<byte>();
            for (int x = 0; x < Width; x++)
            {
                if (!Inside(x, y)) continue;
                int i = y * Width + x; var p = _original[i];
                var kind = Kind(p);
                if (kind == PaintKind.Black) { black.Add(p); blackGloss.Add(_gloss[i].R); }
                else if (kind == PaintKind.Red) { red.Add(p); redGloss.Add(_gloss[i].R); }
                else { wood.Add(p); row.Add(_gloss[i].R); }
            }
            _woodGloss[y] = row.Count == 0 ? (byte)40 : Median(row);
        }
        if (black.Count < 100 || red.Count < 100 || wood.Count < 1000) throw new InvalidDataException("Original plaque paint/wood could not be measured.");
        Black = Median(black); Red = Median(red); NativeWood = WoodStatistics.Measure(wood);
        _nativeBlack = WoodStatistics.Measure(black); _nativeRed = WoodStatistics.Measure(red);
        _blackGloss = Median(blackGloss); _redGloss = Median(redGloss);
    }

    internal (Pixel[] Albedo, Pixel[] Gloss) Compose(Pixel[] image, int width, int height, Artwork art)
    {
        if (image.Length != width * height || width != 725 || height < 2169 || height > 2170) throw new InvalidDataException("Unexpected approved artwork dimensions.");
        var wood = new List<Pixel>(); var black = new List<Pixel>(); var red = new List<Pixel>();
        for (int y = 0; y < height; y += 2)
            for (int x = 120; x < 600; x += 2)
            {
                var p = image[y * width + x]; var kind = Kind(p);
                if (kind == PaintKind.Wood) wood.Add(p);
                else if (kind == PaintKind.Black) black.Add(p);
                else red.Add(p);
            }
        var grade = new WoodGrade(WoodStatistics.Measure(wood), NativeWood);
        var blackGrade = new WoodGrade(WoodStatistics.Measure(black), _nativeBlack);
        var redGrade = new WoodGrade(WoodStatistics.Measure(red), _nativeRed);
        var layout = new FaceLayout(height, art.InkTop, art.InkBottom);
        var next = (Pixel[])_original.Clone(); var mask = (Pixel[])_gloss.Clone();
        float sourceSpan = (FaceLayout.FaceRight - FaceLayout.FaceLeft) * 635f / Width / FaceLayout.LetterWidthGain;
        for (int y = 0; y < Height; y++)
        {
            float v = (Y + y + .5f - FaceLayout.FaceBottom) / (FaceLayout.FaceTop - FaceLayout.FaceBottom);
            if (v < 0 || v > 1) continue;
            float sy = layout.SourceV(v) * (height - 1);
            for (int x = 0; x < Width; x++)
            {
                if (!Inside(x, y)) continue;
                int i = y * Width + x;
                float u = (X + x + .5f - FaceLayout.FaceLeft) / (FaceLayout.FaceRight - FaceLayout.FaceLeft);
                var p = Sample(image, width, height, (width - 1) * .5f + (u - .5f) * sourceSpan, sy);
                var kind = Kind(p);
                next[i] = kind == PaintKind.Black ? blackGrade.Apply(p) : kind == PaintKind.Red ? redGrade.Apply(p) : grade.Apply(p);
                byte g = kind == PaintKind.Black ? _blackGloss : kind == PaintKind.Red ? _redGloss
                    : Kind(_original[i]) == PaintKind.Wood ? _gloss[i].R : _woodGloss[y];
                mask[i] = new Pixel(g, g, g, _gloss[i].A);
            }
        }
        return (next, mask);
    }

    internal static bool Inside(int x, int y) => X + x + .5f >= FaceLayout.FaceLeft && X + x + .5f <= FaceLayout.FaceRight
        && Y + y + .5f >= FaceLayout.FaceBottom && Y + y + .5f <= FaceLayout.FaceTop;

    // Drink lettering has its own palette. Keep the established ramen composer
    // unchanged, including all approved wood and black/red paint grading.
    internal (Pixel[] Albedo, Pixel[] Gloss) ComposeDrink(Pixel[] image, int width, int height, Artwork art)
    {
        if (image.Length != width * height || width != 725 || height < 2169 || height > 2170)
            throw new InvalidDataException("Unexpected drink artwork dimensions.");
        var wood = new List<Pixel>(); var red = new List<Pixel>(); var blue = new List<Pixel>();
        for (int y = 0; y < height; y += 2) for (int x = 140; x < 590; x += 2)
        {
            var p = image[y * width + x];
            if (IsBlue(p)) blue.Add(p); else if (Kind(p) == PaintKind.Red) red.Add(p); else if (Kind(p) == PaintKind.Wood) wood.Add(p);
        }
        if (blue.Count < 100) throw new InvalidDataException("Blue drink lettering is missing.");
        var blueGrade = new WoodGrade(WoodStatistics.Measure(blue), _nativeBlack.WithMean(new Pixel(47, 70, 95)));
        var grade = new WoodGrade(WoodStatistics.Measure(wood), NativeWood);
        var redGrade = new WoodGrade(WoodStatistics.Measure(red), _nativeRed);
        var layout = new FaceLayout(height, art.InkTop, art.InkBottom);
        var next = (Pixel[])_original.Clone(); var mask = (Pixel[])_gloss.Clone();
        float span = (FaceLayout.FaceRight - FaceLayout.FaceLeft) * 635f / Width / FaceLayout.LetterWidthGain;
        for (int y = 0; y < Height; y++)
        {
            float v = (Y + y + .5f - FaceLayout.FaceBottom) / (FaceLayout.FaceTop - FaceLayout.FaceBottom);
            if (v < 0 || v > 1) continue;
            for (int x = 0; x < Width; x++)
            {
                if (!Inside(x, y)) continue;
                int i = y * Width + x;
                float u = (X + x + .5f - FaceLayout.FaceLeft) / (FaceLayout.FaceRight - FaceLayout.FaceLeft);
                float sy = layout.SourceV(v) * (height - 1);
                // The generated PNG's extreme rows contain coloured transparent
                // edge pixels. Use continuous bare grain just inside those rows,
                // while preserving the approved ink mapping exactly.
                if (v < FaceLayout.BottomMargin)
                    sy = 16 + (layout.SourceLow * (height - 1) - 16) * v / FaceLayout.BottomMargin;
                else if (v > 1 - FaceLayout.TopMargin)
                    sy = layout.SourceHigh * (height - 1) + (height - 17 - layout.SourceHigh * (height - 1))
                        * (v - 1 + FaceLayout.TopMargin) / FaceLayout.TopMargin;
                var p = Sample(image, width, height, (width - 1) * .5f + (u - .5f) * span, sy);
                bool inInk = sy >= height - 1 - art.InkBottom - 2 && sy <= height - 1 - art.InkTop + 2;
                bool name = inInk && IsBlue(p), price = inInk && !name && Kind(p) == PaintKind.Red;
                next[i] = name ? blueGrade.Apply(p) : price ? redGrade.Apply(p) : grade.Apply(p);
                byte g = name ? _blackGloss : price ? _redGloss : Kind(_original[i]) == PaintKind.Wood ? _gloss[i].R : _woodGloss[y];
                mask[i] = new Pixel(g, g, g, _gloss[i].A);
            }
        }
        return (next, mask);
    }

    internal static bool IsBlue(Pixel p) => p.B > p.R * 1.12f && p.G > p.R * 1.08f;

    internal static PaintKind Kind(Pixel p)
    {
        int max = Math.Max(p.R, Math.Max(p.G, p.B)), min = Math.Min(p.R, Math.Min(p.G, p.B));
        if (max < 105 && max - min < max * .25f + 8) return PaintKind.Black;
        if (p.R > p.G * 1.45f && p.R > p.B * 1.25f && p.B > p.G * .9f && p.G < 122) return PaintKind.Red;
        return PaintKind.Wood;
    }

    internal static Pixel Sample(Pixel[] image, int width, int height, float x, float y)
    {
        int x0 = Math.Max(0, Math.Min(width - 1, (int)Math.Floor(x))), y0 = Math.Max(0, Math.Min(height - 1, (int)Math.Floor(y)));
        int x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
        float a = Math.Max(0, Math.Min(1, x - x0)), b = Math.Max(0, Math.Min(1, y - y0));
        var p = image[y0 * width + x0]; var q = image[y0 * width + x1]; var s = image[y1 * width + x0]; var t = image[y1 * width + x1];
        byte L(byte c, byte d, byte e, byte f) => (byte)Math.Round((c + (d - c) * a) * (1 - b) + (e + (f - e) * a) * b);
        return new Pixel(L(p.R, q.R, s.R, t.R), L(p.G, q.G, s.G, t.G), L(p.B, q.B, s.B, t.B));
    }

    private static byte Median(IEnumerable<byte> data) { var a = data.OrderBy(v => v).ToArray(); return a[a.Length / 2]; }
    private static Pixel Median(List<Pixel> data) => new(Median(data.Select(p => p.R)), Median(data.Select(p => p.G)), Median(data.Select(p => p.B)));
}

internal sealed class WoodStatistics
{
    internal readonly double[] Mean, Deviation;
    private WoodStatistics(double[] mean, double[] deviation) { Mean = mean; Deviation = deviation; }
    internal WoodStatistics WithMean(Pixel paint) => new(new double[] { paint.R, paint.G, paint.B }, (double[])Deviation.Clone());
    internal static WoodStatistics Measure(IEnumerable<Pixel> data)
    {
        var sums = new double[3]; var squares = new double[3]; int count = 0;
        foreach (var p in data)
        {
            count++;
            sums[0] += p.R; sums[1] += p.G; sums[2] += p.B;
            squares[0] += p.R * p.R; squares[1] += p.G * p.G; squares[2] += p.B * p.B;
        }
        if (count < 10) throw new InvalidDataException("Insufficient wood pixels for material matching.");
        var mean = sums.Select(v => v / count).ToArray();
        return new WoodStatistics(mean, Enumerable.Range(0, 3).Select(k => Math.Sqrt(Math.Max(0, squares[k] / count - mean[k] * mean[k]))).ToArray());
    }
}

internal sealed class WoodGrade
{
    private readonly double[] _gain = new double[3], _offset = new double[3];
    internal WoodGrade(WoodStatistics source, WoodStatistics target)
    {
        for (int k = 0; k < 3; k++)
        {
            _gain[k] = Math.Max(.5, Math.Min(2.5, target.Deviation[k] / Math.Max(1, source.Deviation[k])));
            _offset[k] = target.Mean[k] - source.Mean[k] * _gain[k];
        }
    }
    internal Pixel Apply(Pixel p)
    {
        byte C(byte value, int k) => (byte)Math.Max(0, Math.Min(255, Math.Round(value * _gain[k] + _offset[k])));
        return new Pixel(C(p.R, 0), C(p.G, 1), C(p.B, 2), p.A);
    }
}
