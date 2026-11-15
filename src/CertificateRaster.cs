using System;
using System.IO;

namespace PlaqueRamen;

// Offline/runtime-independent paper reduction. Filter premultiplied colour so
// transparent source pixels cannot introduce coloured fringes around the art.
internal static class CertificateRaster
{
    private const int MaximumSourceEdge = 4096;

    private readonly struct Sample
    {
        internal readonly double R, G, B, A;
        internal Sample(double r, double g, double b, double a) { R = r; G = g; B = b; A = a; }
    }

    private sealed class Kernel
    {
        internal readonly int First;
        internal readonly double[] Weights;
        internal Kernel(int first, double[] weights) { First = first; Weights = weights; }
    }

    internal static (int Width, int Height, Pixel[] Pixels) Downsample(Pixel[] source, int width, int height, int maxEdge)
    {
        if (source == null || width < 1 || height < 1 || width > MaximumSourceEdge || height > MaximumSourceEdge
            || (long)width * height != source.Length || maxEdge < 1)
            throw new InvalidDataException("Invalid certificate raster dimensions or pixel buffer.");
        float scale = Math.Min(1f, maxEdge / (float)Math.Max(width, height));
        int targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        int targetHeight = Math.Max(1, (int)Math.Round(height * scale));
        if (targetWidth == width && targetHeight == height)
            return (width, height, (Pixel[])source.Clone());

        var horizontal = Kernels(width, targetWidth);
        var vertical = Kernels(height, targetHeight);
        var rows = new Sample[targetWidth * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < targetWidth; x++)
        {
            var kernel = horizontal[x];
            double r = 0, g = 0, b = 0, a = 0;
            for (int i = 0; i < kernel.Weights.Length; i++)
            {
                var pixel = source[y * width + kernel.First + i];
                double weight = kernel.Weights[i], alpha = pixel.A / 255d;
                r += pixel.R * alpha * weight; g += pixel.G * alpha * weight;
                b += pixel.B * alpha * weight; a += alpha * weight;
            }
            rows[y * targetWidth + x] = new Sample(r, g, b, a);
        }

        var result = new Pixel[targetWidth * targetHeight];
        for (int y = 0; y < targetHeight; y++)
        for (int x = 0; x < targetWidth; x++)
        {
            var kernel = vertical[y];
            double r = 0, g = 0, b = 0, a = 0;
            for (int i = 0; i < kernel.Weights.Length; i++)
            {
                var pixel = rows[(kernel.First + i) * targetWidth + x];
                double weight = kernel.Weights[i];
                r += pixel.R * weight; g += pixel.G * weight; b += pixel.B * weight; a += pixel.A * weight;
            }
            byte alpha = Channel(a * 255);
            // Hidden RGB is deliberately black when filtered coverage rounds
            // to zero. Divide by the raw alpha before clamping ringing so a
            // constant visible colour stays exact even at transparent edges.
            result[y * targetWidth + x] = a <= 0 || alpha == 0
                ? new Pixel(0, 0, 0, 0)
                : new Pixel(Channel(r / a), Channel(g / a), Channel(b / a), alpha);
        }
        return (targetWidth, targetHeight, result);
    }

    private static Kernel[] Kernels(int source, int target)
    {
        var result = new Kernel[target];
        if (source == target)
        {
            for (int x = 0; x < target; x++) result[x] = new Kernel(x, new[] { 1d });
            return result;
        }
        double ratio = source / (double)target, radius = 3 * ratio;
        for (int x = 0; x < target; x++)
        {
            double centre = (x + .5) * ratio - .5;
            int first = Math.Max(0, (int)Math.Ceiling(centre - radius));
            int last = Math.Min(source - 1, (int)Math.Floor(centre + radius));
            var weights = new double[last - first + 1];
            double sum = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                double distance = (first + i - centre) / ratio;
                double weight = Math.Abs(distance) >= 3 ? 0 : Sinc(distance) * Sinc(distance / 3);
                weights[i] = weight; sum += weight;
            }
            if (Math.Abs(sum) < 1e-12) throw new InvalidDataException("Certificate reduction filter has no coverage.");
            for (int i = 0; i < weights.Length; i++) weights[i] /= sum;
            result[x] = new Kernel(first, weights);
        }
        return result;
    }

    private static double Sinc(double value)
    {
        if (Math.Abs(value) < 1e-12) return 1;
        double angle = Math.PI * value;
        return Math.Sin(angle) / angle;
    }
    private static byte Channel(double value) => (byte)Math.Max(0, Math.Min(255, Math.Round(value)));
}
