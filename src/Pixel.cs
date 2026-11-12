namespace PlaqueRamen;

internal readonly struct Pixel
{
    internal readonly byte R, G, B, A;
    internal Pixel(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }
}
