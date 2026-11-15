using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PlaqueRamen;
using PlaqueRamen.Tooling;

try
{
    if (args.Length != 2) throw new ArgumentException("Usage: CertificateAssetCompiler <moduleRoot> <outputFolder>");
    string root = Path.GetFullPath(args[0]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    string output = Path.GetFullPath(args[1]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    bool Within(string child, string parent) => child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    if (!Within(output, Path.Combine(root, "artifacts")) && output != Path.Combine(root, "artifacts")
        && !Within(output, Path.Combine(root, "dist")))
        throw new InvalidDataException("Prepared artwork output must be inside this module's artifacts or dist folder.");
    string catalogPath = Path.Combine(root, "assets", "Certificates.json");
    byte[] catalogBytes = File.ReadAllBytes(catalogPath);
    string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    var json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
    var art = JsonSerializer.Deserialize<CertificateArt[]>(catalogBytes, json) ?? throw new InvalidDataException("Missing certificate catalogue.");
    if (art.Length != 9 || art.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != 9
        || art.Select(a => a.Guid).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 9)
        throw new InvalidDataException("Expected nine distinct certificate designs.");
    var sourceBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    foreach (var item in art)
    {
        item.Validate();
        if (!Regex.IsMatch(item.Id, "^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Invalid certificate output identity: " + item.Id);
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, "assets", "certificates", item.File));
        if (!Hash(bytes).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Approved master artwork changed: " + item.File);
        sourceBytes.Add(item.Id, bytes);
    }
    byte[] frameBytes = File.ReadAllBytes(Path.Combine(root, "assets", "certificates", CertificateFrameFinish.SourceFile));
    if (Hash(frameBytes) != CertificateFrameFinish.SourceSha) throw new InvalidDataException("Approved frame atlas changed.");
    var frame = PngCodec.Decode(frameBytes);
    if (frame.Width != 512 || frame.Height != 512) throw new InvalidDataException("Native frame atlas dimensions changed.");
    var fixture = JsonSerializer.Deserialize<Fixture>(File.ReadAllBytes(Path.Combine(root, "tests", "certificate-fixture.json")), json)
        ?? throw new InvalidDataException("Missing certificate mesh fixture.");
    var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    var prepared = new CertificatePreparedCatalog { SourceCatalogSha = Hash(catalogBytes), FrameSourceSha = Hash(frameBytes) };
    var items = new List<PreparedCertificate>();
    long fullPaperPixels = 0, preparedPaperPixels = 0;
    PreparedTexture Texture(string name, Pixel[] pixels, int width, int height)
    {
        byte[] bytes = PngCodec.Encode(pixels, width, height);
        // Decode every derived PNG before accepting its metadata. This verifies
        // row orientation, RGBA bytes and checksums without a graphics runtime.
        var roundTrip = PngCodec.Decode(bytes);
        if (roundTrip.Width != width || roundTrip.Height != height || !SamePixels(roundTrip.Pixels, pixels))
            throw new InvalidDataException("Prepared PNG round trip changed pixels: " + name);
        files.Add(name, bytes);
        return new PreparedTexture { File = name, Sha256 = Hash(bytes), Width = width, Height = height };
    }
    foreach (var item in art)
    {
        var shape = CertificateLayout.Create(fixture.vertices, fixture.uv, fixture.indices, item);
        var source = PngCodec.Decode(sourceBytes[item.Id]);
        var fit = CertificatePaper.Fit(source.Pixels, source.Width, source.Height, item.Crop, shape.PaperAspect);
        var paper = CertificateRaster.Downsample(fit.Pixels, fit.Width, fit.Height, item.IsPhotograph ? 768 : 1024);
        int glossWidth = 512, glossHeight = (int)Math.Round(512 / shape.PaperAspect);
        int seed = item.Id.Aggregate(17, (value, c) => unchecked(value * 31 + c));
        var next = new PreparedCertificate {
            Id = item.Id, Guid = item.Guid, SourceSha = item.Sha256, SourceFitWidth = fit.Width, SourceFitHeight = fit.Height,
            PaperAspect = shape.PaperAspect,
            Paper = Texture(item.Id + ".paper.png", paper.Pixels, paper.Width, paper.Height),
            Gloss = Texture(item.Id + ".gloss.png", CertificatePaper.StockGloss(glossWidth, glossHeight, seed, item.RecentPaper), glossWidth, glossHeight)
        };
        if (item.Width != 0)
        {
            // Icons and finishes retain the exact previous composition, using
            // the full fitted master rather than the reduced runtime paper.
            next.Icon = Texture(item.Id + ".icon.png", CertificateIcon.Compose(fit.Pixels, fit.Width, fit.Height), CertificateIcon.Size, CertificateIcon.Size);
            next.Frame = Texture(item.Id + ".frame.png", CertificateFrameFinish.Compose(frame.Pixels, frame.Width, frame.Height, item, shape), frame.Width, frame.Height);
        }
        items.Add(next); fullPaperPixels += (long)fit.Width * fit.Height; preparedPaperPixels += (long)paper.Width * paper.Height;
    }
    prepared.Items = items.ToArray();
    prepared.Validate(art, Hash(catalogBytes));
    if (files.Count != 34) throw new InvalidDataException("Expected 34 prepared texture files.");
    byte[] manifest = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(prepared, json) + "\n");
    var restored = JsonSerializer.Deserialize<CertificatePreparedCatalog>(manifest, json) ?? throw new InvalidDataException("Prepared metadata could not be read.");
    restored.Validate(art, Hash(catalogBytes));
    files.Add("PreparedCertificates.json", manifest);
    if (Directory.Exists(output))
    {
        var existing = Directory.GetFiles(output, "*", SearchOption.AllDirectories);
        if (existing.Any(path => Path.GetDirectoryName(path) != output || !files.ContainsKey(Path.GetFileName(path))))
            throw new InvalidDataException("Unexpected files in prepared-artwork output; retained for inspection.");
    }
    Directory.CreateDirectory(output);
    foreach (var file in files)
    {
        string path = Path.Combine(output, file.Key);
        File.WriteAllBytes(path, file.Value);
        if (Hash(File.ReadAllBytes(path)) != Hash(file.Value)) throw new IOException("Prepared file did not verify: " + file.Key);
    }
    Console.WriteLine(JsonSerializer.Serialize(new {
        designs = items.Count, files = files.Count, sourcePaperPixels = fullPaperPixels, preparedPaperPixels,
        paperPixelReductionPercent = Math.Round((1 - preparedPaperPixels / (double)fullPaperPixels) * 100, 2),
        bytes = files.Sum(f => (long)f.Value.Length), manifestSHA256 = Hash(manifest), output
    }));
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Certificate artwork preparation failed: " + ex.Message);
    return 1;
}

static bool SamePixels(Pixel[] a, Pixel[] b)
{
    if (a.Length != b.Length) return false;
    for (int i = 0; i < a.Length; i++) if (a[i].R != b[i].R || a[i].G != b[i].G || a[i].B != b[i].B || a[i].A != b[i].A) return false;
    return true;
}

internal sealed class Fixture
{
    public float[][] vertices = Array.Empty<float[]>(), uv = Array.Empty<float[]>();
    public int[] indices = Array.Empty<int>();
}
