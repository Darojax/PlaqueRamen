using System;
using System.IO;
using System.Linq;

namespace PlaqueRamen;

// Shared contract between the offline artwork compiler and the game loader.
internal sealed class CertificatePreparedCatalog
{
    public int Format = 1;
    public string SourceCatalogSha = "", FrameSourceSha = "";
    public PreparedCertificate[] Items = Array.Empty<PreparedCertificate>();

    internal void Validate(CertificateArt[] originals, string catalogSha)
    {
        if (Format != 1 || !SourceCatalogSha.Equals(catalogSha, StringComparison.OrdinalIgnoreCase)
            || FrameSourceSha != CertificateFrameFinish.SourceSha || Items == null || Items.Any(i=>i==null) || Items.Length != originals.Length
            || Items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != Items.Length)
            throw new InvalidDataException("Prepared certificate catalogue does not match its source.");
        foreach (var art in originals)
        {
            var item = Items.SingleOrDefault(i => i.Id == art.Id);
            if (item == null) throw new InvalidDataException("Prepared certificate missing: " + art.Id);
            item.Validate(art);
        }
        var files=Items.SelectMany(i=>i.Textures).Select(t=>t.File).ToArray();
        if(files.Length!=34 || files.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=files.Length)
            throw new InvalidDataException("Prepared certificate texture identities are not unique.");
    }
}

internal sealed class PreparedCertificate
{
    public string Id = "", Guid = "", SourceSha = "";
    public int SourceFitWidth = 0, SourceFitHeight = 0;
    public float PaperAspect = 0;
    public PreparedTexture Paper = new(), Gloss = new();
    public PreparedTexture? Icon = null, Frame = null;

    internal PreparedTexture[] Textures => new[] { Paper, Gloss, Icon, Frame }.Where(t => t != null).Cast<PreparedTexture>().ToArray();
    internal void Validate(CertificateArt art)
    {
        int edge = art.IsPhotograph ? 768 : 1024;
        if (Id != art.Id || Guid != art.Guid || !SourceSha.Equals(art.Sha256, StringComparison.OrdinalIgnoreCase)
            || SourceFitWidth < 1 || SourceFitHeight < 1 || SourceFitWidth > 4096 || SourceFitHeight > 4096
            || float.IsNaN(PaperAspect) || float.IsInfinity(PaperAspect) || PaperAspect < .2f || PaperAspect > 5
            || Paper == null || Gloss == null || (art.Width == 0 ? Icon != null || Frame != null : Icon == null || Frame == null))
            throw new InvalidDataException("Prepared certificate definition changed: " + Id);
        foreach (var texture in Textures) texture.Validate();
        float scale = Math.Min(1f, edge / (float)Math.Max(SourceFitWidth, SourceFitHeight));
        int width = Math.Max(1, (int)Math.Round(SourceFitWidth * scale));
        int height = Math.Max(1, (int)Math.Round(SourceFitHeight * scale));
        if (Paper.Width != width || Paper.Height != height || Gloss.Width != 512
            || Gloss.Height != (int)Math.Round(512 / PaperAspect)
            || (Icon != null && (Icon.Width != 512 || Icon.Height != 512))
            || (Frame != null && (Frame.Width != 512 || Frame.Height != 512)))
            throw new InvalidDataException("Prepared texture dimensions changed: " + Id);
    }
}

internal sealed class PreparedTexture
{
    public string File = "", Sha256 = "";
    public int Width = 0, Height = 0;
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(File) || File != Path.GetFileName(File)
            || !File.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit)
            || Width < 1 || Height < 1 || Width > 2048 || Height > 2048)
            throw new InvalidDataException("Invalid prepared certificate texture.");
    }
}
