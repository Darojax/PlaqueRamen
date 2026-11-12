using System;
using System.IO;

namespace PlaqueRamen;

internal readonly struct FaceLayout
{
    // These are the visible front-face UV limits, in original atlas pixels.
    // The larger 924..1808 rectangle also contains the top and bottom edges.
    internal const float FaceBottom = 955.36f, FaceTop = 1774.56f;
    internal const float FaceLeft = 1211.06f, FaceRight = 1397.67f;
    internal const float BottomMargin = .085f, TopMargin = .105f, LetterWidthGain = 1.12f;
    internal readonly float SourceLow, SourceHigh;

    internal FaceLayout(int height, int inkTop, int inkBottom)
    {
        if (height < 2 || inkTop < 1 || inkBottom <= inkTop || inkBottom >= height - 1)
            throw new InvalidDataException("Approved lettering bounds are missing or invalid.");
        SourceLow = 1f - inkBottom / (height - 1f);
        SourceHigh = 1f - inkTop / (height - 1f);
    }

    // Move the existing lettering into the safe area while retaining the whole
    // grain. Clear wood above/below is stretched smoothly, never filled flat or
    // replaced with generic wood. Input/output are bottom-origin coordinates.
    internal float SourceV(float v)
    {
        v = Math.Max(0f, Math.Min(1f, v));
        float high = 1f - TopMargin;
        if (v < BottomMargin) return v * SourceLow / BottomMargin;
        if (v <= high) return SourceLow + (v - BottomMargin) * (SourceHigh - SourceLow) / (high - BottomMargin);
        return SourceHigh + (v - high) * (1f - SourceHigh) / TopMargin;
    }
}
