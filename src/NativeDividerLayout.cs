using System;

namespace PlaqueRamen;

internal static class NativeDividerLayout
{
    // Native StyleSheet_Stores001 / .InfoTextStoreDivider: height 1px,
    // max-height 1% of the auto-sized description group, no flex shrinking.
    internal const float Height=1,MaximumFraction=.01f;
    internal static float Cap(float nativeTextHeight,float otherHeight,int count)
    {
        if(float.IsNaN(nativeTextHeight)||float.IsInfinity(nativeTextHeight)||nativeTextHeight<0
            ||float.IsNaN(otherHeight)||float.IsInfinity(otherHeight)||count<1||count>4)return float.NaN;
        float body=nativeTextHeight+Math.Max(0,otherHeight);
        // H = text + other + count * min(1px, .01 * H).
        return Math.Min(Height,body*MaximumFraction/(1-count*MaximumFraction));
    }
}
