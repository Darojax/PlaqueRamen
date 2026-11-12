using PlaqueRamen;


internal static class PaperPriceChecks
{
    private static bool Same(Pixel a, Pixel b) => a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;
    private static bool Inside(int i) => i % 1024 >= PaperPricePatch.X && i % 1024 < PaperPricePatch.X + PaperPricePatch.Width
        && i / 1024 >= PaperPricePatch.Y && i / 1024 < PaperPricePatch.Y + PaperPricePatch.Height;
    private static Pixel[] Crop(Pixel[] source) => Enumerable.Range(0, PaperPricePatch.Height)
        .SelectMany(y => Enumerable.Range(0, PaperPricePatch.Width).Select(x => source[(y + PaperPricePatch.Y) * 1024 + x + PaperPricePatch.X])).ToArray();

    internal static void Run(Action<bool, string> check, Action<Action, string> refuses)
    {
        var native = Enumerable.Range(0, 1024 * 1024).Select(i => new Pixel((byte)(i % 251), (byte)(i % 197), (byte)(i % 173), 207)).ToArray();
        for (int y = 0; y < 80; y++) for (int x = 0; x < 104; x++)
            native[(PaperPricePatch.Y + y) * 1024 + PaperPricePatch.X + x] = x > 10 && x < 40 && y > 10 && y < 65
                ? new Pixel(50, 45, 40) : new Pixel(170, 160, 150);
        var expected = Crop(native);
        var replacement = Enumerable.Range(0, 104 * 80).Select(i => i % 104 > 60 && i % 104 < 90 && i / 104 > 10 && i / 104 < 65
            ? new Pixel(70, 60, 50) : new Pixel(190, 180, 170)).ToArray();
        var prior = (Pixel[])native.Clone(); var priorArt = (Pixel[])replacement.Clone();
        var result = PaperPricePatch.Compose(native, expected, replacement, 104, 80);
        check(Enumerable.Range(0, native.Length).Where(i => !Inside(i)).All(i => Same(native[i], result[i])),
            "all native atlas pixels outside the first two price digits stay exact, including final zero, yen, Wi-Fi and sign lettering");
        check(native.Zip(prior).All(p => Same(p.First, p.Second)) && replacement.Zip(priorArt).All(p => Same(p.First, p.Second)), "paper composition never mutates native/source artwork");
        check(result.Select((p, i) => (p, i)).All(t => t.p.A == native[t.i].A), "native atlas alpha is preserved");
        check(Same(result[(PaperPricePatch.Y + 30) * 1024 + PaperPricePatch.X + 25], new Pixel(170, 160, 150)), "old digit ink is erased with matching paper");
        check(Same(result[(PaperPricePatch.Y + 30) * 1024 + PaperPricePatch.X + 75], new Pixel(50, 45, 40)), "new digit ink matches the original palette");
        check(Enumerable.Range(0, expected.Length).Where(i => i % 104 == 0 || i % 104 == 103 || i / 104 == 0 || i / 104 == 79)
            .All(i => Same(result[(i / 104 + PaperPricePatch.Y) * 1024 + i % 104 + PaperPricePatch.X], expected[i])), "patch boundary remains native without a rectangular seam");
        var future = (Pixel[])native.Clone(); future[0] = new Pixel(12, 34, 56);
        check(Same(PaperPricePatch.Compose(future, expected, replacement, 104, 80)[0], future[0]), "future game changes elsewhere in the atlas are retained");
        var near = (Pixel[])native.Clone(); near[PaperPricePatch.Y * 1024 + PaperPricePatch.X] = new Pixel(172, 162, 152);
        check(PaperPricePatch.MatchError(near, expected) < .01, "small GPU/decompression round-trip differences are accepted");
        near[PaperPricePatch.Y * 1024 + PaperPricePatch.X] = new Pixel(100, 200, 100);
        refuses(() => PaperPricePatch.Compose(near, expected, replacement, 104, 80), "changed native price artwork is not overwritten");
        refuses(() => PaperPricePatch.Compose(native.Take(100).ToArray(), expected, replacement, 104, 80), "changed native atlas dimensions refused");
        refuses(() => PaperPricePatch.Compose(native, expected.Take(100).ToArray(), replacement, 104, 80), "changed reference crop dimensions refused");
        refuses(() => PaperPricePatch.Compose(native, expected, replacement, 80, 1040), "replacement buffer dimensions must agree");
        refuses(() => PaperPricePatch.Compose(native, expected, Enumerable.Repeat(new Pixel(180, 170, 160), 100).ToArray(), 10, 10), "blank or unreadable replacement art refused");
        check(PaperPricePatch.Compose(native, expected, replacement, 80, 104).Length == native.Length, "replacement resolution is independent of fixed atlas coordinates");
    }

}
