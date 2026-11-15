using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PlaqueRamen;
using PlaqueRamen.Tooling;

internal static class PreparedCertificateChecks
{
    private sealed class Fixture
    {
        public float[][] vertices = Array.Empty<float[]>(), uv = Array.Empty<float[]>();
        public int[] indices = Array.Empty<int>();
    }

    internal static void Run(string root, Action<bool, string> check, Action<Action, string> refuses)
    {
        PngChecks(check, refuses);
        var json = new JsonSerializerOptions { IncludeFields = true };
        string catalogPath = Path.Combine(root, "assets/Certificates.json");
        byte[] catalogBytes = File.ReadAllBytes(catalogPath);
        var source = JsonSerializer.Deserialize<CertificateArt[]>(File.ReadAllText(catalogPath), json)!;
        string preparedRoot = Path.Combine(root, "artifacts/prepared-certificates");
        var prepared = JsonSerializer.Deserialize<CertificatePreparedCatalog>(File.ReadAllText(Path.Combine(preparedRoot, "PreparedCertificates.json")), json)!;
        string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
        prepared.Validate(source, Hash(catalogBytes));
        check(prepared.Format == 1 && prepared.Items.Length == 9 && prepared.SourceCatalogSha == Hash(catalogBytes),
            "prepared artwork matches all nine source designs and the exact source catalogue");
        var expectedFiles = prepared.Items.SelectMany(i => i.Textures).Select(t => t.File).Append("PreparedCertificates.json").Order().ToArray();
        var actualFiles = Directory.GetFiles(preparedRoot, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(preparedRoot, p)).Order().ToArray();
        check(expectedFiles.Length == 35 && expectedFiles.Distinct().Count() == 35 && actualFiles.SequenceEqual(expectedFiles),
            "prepared runtime artwork contains exactly 34 unique PNGs and one manifest, with no masters or private files");
        var fixture = JsonSerializer.Deserialize<Fixture>(File.ReadAllText(Path.Combine(root, "tests/certificate-fixture.json")), json)!;
        var frame = PngCodec.Decode(File.ReadAllBytes(Path.Combine(root, "assets/certificates", CertificateFrameFinish.SourceFile)));
        long sourcePixels = 0, paperPixels = 0;
        foreach (var art in source)
        {
            var item = prepared.Items.Single(p => p.Id == art.Id);
            var shape = CertificateLayout.Create(fixture.vertices, fixture.uv, fixture.indices, art);
            var original = PngCodec.Decode(File.ReadAllBytes(Path.Combine(root, "assets/certificates", art.File)));
            var fit = CertificatePaper.Fit(original.Pixels, original.Width, original.Height, art.Crop, shape.PaperAspect);
            check(item.Guid == art.Guid && item.SourceSha == art.Sha256 && item.SourceFitWidth == fit.Width && item.SourceFitHeight == fit.Height
                && item.PaperAspect == shape.PaperAspect,
                "prepared paper keeps source identity, full original crop dimensions and native geometry aspect: " + art.Id);
            var images = new Dictionary<string, RasterImage>();
            foreach (var texture in item.Textures)
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(preparedRoot, texture.File));
                var image = PngCodec.Decode(bytes); images.Add(texture.File, image);
                check(Hash(bytes) == texture.Sha256 && image.Width == texture.Width && image.Height == texture.Height,
                    "prepared PNG hash and actual dimensions agree with its manifest: " + texture.File);
            }
            var paper = images[item.Paper.File];
            var expectedPaper = CertificateRaster.Downsample(fit.Pixels, fit.Width, fit.Height, art.IsPhotograph ? 768 : 1024);
            check(paper.Width == expectedPaper.Width && paper.Height == expectedPaper.Height && Same(paper.Pixels, expectedPaper.Pixels),
                "packaged reduced paper is the approved full-fit resampling without a changed crop or regenerated text: " + art.Id);
            int glossWidth = 512, glossHeight = (int)Math.Round(512 / shape.PaperAspect);
            int seed = art.Id.Aggregate(17, (value, c) => unchecked(value * 31 + c));
            var gloss = images[item.Gloss.File];
            check(gloss.Width == glossWidth && gloss.Height == glossHeight
                && Same(gloss.Pixels, CertificatePaper.StockGloss(glossWidth, glossHeight, seed, art.RecentPaper)),
                "prepared paper gloss preserves every previous fibre pixel: " + art.Id);
            if (art.Width != 0)
            {
                check(item.Icon != null && Same(images[item.Icon.File].Pixels, CertificateIcon.Compose(fit.Pixels, fit.Width, fit.Height)),
                    "prepared inventory icon is byte-identical to the original full-master composition: " + art.Id);
                check(item.Frame != null && Same(images[item.Frame.File].Pixels, CertificateFrameFinish.Compose(frame.Pixels, frame.Width, frame.Height, art, shape)),
                    "prepared frame preserves every original grain, patina and gilt pixel: " + art.Id);
            }
            else check(item.Icon == null && item.Frame == null, "native appreciation frame and inventory icon retain their original game assets");
            sourcePixels += (long)fit.Width * fit.Height; paperPixels += (long)paper.Width * paper.Height;
        }
        check(paperPixels < sourcePixels * .5 && paperPixels > 0, "prepared framed papers use less than half the previous pixel count");
        string expectedSha = prepared.SourceCatalogSha;
        prepared.SourceCatalogSha = new string('0', 64);
        refuses(() => prepared.Validate(source, Hash(catalogBytes)), "prepared textures from a different source catalogue are refused");
        prepared.SourceCatalogSha = expectedSha;
        string expectedGuid = prepared.Items[0].Guid;
        prepared.Items[0].Guid = source[1].Guid;
        refuses(() => prepared.Validate(source, Hash(catalogBytes)), "prepared image identities cannot be swapped between saved decorations");
        prepared.Items[0].Guid = expectedGuid;
        string expectedFile = prepared.Items[0].Paper.File;
        prepared.Items[0].Paper.File = "../outside.png";
        refuses(() => prepared.Validate(source, Hash(catalogBytes)), "prepared image filenames cannot escape their artwork folder");
        prepared.Items[0].Paper.File = expectedFile;
    }

    private static void PngChecks(Action<bool, string> check, Action<Action, string> refuses)
    {
        // Explicit, independently specified 2x2 scanlines exercise all standard
        // PNG filters. PNG rows are red/green then blue/white; Unity is reversed.
        byte[][] filteredRows = {
            new byte[] { 0,255,0,0,255,0,255,0,128, 0,0,0,255,255,255,255,255,255 },
            new byte[] { 1,255,0,0,255,1,255,0,129, 1,0,0,255,255,255,255,0,0 },
            new byte[] { 2,255,0,0,255,0,255,0,128, 2,1,0,255,0,255,0,255,127 },
            new byte[] { 3,255,0,0,255,129,255,0,1, 3,129,0,255,128,255,128,128,64 },
            new byte[] { 4,255,0,0,255,1,255,0,129, 4,1,0,255,0,255,0,0,127 }
        };
        var expected = new[] { new Pixel(0,0,255,255), new Pixel(255,255,255,255), new Pixel(255,0,0,255), new Pixel(0,255,0,128) };
        for (int filter = 0; filter < filteredRows.Length; filter++)
        {
            var image = PngCodec.Decode(FixturePng(filteredRows[filter], 6));
            check(image.Width == 2 && image.Height == 2 && Same(image.Pixels, expected),
                "PNG filter " + filter + " preserves channel values, alpha and Unity bottom-origin rows");
        }
        var rgb = PngCodec.Decode(FixturePng(new byte[] { 0,255,0,0,0,255,0, 0,0,0,255,255,255,255 }, 2));
        check(Same(rgb.Pixels, new[] { expected[0], expected[1], expected[2], new Pixel(0,255,0,255) }),
            "RGB master PNGs decode without introducing transparency or changing row order");
        var transparent = PngCodec.Decode(FixturePng(new byte[] { 0,255,0,0,0,255,0, 0,0,0,255,255,255,255 }, 2, transparency: new byte[] {0,255,0,0,0,0}));
        check(transparent.Pixels[2].A == 0 && transparent.Pixels.Where((_, i) => i != 2).All(p => p.A == 255),
            "an RGB transparency key affects only its matching colour");
        byte[] encoded = PngCodec.Encode(expected, 2, 2);
        check(Same(PngCodec.Decode(encoded).Pixels, expected) && encoded.SequenceEqual(PngCodec.Encode(expected, 2, 2)),
            "build PNG output preserves all RGBA bytes and is deterministic");
        byte[] corrupt = (byte[])encoded.Clone(); corrupt[^1] ^= 1;
        refuses(() => PngCodec.Decode(corrupt), "PNG checksum corruption is refused");
        refuses(() => PngCodec.Decode(encoded[..^2]), "truncated PNG chunks are refused");
        refuses(() => PngCodec.Decode(FixturePng(filteredRows[0], 6, bitDepth: 16)), "unreviewed higher-bit-depth artwork is refused");
        refuses(() => PngCodec.Decode(FixturePng(filteredRows[0], 6, interlace: 1)), "unreviewed interlaced artwork is refused");
        refuses(() => PngCodec.Decode(FixturePng(new byte[] { 0 }, 6)), "truncated decompressed PNG rows are refused");
        refuses(() => PngCodec.Decode(FixturePng(filteredRows[0].Append((byte)0).ToArray(), 6)), "extra decompressed PNG rows are refused");
        refuses(() => PngCodec.Encode(new Pixel[3], 2, 2), "mismatched PNG output pixel counts are refused");
    }

    private static byte[] FixturePng(byte[] rows, byte colourType, byte bitDepth = 8, byte interlace = 0, byte[]? transparency = null)
    {
        using var output = new MemoryStream(); output.Write(new byte[] {137,80,78,71,13,10,26,10});
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0,4), 2); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4,4), 2);
        header[8] = bitDepth; header[9] = colourType; header[12] = interlace;
        Chunk("IHDR", header);
        if (transparency != null) Chunk("tRNS", transparency);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(rows);
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", Array.Empty<byte>()); return output.ToArray();
        void Chunk(string name, byte[] data)
        {
            var type = Encoding.ASCII.GetBytes(name); Span<byte> word = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
            output.Write(word); output.Write(type); output.Write(data);
            // Independent bitwise CRC implementation for the fixed fixtures.
            uint crc = uint.MaxValue;
            foreach (byte value in type.Concat(data))
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
            }
            BinaryPrimitives.WriteUInt32BigEndian(word, crc ^ uint.MaxValue); output.Write(word);
        }
    }
    private static bool Same(Pixel[] a, Pixel[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i].R != b[i].R || a[i].G != b[i].G || a[i].B != b[i].B || a[i].A != b[i].A) return false;
        return true;
    }
}
