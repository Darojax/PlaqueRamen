using System;
using System.IO;

namespace PlaqueRamen;

// Native decoration icons are 512-square sprites. Fit the full paper into
// that canvas rather than giving the inventory a tall/wide sprite to crop.
internal static class CertificateIcon
{
    internal const int Size=512,Margin=16;
    internal static Pixel[] Compose(Pixel[] source,int width,int height)
    {
        if(width<1 || height<1 || width>4096 || height>4096 || source.Length!=width*height)
            throw new InvalidDataException("Invalid certificate icon source.");
        float scale=(Size-2*Margin)/(float)Math.Max(width,height);
        int w=Math.Max(1,(int)Math.Round(width*scale)),h=Math.Max(1,(int)Math.Round(height*scale));
        int left=(Size-w)/2,bottom=(Size-h)/2;
        var icon=new Pixel[Size*Size];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            float sx=w==1?(width-1)*.5f:x*(width-1f)/(w-1),sy=h==1?(height-1)*.5f:y*(height-1f)/(h-1);
            int x0=(int)Math.Floor(sx),y0=(int)Math.Floor(sy),x1=Math.Min(width-1,x0+1),y1=Math.Min(height-1,y0+1);
            float a=sx-x0,b=sy-y0;
            var p=source[y0*width+x0];var q=source[y0*width+x1];var r=source[y1*width+x0];var s=source[y1*width+x1];
            byte Blend(byte c,byte d,byte e,byte f)=>(byte)Math.Round((c+(d-c)*a)*(1-b)+(e+(f-e)*a)*b);
            icon[(bottom+y)*Size+left+x]=new Pixel(Blend(p.R,q.R,r.R,s.R),Blend(p.G,q.G,r.G,s.G),Blend(p.B,q.B,r.B,s.B),Blend(p.A,q.A,r.A,s.A));
        }
        return icon;
    }
}
