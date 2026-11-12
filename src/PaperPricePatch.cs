using System;
using System.IO;
using System.Linq;


namespace PlaqueRamen;

// Only the first two digits of the native 700-yen label. Coordinates and
// buffers are bottom-origin, like Unity GetPixels32; the last 0 and yen stay native.
internal static class PaperPricePatch
{
    internal const string Atlas = "T_Signs_Information001_BASE";
    internal const int Size = 1024, X = 736, Y = 112, Width = 104, Height = 80;

    internal static double MatchError(Pixel[] atlas, Pixel[] expected)
    {
        if (atlas.Length != Size * Size || expected.Length != Width * Height)
            throw new InvalidDataException("Paper sign texture dimensions changed.");
        long error = 0; int worst = 0;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var p = atlas[(Y + y) * Size + X + x]; var q = expected[y * Width + x];
                foreach (int d in new[] { Math.Abs(p.R - q.R), Math.Abs(p.G - q.G), Math.Abs(p.B - q.B) })
                { error += d; worst = Math.Max(worst, d); }
            }
        double mean = error / (double)(Width * Height * 3);
        // Small DXT1 decoder / sRGB round-trip differences are allowed. New art
        // at this location is not overwritten, even if its texture name is unchanged.
        if (worst > 8 || mean > 2) throw new InvalidDataException("The paper beer sign no longer matches the original 700-yen label.");
        return mean;
    }

    internal static Pixel[] Compose(Pixel[] atlas, Pixel[] expected, Pixel[] replacement, int width, int height)
    {
        MatchError(atlas, expected);
        if (width < 2 || height < 2 || width > 4096 || height > 4096 || replacement.Length != width * height)
            throw new InvalidDataException("Invalid replacement price image.");
        var scaled = new Pixel[Width * Height];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                scaled[y * Width + x] = Sample(replacement, width, height,
                    (x + .5f) * width / Width - .5f, (y + .5f) * height / Height - .5f);

        // Match the generated print to the native paper and ink, independently
        // per colour channel. This keeps the original warm charcoal/beige palette.
        var sourceInk = Tone(scaled, false); var sourcePaper = Tone(scaled, true);
        var targetInk = Tone(expected, false); var targetPaper = Tone(expected, true);
        if (Enumerable.Range(0, 3).Any(k => sourcePaper[k] - sourceInk[k] < 32 || targetPaper[k] - targetInk[k] < 32))
            throw new InvalidDataException("The price image has no clear paper/ink contrast.");
        var result = (Pixel[])atlas.Clone();
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = (Y + y) * Size + X + x;
                var p = scaled[y * Width + x]; var native = atlas[i];
                float blend = Math.Min(1, Math.Min(Math.Min(x, Width - 1 - x), Math.Min(y, Height - 1 - y)) / 2f);
                byte Channel(int k, byte value, byte old) => (byte)Math.Round(Math.Max(0, Math.Min(255,
                    old + blend * (targetInk[k] + (value - sourceInk[k]) * (targetPaper[k] - targetInk[k]) / (sourcePaper[k] - sourceInk[k]) - old))));
                result[i] = new Pixel(Channel(0, p.R, native.R), Channel(1, p.G, native.G), Channel(2, p.B, native.B), native.A);
            }
        return result;
    }

    private static double[] Tone(Pixel[] pixels, bool paper)
    {
        var ranked = pixels.OrderBy(p => p.R + p.G + p.B).ToArray();
        int n = Math.Max(1, ranked.Length / (paper ? 5 : 10));
        var selected = paper ? ranked.Skip(ranked.Length - n) : ranked.Take(n);
        return new[] { selected.Average(p => p.R), selected.Average(p => p.G), selected.Average(p => p.B) };
    }

    private static Pixel Sample(Pixel[] data, int w, int h, float x, float y)
    {
        x = Math.Max(0, Math.Min(w - 1, x)); y = Math.Max(0, Math.Min(h - 1, y));
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(w - 1, x0 + 1), y1 = Math.Min(h - 1, y0 + 1);
        float fx = x - x0, fy = y - y0;
        Pixel a = data[y0 * w + x0], b = data[y0 * w + x1], c = data[y1 * w + x0], d = data[y1 * w + x1];
        byte Mix(byte aa, byte bb, byte cc, byte dd) => (byte)Math.Round((aa * (1 - fx) + bb * fx) * (1 - fy) + (cc * (1 - fx) + dd * fx) * fy);
        return new Pixel(Mix(a.R, b.R, c.R, d.R), Mix(a.G, b.G, c.G, d.G), Mix(a.B, b.B, c.B, d.B));
    }
}
