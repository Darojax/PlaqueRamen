using System;
using System.Linq;
using PlaqueRamen;

internal static class CertificateRasterChecks
{
    internal static void Run(Action<bool, string> check, Action<Action, string> refuses)
    {
        bool Equal(Pixel a, Pixel b) => a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;
        foreach (var (width, height, edge, expectedWidth, expectedHeight) in new[] {
            (9, 5, 4, 4, 2), (5, 9, 4, 2, 4), (13, 13, 8, 8, 8),
            (1, 9, 3, 1, 3), (9, 1, 3, 3, 1), (3, 2, 8, 3, 2),
            (1122, 1403, 768, 614, 768), (1450, 1086, 1024, 1024, 767) })
        {
            var source = Enumerable.Repeat(new Pixel(96, 121, 53), width * height).ToArray();
            var reduced = CertificateRaster.Downsample(source, width, height, edge);
            check(reduced.Width == expectedWidth && reduced.Height == expectedHeight && reduced.Pixels.Length == expectedWidth * expectedHeight,
                "portrait, landscape and thin papers keep the specified rounded aspect budget: " + width + "x" + height);
            check(reduced.Pixels.All(p => Equal(p, source[0])), "edge-normalised Lanczos filtering preserves constant paper colour: " + width + "x" + height);
        }

        foreach (var colour in new[] { new Pixel(51, 92, 203, 255), new Pixel(18, 127, 233, 73),
            new Pixel(0, 0, 0, 255), new Pixel(255, 255, 255, 255), new Pixel(0, 0, 0, 0) })
        {
            var pixels = Enumerable.Repeat(colour, 17 * 11).ToArray();
            check(CertificateRaster.Downsample(pixels, 17, 11, 9).Pixels.All(p => Equal(p, colour)),
                "constant opaque, translucent and transparent paper survives filtering without a colour/alpha shift");
        }

        var gradient = Enumerable.Range(0, 40 * 24).Select(i => new Pixel((byte)(i % 40 * 6), (byte)(i / 40 * 10), 61, (byte)(100 + i % 156))).ToArray();
        var original = gradient.ToArray();
        var down = CertificateRaster.Downsample(gradient, 40, 24, 16);
        check(gradient.Zip(original).All(p => Equal(p.First, p.Second)), "reduction never modifies the high-resolution source artwork");
        check(down.Pixels.Zip(CertificateRaster.Downsample(gradient, 40, 24, 16).Pixels).All(p => Equal(p.First, p.Second)),
            "prepared artwork reduction is byte-deterministic");
        var unchanged = CertificateRaster.Downsample(gradient, 40, 24, int.MaxValue);
        check(unchanged.Width == 40 && unchanged.Height == 24 && unchanged.Pixels.Zip(original).All(p => Equal(p.First, p.Second)),
            "a larger budget never upscales or resamples existing artwork");
        unchanged.Pixels[0] = new Pixel(255, 0, 0);
        check(!ReferenceEquals(unchanged.Pixels, gradient) && Equal(gradient[0], original[0]), "even no-op results cannot alias and mutate the source buffer");

        var stripes = Enumerable.Range(0, 64 * 8).Select(i => i % 2 == 0 ? new Pixel(0, 0, 0) : new Pixel(255, 255, 255)).ToArray();
        var averaged = CertificateRaster.Downsample(stripes, 64, 8, 8);
        check(averaged.Width == 8 && averaged.Height == 1 && averaged.Pixels.Skip(3).Take(2).All(p => p.R is >= 126 and <= 129 && p.R == p.G && p.G == p.B && p.A == 255),
            "high-frequency strokes are area-filtered instead of aliasing to all-black or all-white");

        var ink = Enumerable.Repeat(new Pixel(232, 220, 185), 128 * 48).ToArray();
        for (int y = 0; y < 48; y++) for (int x = 0; x < 128; x++)
            if ((x >= 32 && x <= 34) || (x >= 72 && x <= 74) || (y >= 24 && y <= 26)) ink[y * 128 + x] = new Pixel(0, 0, 0);
        var text = CertificateRaster.Downsample(ink, 128, 48, 64);
        check(Enumerable.Range(15, 4).Any(x => text.Pixels[8 * text.Width + x].R < 60)
            && Enumerable.Range(35, 4).Any(x => text.Pixels[8 * text.Width + x].R < 60)
            && text.Pixels[12 * text.Width + 25].R < 60,
            "thin vertical and horizontal ink strokes retain strong contrast after reduction");
        check(text.Pixels[5 * text.Width + 2].R >= 225 && text.Pixels[5 * text.Width + 2].B >= 180,
            "blank paper away from fine lettering keeps its warm light tone");

        var step = Enumerable.Range(0, 64 * 8).Select(i => i % 64 < 32 ? new Pixel(0, 0, 0) : new Pixel(255, 255, 255)).ToArray();
        var bounded = CertificateRaster.Downsample(step, 64, 8, 20);
        check(bounded.Pixels.Take(7).All(p => p.R < 9) && bounded.Pixels.Skip(13).Take(7).All(p => p.R > 246),
            "Lanczos overshoot clamps at black and white instead of wrapping byte channels");

        var edgePixels = Enumerable.Range(0, 32 * 12).Select(i => i % 32 < 16 ? new Pixel(220, 40, 20, 255) : new Pixel(0, 0, 255, 0)).ToArray();
        var transparent = CertificateRaster.Downsample(edgePixels, 32, 12, 11).Pixels;
        check(transparent.Any(p => p.A is > 0 and < 255)
            && transparent.Where(p => p.A > 0).All(p => Math.Abs(p.R - 220) <= 1 && Math.Abs(p.G - 40) <= 1 && Math.Abs(p.B - 20) <= 1),
            "premultiplied-alpha reduction keeps hidden blue out of partially transparent red edges");
        check(transparent.Where(p => p.A == 0).All(p => p.R == 0 && p.G == 0 && p.B == 0),
            "fully transparent filtered edges contain no stray hidden colour");
        check(transparent.Average(p => p.A) is > 120 and < 135, "transparent edge reduction preserves approximately half-covered alpha");

        var asymmetric = Enumerable.Range(0, 20 * 12).Select(i => new Pixel((byte)(i % 20 * 10), (byte)(i / 20 * 20), (byte)(i * 3 % 256), (byte)(50 + i % 206))).ToArray();
        var transposed = new Pixel[asymmetric.Length];
        for (int y = 0; y < 12; y++) for (int x = 0; x < 20; x++) transposed[x * 12 + y] = asymmetric[y * 20 + x];
        var a = CertificateRaster.Downsample(asymmetric, 20, 12, 10);
        var b = CertificateRaster.Downsample(transposed, 12, 20, 10);
        check(Enumerable.Range(0, a.Height).All(y => Enumerable.Range(0, a.Width).All(x => Equal(a.Pixels[y * a.Width + x], b.Pixels[x * b.Width + y]))),
            "separable filtering has no preferred axis or unintended image flip");

        refuses(() => CertificateRaster.Downsample(null!, 1, 1, 1), "null certificate pixel data is refused");
        refuses(() => CertificateRaster.Downsample(Array.Empty<Pixel>(), 0, 1, 1), "zero-width certificate data is refused");
        refuses(() => CertificateRaster.Downsample(Array.Empty<Pixel>(), 1, -1, 1), "negative-height certificate data is refused");
        refuses(() => CertificateRaster.Downsample(new Pixel[4], 3, 2, 1), "inconsistent certificate pixel counts are refused");
        refuses(() => CertificateRaster.Downsample(new Pixel[1], 1, 1, 0), "zero reduction budget is refused");
        refuses(() => CertificateRaster.Downsample(new Pixel[1], 1, 1, -1), "negative reduction budget is refused");
        refuses(() => CertificateRaster.Downsample(Array.Empty<Pixel>(), int.MaxValue, int.MaxValue, 1), "overflow-sized certificate dimensions are refused before allocation");
        refuses(() => CertificateRaster.Downsample(Array.Empty<Pixel>(), 4097, 1, 1), "certificate source allocation is bounded to the reviewed edge budget");
    }
}
