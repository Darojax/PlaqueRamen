using System;
using System.IO;
using System.Linq;

namespace PlaqueRamen;

internal static class CertificateFrameFinish
{
    internal const string SourceFile="frame-wood.original.png";
    internal const string SourceSha="38D21F4A7927844CACAC7B30F9098EF50A616FDD8E6AC572012DD3A5149DCA9B";
    internal static readonly string[] Styles={"native","oak","mahogany","walnut","metal"};

    // Preserve the native atlas's directional grain, scratches and UV layout.
    // This runs once per cached surface, never during normal frame rendering.
    internal static Pixel[] Compose(Pixel[] source,int width,int height,CertificateArt art,CertificateShape shape)
    {
        if(width!=512 || height!=512 || source.Length!=width*height || !Styles.Contains(art.FrameStyle))
            throw new InvalidDataException("Certificate frame atlas or finish changed.");
        float[] tone=art.FrameStyle switch {
            "oak"=>new[]{2.35f,2.12f,1.7f}, "mahogany"=>new[]{1.65f,.94f,.82f},
            "walnut"=>new[]{1.15f,1.05f,.9f}, _=>new[]{1f,1f,1f}};
        var output=new Pixel[source.Length];
        byte C(float n)=>(byte)Math.Max(0,Math.Min(255,(int)Math.Round(n)));
        for(int i=0;i<source.Length;i++)
        {
            var p=source[i];
            output[i]=art.FrameStyle=="metal"?new Pixel(44,49,54,p.A):new Pixel(C(p.R*tone[0]),C(p.G*tone[1]),C(p.B*tone[2]),p.A);
        }
        if(!art.Gilt)return output;
        var paper=shape.Paper.Distinct().ToArray();
        float left=paper.Min(i=>shape.Vertices[i][0]),right=paper.Max(i=>shape.Vertices[i][0]);
        float bottom=paper.Min(i=>shape.Vertices[i][1]),top=paper.Max(i=>shape.Vertices[i][1]);
        var uv=shape.Uv;var vs=shape.Vertices;
        for(int t=0;t<shape.Frame.Length;t+=3)
        {
            int a=shape.Frame[t],b=shape.Frame[t+1],c=shape.Frame[t+2];
            float ax=uv[a][0]*width,ay=uv[a][1]*height,bx=uv[b][0]*width,by=uv[b][1]*height,cx=uv[c][0]*width,cy=uv[c][1]*height;
            float area=(by-cy)*(ax-cx)+(cx-bx)*(ay-cy);if(Math.Abs(area)<.00001f)continue;
            int x0=Math.Max(0,(int)Math.Floor(Math.Min(ax,Math.Min(bx,cx)))),x1=Math.Min(width-1,(int)Math.Ceiling(Math.Max(ax,Math.Max(bx,cx))));
            int y0=Math.Max(0,(int)Math.Floor(Math.Min(ay,Math.Min(by,cy)))),y1=Math.Min(height-1,(int)Math.Ceiling(Math.Max(ay,Math.Max(by,cy))));
            for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
            {
                float wa=((by-cy)*(x+.5f-cx)+(cx-bx)*(y+.5f-cy))/area,wb=((cy-ay)*(x+.5f-cx)+(ax-cx)*(y+.5f-cy))/area,wc=1-wa-wb;
                if(wa<0||wb<0||wc<0)continue;
                float px=vs[a][0]*wa+vs[b][0]*wb+vs[c][0]*wc,py=vs[a][1]*wa+vs[b][1]*wb+vs[c][1]*wc;
                float dx=Math.Max(left-px,Math.Max(px-right,0)),dy=Math.Max(bottom-py,Math.Max(py-top,0));
                float distance=(float)Math.Sqrt(dx*dx+dy*dy);
                if(distance<=0 || distance>.0022f)continue;
                var p=source[y*width+x];float grain=.75f+(.2126f*p.R+.7152f*p.G+.0722f*p.B)/510;
                output[y*width+x]=new Pixel(C(155*grain),C(121*grain),C(66*grain),p.A);
            }
        }
        return output;
    }
}
