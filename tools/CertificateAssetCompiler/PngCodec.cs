using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using PlaqueRamen;

namespace PlaqueRamen.Tooling;

internal readonly record struct RasterImage(int Width, int Height, Pixel[] Pixels);

// Small build-only codec for the approved 8-bit RGB/RGBA, non-interlaced PNG
// masters. Channel bytes are unchanged; arrays use Unity's bottom-origin order.
internal static class PngCodec
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
    private static readonly uint[] CrcTable = MakeCrcTable();
    private const int MaximumDimension = 4096;

    internal static RasterImage Decode(byte[] bytes)
    {
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("Invalid PNG signature.");
        int offset = 8, width = 0, height = 0, channels = 0;
        bool header = false, ended = false;
        (byte R, byte G, byte B)? transparent = null;
        using var compressed = new MemoryStream();
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 12) throw new InvalidDataException("Truncated PNG chunk.");
            uint unsignedLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (unsignedLength > int.MaxValue || unsignedLength > bytes.Length - offset - 12)
                throw new InvalidDataException("PNG chunk exceeds its file.");
            int length = (int)unsignedLength;
            ReadOnlySpan<byte> type = bytes.AsSpan(offset + 4, 4);
            ReadOnlySpan<byte> data = bytes.AsSpan(offset + 8, length);
            uint expected = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + length, 4));
            if (Crc(type, data) != expected) throw new InvalidDataException("PNG chunk checksum differs.");
            string name = Encoding.ASCII.GetString(type);
            if (!header && name != "IHDR") throw new InvalidDataException("PNG header must be first.");
            switch (name)
            {
                case "IHDR":
                    if (header || length != 13) throw new InvalidDataException("Invalid PNG header.");
                    width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
                    height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
                    if (width < 1 || height < 1 || width > MaximumDimension || height > MaximumDimension
                        || data[8] != 8 || (data[9] != 2 && data[9] != 6) || data[10] != 0 || data[11] != 0 || data[12] != 0)
                        throw new InvalidDataException("Expected bounded 8-bit RGB/RGBA, non-interlaced PNG artwork.");
                    channels = data[9] == 2 ? 3 : 4; header = true;
                    break;
                case "IDAT":
                    compressed.Write(data);
                    break;
                case "tRNS":
                    if (channels != 3 || length != 6 || data[0] != 0 || data[2] != 0 || data[4] != 0)
                        throw new InvalidDataException("Invalid RGB PNG transparency key.");
                    transparent = (data[1], data[3], data[5]);
                    break;
                case "IEND":
                    if (length != 0 || offset + 12 != bytes.Length) throw new InvalidDataException("Invalid PNG ending.");
                    ended = true;
                    break;
                case "PLTE":
                    // Optional suggested palette in truecolour PNGs; pixel data
                    // remains RGB/RGBA and does not refer to this palette.
                    if (length == 0 || length > 768 || length % 3 != 0) throw new InvalidDataException("Invalid PNG palette.");
                    break;
                default:
                    if ((type[0] & 32) == 0) throw new InvalidDataException("Unsupported critical PNG chunk: " + name);
                    break;
            }
            offset += length + 12;
            if (ended) break;
        }
        if (!header || !ended || compressed.Length == 0) throw new InvalidDataException("Incomplete PNG artwork.");
        int rowBytes = checked(width * channels);
        int expectedBytes = checked((rowBytes + 1) * height);
        var raw = new byte[expectedBytes];
        compressed.Position = 0;
        try
        {
            using var decoder = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true);
            decoder.ReadExactly(raw);
            if (decoder.ReadByte() != -1) throw new InvalidDataException("PNG expands beyond the declared dimensions.");
        }
        catch (EndOfStreamException ex) { throw new InvalidDataException("PNG pixel data is truncated.", ex); }
        var pixels = new Pixel[checked(width * height)];
        var previous = new byte[rowBytes];
        var current = new byte[rowBytes];
        for (int y = 0; y < height; y++)
        {
            int start = y * (rowBytes + 1);
            int filter = raw[start];
            if (filter > 4) throw new InvalidDataException("Unsupported PNG scanline filter.");
            for (int i = 0; i < rowBytes; i++)
            {
                int left = i >= channels ? current[i - channels] : 0;
                int above = previous[i];
                int upperLeft = i >= channels ? previous[i - channels] : 0;
                int prediction = Predict(filter, left, above, upperLeft);
                current[i] = unchecked((byte)(raw[start + 1 + i] + prediction));
            }
            int target = (height - 1 - y) * width;
            for (int x = 0; x < width; x++)
            {
                int i = x * channels;
                byte alpha = channels == 4 ? current[i + 3] : (byte)255;
                if (transparent is { } key && current[i] == key.R && current[i + 1] == key.G && current[i + 2] == key.B) alpha = 0;
                pixels[target + x] = new Pixel(current[i], current[i + 1], current[i + 2], alpha);
            }
            (previous, current) = (current, previous);
        }
        return new RasterImage(width, height, pixels);
    }

    internal static byte[] Encode(Pixel[] pixels, int width, int height)
    {
        if (width < 1 || height < 1 || width > MaximumDimension || height > MaximumDimension || pixels.Length != width * height)
            throw new InvalidDataException("Invalid PNG output dimensions.");
        int rowBytes = width * 4;
        var previous = new byte[rowBytes];
        var current = new byte[rowBytes];
        var candidate = new byte[rowBytes];
        var best = new byte[rowBytes];
        using var compressed = new MemoryStream();
        using (var encoder = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                int source = (height - 1 - y) * width;
                for (int x = 0; x < width; x++)
                {
                    var p = pixels[source + x]; int i = x * 4;
                    current[i] = p.R; current[i + 1] = p.G; current[i + 2] = p.B; current[i + 3] = p.A;
                }
                long bestScore = long.MaxValue; int bestFilter = 0;
                for (int filter = 0; filter <= 4; filter++)
                {
                    long score = 0;
                    for (int i = 0; i < rowBytes; i++)
                    {
                        int left = i >= 4 ? current[i - 4] : 0, above = previous[i], upperLeft = i >= 4 ? previous[i - 4] : 0;
                        byte value = unchecked((byte)(current[i] - Predict(filter, left, above, upperLeft)));
                        candidate[i] = value; score += Math.Abs((int)unchecked((sbyte)value));
                    }
                    if (score >= bestScore) continue;
                    bestScore = score; bestFilter = filter; candidate.CopyTo(best, 0);
                }
                encoder.WriteByte((byte)bestFilter); encoder.Write(best);
                (previous, current) = (current, previous);
            }
        }
        using var output = new MemoryStream();
        output.Write(Signature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header[..4], width); BinaryPrimitives.WriteInt32BigEndian(header.Slice(4, 4), height);
        header[8] = 8; header[9] = 6; header[10] = 0; header[11] = 0; header[12] = 0;
        WriteChunk(output, "IHDR", header); WriteChunk(output, "IDAT", compressed.ToArray()); WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static int Predict(int filter, int left, int above, int upperLeft) => filter switch
    {
        0 => 0, 1 => left, 2 => above, 3 => (left + above) / 2, 4 => Paeth(left, above, upperLeft),
        _ => throw new InvalidDataException("Invalid PNG filter.")
    };
    private static int Paeth(int left, int above, int upperLeft)
    {
        int p = left + above - upperLeft, a = Math.Abs(p - left), b = Math.Abs(p - above), c = Math.Abs(p - upperLeft);
        return a <= b && a <= c ? left : b <= c ? above : upperLeft;
    }
    private static void WriteChunk(Stream output, string name, ReadOnlySpan<byte> data)
    {
        byte[] type = Encoding.ASCII.GetBytes(name);
        Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length); output.Write(type); output.Write(data);
        Span<byte> crc = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(type, data)); output.Write(crc);
    }
    private static uint Crc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in type) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        foreach (byte value in data) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return crc ^ uint.MaxValue;
    }
    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320u ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }
}
