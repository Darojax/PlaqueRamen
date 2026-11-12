using PlaqueRamen;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class DarkPreview
{
    internal static void Run(string root, Artwork[] arts, Action<bool, string> check)
    {
        string input = Path.Combine(root, "artifacts/dark-preview-inputs"), output = Path.Combine(root, "artifacts/dark-preview-0.1.9");
        Directory.CreateDirectory(output);
        var original = Read(Path.Combine(input, "original.ppm")); var gloss = Read(Path.Combine(input, "gloss.ppm"));
        Pixel[] Crop(Pixel[] data) => Enumerable.Range(0, DarkPlaqueSurface.Height).SelectMany(y => Enumerable.Range(0, DarkPlaqueSurface.Width)
            .Select(x => data[(DarkPlaqueSurface.Y + y) * 2048 + DarkPlaqueSurface.X + x])).ToArray();
        var old = Crop(original.Pixels); var oldGloss = Crop(gloss.Pixels); var surface = new DarkPlaqueSurface(old, oldGloss);
        Write(Path.Combine(output, "native-original.ppm"), View(old), 256, 819);
        var hashes = new HashSet<string>(); var records = new List<object>();
        foreach (var art in arts)
        {
            var source = Read(Path.Combine(input, art.Id + ".ppm"));
            var composed = surface.Compose(source.Pixels, source.Width, source.Height, art);
            var view = View(composed.Albedo); var viewGloss = View(composed.Gloss);
            Write(Path.Combine(output, art.Id + ".ppm"), view, 256, 819);
            var wood = view.Where(p => Math.Max(p.R, Math.Max(p.G, p.B)) < 105).ToArray();
            var stats = WoodStatistics.Measure(wood);
            check(Enumerable.Range(0, 3).All(k => Math.Abs(stats.Mean[k] - surface.NativeWood.Mean[k]) < 8), "actual dark stain stays close to native: " + art.Id);
            check(stats.Deviation.All(d => d > 3), "actual dark grain keeps contrast: " + art.Id);
            var ink = view.Select((p, i) => (p, i)).Where(t => DarkPlaqueSurface.IsPaint(t.p)).Select(t => t.i).ToArray();
            int lo = ink.Min(i => i / 256), hi = ink.Max(i => i / 256);
            check(lo >= 819 * .07 && hi <= 819 * .91 && hi - lo >= 819 * .75, "dark lettering uses the safe area without approaching the pin or bottom edge: " + art.Id);
            check(ink.All(i => Math.Abs(view[i].R - view[i].G) < 12 && Math.Abs(view[i].G - view[i].B) < 12), "dark name and price both use native off-white paint: " + art.Id);
            var name = ink.Where(i => i % 256 > 256 * .46).ToArray();
            var price = ink.Where(i => i % 256 < 256 * .46).ToArray();
            check(name.Length > 200 && price.Length > 200 && name.Length + price.Length == ink.Length,
                "actual dark render retains both readable columns: " + art.Id);
            check(name.Min(i => i % 256) >= 256 * .46 && name.Max(i => i % 256) <= 256 * .91
                && name.Min(i => i / 256) >= 819 * .39 && name.Max(i => i / 256) <= 819 * .91,
                "actual dish name occupies the upper-right native area: " + art.Id);
            check(price.Min(i => i % 256) >= 256 * .09 && price.Max(i => i % 256) <= 256 * .46
                && price.Min(i => i / 256) >= 819 * .10 && price.Max(i => i / 256) <= 819 * .53,
                "actual price occupies the lower-left native area: " + art.Id);
            check(name.Average(i => i % 256) > price.Average(i => i % 256) + 256 * .3
                && name.Average(i => i / 256) > price.Average(i => i / 256) + 819 * .25,
                "dark lettering has the native stagger rather than a central stack: " + art.Id);
            check(ink.Average(i => viewGloss[i].R) > 100, "dark brush paint follows native gloss: " + art.Id);
            for (int i = 0; i < old.Length; i++) if (!DarkPlaqueSurface.Inside(i % DarkPlaqueSurface.Width, i / DarkPlaqueSurface.Width))
                if (!Equal(old[i], composed.Albedo[i]) || !Equal(oldGloss[i], composed.Gloss[i])) throw new Exception("Dark edge material changed.");
            check(true, "dark board edge and unrelated material pixels remain exact: " + art.Id);
            var grain = Enumerable.Range(760, 35).SelectMany(y => Enumerable.Range(20, 35).Select(x => view[y * 256 + x])).ToArray();
            string hash = Convert.ToHexString(SHA256.HashData(grain.SelectMany(p => new[] { p.R, p.G, p.B }).ToArray()));
            hashes.Add(hash);
            check(WoodStatistics.Measure(grain).Deviation.All(d => d > .5), "individual upper grain patch is not a flat fill: " + art.Id);
            object Area(int[] pixels) => new { Left = pixels.Min(i => i % 256) / 256d, Right = pixels.Max(i => i % 256) / 256d,
                Bottom = pixels.Min(i => i / 256) / 819d, Top = pixels.Max(i => i / 256) / 819d, PaintPixels = pixels.Length };
            records.Add(new { art.Id, art.Dish, art.Yen, WoodMean = stats.Mean, WoodDeviation = stats.Deviation, InkBottom = lo / 819d, InkTop = hi / 819d,
                Name = Area(name), Price = Area(price), GrainHash = hash });
        }
        check(hashes.Count == 10, "all ten dark dishes retain distinct grain patterns");
        File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new { NativeWoodMean = surface.NativeWood.Mean,
            NativeWoodDeviation = surface.NativeWood.Deviation, WhitePaint = new int[] { surface.Paint.R, surface.Paint.G, surface.Paint.B },
            DistinctGrains = hashes.Count, SameCompositorAsGame = true, InGameLighting = false, Plaques = records }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool Equal(Pixel a, Pixel b) => a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;
    private static Pixel[] View(Pixel[] face)
    {
        var output = new Pixel[256 * 819];
        for (int y = 0; y < 819; y++) for (int x = 0; x < 256; x++)
            output[y * 256 + x] = PlaqueSurface.Sample(face, DarkPlaqueSurface.Width, DarkPlaqueSurface.Height,
                DarkPlaqueSurface.Left - DarkPlaqueSurface.X + (x + .5f) / 256 * (DarkPlaqueSurface.Right - DarkPlaqueSurface.Left) - .5f,
                DarkPlaqueSurface.Bottom - DarkPlaqueSurface.Y + (y + .5f) / 819 * (DarkPlaqueSurface.Top - DarkPlaqueSurface.Bottom) - .5f);
        return output;
    }
    private static (int Width, int Height, Pixel[] Pixels) Read(string path)
    {
        using var file = File.OpenRead(path);
        string Line() { var bytes = new List<byte>(); int b; while ((b = file.ReadByte()) >= 0 && b != 10) bytes.Add((byte)b); return Encoding.ASCII.GetString(bytes.ToArray()); }
        if (Line() != "P6") throw new InvalidDataException("Expected binary PPM.");
        var size = Line().Split(' '); int w = int.Parse(size[0], CultureInfo.InvariantCulture), h = int.Parse(size[1], CultureInfo.InvariantCulture);
        if (Line() != "255") throw new InvalidDataException("Expected RGB8.");
        var raw = new byte[w * h * 3]; file.ReadExactly(raw); var pixels = new Pixel[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Pixel(raw[3 * i], raw[3 * i + 1], raw[3 * i + 2]);
        return (w, h, pixels);
    }
    private static void Write(string path, Pixel[] pixels, int w, int h)
    {
        using var file = File.Create(path); file.Write(Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n"));
        for (int y = h - 1; y >= 0; y--) for (int x = 0; x < w; x++)
        { var p = pixels[y * w + x]; file.WriteByte(p.R); file.WriteByte(p.G); file.WriteByte(p.B); }
    }
}
