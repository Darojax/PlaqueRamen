using System;
using System.IO;

namespace PlaqueRamen;

// The dark signs have two separate vertical columns. Keep the existing brush
// lettering, but fit it without distortion into the native upper-right name
// and lower-left price areas. Coordinates here are bottom-origin face pixels.
internal sealed class DarkLettering
{
    private const float FaceWidth = 256, FaceHeight = 819.2f;
    internal readonly InkColumn Name, Price;
    private readonly Pixel[] _image;
    private readonly int _width, _height;

    internal DarkLettering(Pixel[] image, int width, int height, Artwork art,
        WoodStatistics wood, WoodStatistics black, WoodStatistics red)
    {
        _image = image; _width = width; _height = height;
        int low = height - 1 - art.InkBottom, high = height - 1 - art.InkTop;
        var price = Bounds(image, width, low, high, PaintKind.Red);
        var name = Bounds(image, width, price.Top + 12, high, PaintKind.Black);
        Name = new InkColumn(image, width, name, PaintKind.Black, wood.Mean[1], black.Mean[1],
            .685f * FaceWidth, .43f * FaceWidth, .505f * FaceHeight, .905f * FaceHeight, true);
        Price = new InkColumn(image, width, price, PaintKind.Red, wood.Mean[1], red.Mean[1],
            .275f * FaceWidth, .35f * FaceWidth, .415f * FaceHeight, .105f * FaceHeight, false);

        if (Math.Min(name.Left, price.Left) <= 220 || Math.Max(name.Right, price.Right) >= 540)
            throw new InvalidDataException("Approved lettering reaches the clear wood margins.");
    }

    internal float Coverage(float u, float v) => Math.Max(Name.Sample(u * FaceWidth, v * FaceHeight), Price.Sample(u * FaceWidth, v * FaceHeight));

    internal Pixel SampleGrain(float u, float sourceY)
    {
        // The unpainted sides of this dish retain its own vertical grain and
        // knots. Expand and crossfade those continuous strips, rather than
        // painting over the old words or leaving stroke-shaped repair marks.
        var a = PlaqueSurface.Sample(_image, _width, _height, 90 + u * 120, sourceY);
        var b = PlaqueSurface.Sample(_image, _width, _height, 550 + u * 85, sourceY);
        float t = u * u * (3 - 2 * u);
        byte Mix(byte c, byte d) => (byte)Math.Round(c + (d - c) * t);
        return new Pixel(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B), a.A);
    }

    private static InkBounds Bounds(Pixel[] image, int width, int low, int high, PaintKind kind)
    {
        if (low >= high) throw new InvalidDataException("Dark sign lettering areas overlap or are missing.");
        var rows = new int[high - low + 1]; int max = 0;
        for (int y = low; y <= high; y++)
        {
            for (int x = 120; x < 600; x++) if (PlaqueSurface.Kind(image[y * width + x]) == kind) rows[y - low]++;
            max = Math.Max(max, rows[y - low]);
        }
        int first = high, last = low;
        for (int y = low; y <= high; y++) if (rows[y - low] >= Math.Max(6, max * .05f)) { first = Math.Min(first, y); last = y; }
        if (first >= last) throw new InvalidDataException("Dark sign brush lettering could not be located.");
        first = Math.Max(low, first - 4); last = Math.Min(high, last + 4);
        int left = 599, right = 120;
        for (int y = first; y <= last; y++) for (int x = 120; x < 600; x++)
            if (PlaqueSurface.Kind(image[y * width + x]) == kind) { left = Math.Min(left, x); right = Math.Max(right, x); }
        if (right - left < 20 || last - first < 20) throw new InvalidDataException("Dark sign brush lettering is incomplete.");
        return new InkBounds(left, first, right, last);
    }

    internal readonly struct InkBounds
    {
        internal readonly int Left, Bottom, Right, Top;
        internal int Width => Right - Left + 1;
        internal int Height => Top - Bottom + 1;
        internal InkBounds(int left, int bottom, int right, int top) { Left = left; Bottom = bottom; Right = right; Top = top; }
    }

    internal sealed class InkColumn
    {
        internal readonly float Left, Bottom, Width, Height;
        private readonly float[] _coverage;
        private readonly int _width, _height;
        private readonly float _scale;

        internal InkColumn(Pixel[] image, int imageWidth, InkBounds bounds, PaintKind kind, double wood, double ink,
            float center, float maxWidth, float maxHeight, float anchor, bool topAligned)
        {
            _width = bounds.Width; _height = bounds.Height;
            _scale = Math.Min(maxWidth / _width, maxHeight / _height);
            Width = _width * _scale; Height = _height * _scale;
            Left = center - Width * .5f; Bottom = topAligned ? anchor - Height : anchor;
            _coverage = new float[_width * _height];
            for (int y = 0; y < _height; y++) for (int x = 0; x < _width; x++)
            {
                var p = image[(bounds.Bottom + y) * imageWidth + bounds.Left + x];
                if (PlaqueSurface.Kind(p) == kind) _coverage[y * _width + x] = (float)Math.Max(0, Math.Min(1, (wood - p.G) / Math.Max(1, wood - ink)));
            }
        }

        internal float Sample(float x, float y)
        {
            x = (x - Left) / _scale - .5f; y = (y - Bottom) / _scale - .5f;
            if (x < -1 || y < -1 || x >= _width || y >= _height) return 0;
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float a = x - ix, b = y - iy;
            float At(int px, int py) => px < 0 || py < 0 || px >= _width || py >= _height ? 0 : _coverage[py * _width + px];
            return (At(ix, iy) * (1 - a) + At(ix + 1, iy) * a) * (1 - b)
                + (At(ix, iy + 1) * (1 - a) + At(ix + 1, iy + 1) * a) * b;
        }
    }
}
