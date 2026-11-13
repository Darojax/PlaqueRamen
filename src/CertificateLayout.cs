using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlaqueRamen;

internal sealed class CertificateArt
{
    public string Id = "", Guid = "", Name = "", Description = "", File = "", Sha256 = "";
    public int[] Crop = Array.Empty<int>(); // left/top/width/height in source pixels
    public float Width = 0, Height = 0; // zero retains the native geometry exactly
    public int Price = -1; // -1 inherits the native certificate price; zero is free
    public bool RecentPaper = false;
    public string Mount = "Native", FrameStyle = "native";
    public bool DisplayLedge = false;
    internal bool FlushWall => Mount == "Wall" && !DisplayLedge;
    internal bool IsPhotograph => Guid == CertificateLayout.PortraitGuid || Guid == CertificateLayout.FamilyPortraitGuid;
    public float BorderScale = 1;
    public bool Gilt = false;
    public float[] FrameTint = { 1, 1, 1 };

    internal void Validate()
    {
        if (!System.Guid.TryParse(Guid, out _) || string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name)
            || File != Path.GetFileName(File) || !File.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit)
            || Crop.Length != 4 || Crop.Any(n => n < 0) || Crop[2] < 1 || Crop[3] < 1
            || !Finite(Width) || !Finite(Height) || !((Width == 0 && Height == 0) || (Width >= .15f && Width <= 1 && Height >= .15f && Height <= 1))
            || Price < -1 || !Finite(BorderScale) || BorderScale < .2f || BorderScale > 1.5f
            || (Mount != "Native" && Mount != "Wall" && Mount != "Table")
            || (DisplayLedge && (Mount != "Wall" || Width == 0))
            || !CertificateFrameFinish.Styles.Contains(FrameStyle)
            || FrameTint.Length != 3 || FrameTint.Any(v => !Finite(v) || v < .1f || v > 1.5f))
            throw new InvalidDataException("Invalid certificate definition: " + Id);
    }
    internal int ResolvePrice(int nativePrice) => Price == -1 ? nativePrice : Price;
    private static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
}

internal sealed class CertificateShape
{
    internal readonly float[][] Vertices, Uv;
    internal readonly int[] Frame, Paper, Overlay;
    internal readonly float PaperAspect;
    internal CertificateShape(float[][] vertices, float[][] uv, int[] frame, int[] paper, int[] overlay, float aspect)
    { Vertices=vertices; Uv=uv; Frame=frame; Paper=paper; Overlay=overlay; PaperAspect=aspect; }
    internal int[][] Passes(int nativeMaterialCount)
    {
        if(nativeMaterialCount is <1 or >2)throw new InvalidDataException("Certificate material layout changed.");
        return nativeMaterialCount==1?new[]{Frame,Paper}:new[]{Frame,Paper,Overlay};
    }
}

internal static class CertificateLayout
{
    internal const string NativeGuid = "eee9b24c-64f7-4e90-acfd-48a4749b7bd5";
    internal const string HygieneGuid = "272c3884-f60e-4915-9452-ef243c59b311";
    internal const string CommunityGuid = "9b510410-6122-4a9d-814f-39c54551f7b2";
    internal const string Permit9Guid = "f9876eec-2dfc-467b-bf9d-76bfbebc8f20";
    internal const string Permit68Guid = "37392ce4-88a0-4474-be10-f8a94e67f1e8";
    internal const string CulinaryGuid = "03209f80-063f-47ba-9dd7-a7b698c58a7a";
    internal const string TrainingGuid = "786f9b62-7eb8-49c6-be1c-2cc7b04617be";
    internal const string PortraitGuid = "77fe5187-a2ba-44bd-acbc-101d133eff16";
    internal const string FamilyPortraitGuid = "da7f7202-2179-4ff4-a82d-a6470e79e6a8";

    internal static CertificateShape Create(float[][] vertices,float[][] uv,int[] indices,CertificateArt art)
    {
        var shape=Create(vertices,uv,indices,art.Width,art.Height,art.BorderScale,art.FlushWall);
        // A taller frame on the native wall ledge must retain its original
        // depth contacts instead of leaning farther backwards into the wall.
        if(art.DisplayLedge)
        {
            for(int i=0;i<shape.Vertices.Length;i++)shape.Vertices[i][2]=vertices[i][2];
            var paper=shape.Paper.Distinct().ToArray();
            float width=paper.Max(i=>shape.Vertices[i][0])-paper.Min(i=>shape.Vertices[i][0]);
            float height=paper.Max(i=>shape.Vertices[i][1])-paper.Min(i=>shape.Vertices[i][1]);
            float depth=paper.Max(i=>shape.Vertices[i][2])-paper.Min(i=>shape.Vertices[i][2]);
            return new CertificateShape(shape.Vertices,shape.Uv,shape.Frame,shape.Paper,shape.Overlay,width/(float)Math.Sqrt(height*height+depth*depth));
        }
        return shape;
    }

    internal static CertificateShape Create(float[][] vertices, float[][] uv, int[] indices, float width, float height,float borderScale=1,bool wall=false)
    {
        if (vertices.Length != 72 || uv.Length != 72 || indices.Length != 108
            || vertices.Any(v => v.Length < 3 || v.Take(3).Any(n => float.IsNaN(n) || float.IsInfinity(n)))
            || uv.Any(v => v.Length < 2 || v.Take(2).Any(n => float.IsNaN(n) || float.IsInfinity(n)))
            || indices.Any(i => i < 0 || i >= vertices.Length)) throw new InvalidDataException("Certificate mesh contract changed.");
        var paper=new List<int>(); var frame=new List<int>();
        for(int t=0;t<indices.Length;t+=3)
        {
            var triangle=indices.Skip(t).Take(3).ToArray();
            (triangle.All(i=>uv[i][0]<.32f && uv[i][1]>.76f)?paper:frame).AddRange(triangle);
        }
        var ids=paper.Distinct().ToArray();
        if(paper.Count!=6 || ids.Length!=4 || frame.Intersect(ids).Any()) throw new InvalidDataException("Certificate paper face changed.");
        float minX=vertices.Min(v=>v[0]),maxX=vertices.Max(v=>v[0]),minY=vertices.Min(v=>v[1]),maxY=vertices.Max(v=>v[1]);
        float x0=ids.Min(i=>vertices[i][0]),x1=ids.Max(i=>vertices[i][0]),y0=ids.Min(i=>vertices[i][1]),y1=ids.Max(i=>vertices[i][1]);
        float u0=ids.Min(i=>uv[i][0]),u1=ids.Max(i=>uv[i][0]),v0=ids.Min(i=>uv[i][1]),v1=ids.Max(i=>uv[i][1]);
        if(Math.Abs((maxX-minX)-.3457062f)>.002f || Math.Abs((maxY-minY)-.2653057f)>.002f
            || Math.Abs((x1-x0)-.293974f)>.002f || Math.Abs((y1-y0)-.2128551f)>.002f || u1-u0<.2f || v1-v0<.15f)
            throw new InvalidDataException("Certificate frame dimensions or paper UVs changed.");
        var copy=vertices.Select(v=>v.Take(3).ToArray()).ToArray();
        var mapped=uv.Select(v=>v.Take(2).ToArray()).ToArray();
        bool resize=width!=0 || height!=0;
        if(resize && (!(width>=.15f && width<=1) || !(height>=.15f && height<=1))
            || float.IsNaN(borderScale) || borderScale<.2f || borderScale>1.5f || (!resize && (wall || borderScale!=1))) throw new InvalidDataException("Invalid certificate size or frame profile.");
        float z0=ids.Where(i=>Math.Abs(vertices[i][1]-y0)<.0001f).Average(i=>vertices[i][2]);
        float z1=ids.Where(i=>Math.Abs(vertices[i][1]-y1)<.0001f).Average(i=>vertices[i][2]);
        float slope=(z1-z0)/(y1-y0);
        if(resize)
        {
            float left=(minX+maxX-width)/2, right=left+width;
            for(int i=0;i<copy.Length;i++)
            {
                copy[i][0]=Warp(vertices[i][0],minX,maxX,x0,x1,left,right,borderScale);
                copy[i][1]=Warp(vertices[i][1],minY,maxY,y0,y1,minY,minY+height,borderScale);
                copy[i][2]=vertices[i][2]+(copy[i][1]-vertices[i][1])*slope;
            }
        }
        if(wall)
        {
            // Native wall signs face -Z. Remove the display lean and put the
            // back just in front of the wall. Keep the existing base pivot so
            // saved placements are not shifted by a whole frame height.
            foreach(var v in copy)v[2]-=(v[1]-minY)*slope;
            float back=copy.Max(v=>v[2]);foreach(var v in copy)v[2]-=back+.001f;
        }
        foreach(int i in ids) mapped[i]=new[]{(uv[i][0]-u0)/(u1-u0),(uv[i][1]-v0)/(v1-v0)};
        float paperWidth=ids.Max(i=>copy[i][0])-ids.Min(i=>copy[i][0]);
        float paperHeight=(ids.Max(i=>copy[i][1])-ids.Min(i=>copy[i][1]))*(wall?1:(float)Math.Sqrt(1+slope*slope));
        return new CertificateShape(copy,mapped,frame.ToArray(),paper.ToArray(),indices.ToArray(),paperWidth/paperHeight);
    }

    // Stretch the centre while translating the moulded border intact. The
    // frame's base and thickness are retained; no whole-object scale is saved.
    private static float Warp(float p,float low,float high,float innerLow,float innerHigh,float targetLow,float targetHigh,float borderScale)
    {
        if(p<=innerLow)return targetLow+(p-low)*borderScale;
        if(p>=innerHigh)return targetHigh-(high-p)*borderScale;
        float a=targetLow+(innerLow-low)*borderScale,b=targetHigh-(high-innerHigh)*borderScale;
        return a+(p-innerLow)/(innerHigh-innerLow)*(b-a);
    }
}

internal static class CertificatePaper
{
    // Input/output use Unity's bottom-origin pixel order. Copy the supplied
    // paper crop exactly, adding a narrow matching-paper margin when needed to
    // preserve its aspect ratio. Never stretch glyphs or include the photo frame.
    internal static (int Width,int Height,Pixel[] Pixels) Fit(Pixel[] source,int width,int height,int[] crop,float aspect)
    {
        if(width<1 || height<1 || source.Length!=width*height || crop.Length!=4 || crop.Any(n=>n<0)
            || crop[2]<1 || crop[3]<1 || (long)crop[0]+crop[2]>width || (long)crop[1]+crop[3]>height
            || float.IsNaN(aspect) || float.IsInfinity(aspect) || aspect<.2f || aspect>5)
            throw new InvalidDataException("Certificate paper crop or aspect changed.");
        int cw=crop[2],ch=crop[3],w=cw,h=ch;
        if(cw/(float)ch>aspect)h=(int)Math.Ceiling(cw/aspect);else w=(int)Math.Ceiling(ch*aspect);
        if(w>4096 || h>4096)throw new InvalidDataException("Certificate paper exceeds the texture budget.");
        // The photograph's very first edge pixel may contain its pink backing
        // or a drop shadow. Sample inside the unprinted paper corner instead.
        int inset=Math.Min(12,Math.Min(cw,ch)/10);
        var background=source[(height-1-crop[1]-inset)*width+crop[0]+inset];
        var pixels=new Pixel[w*h];int dx=(w-cw)/2,dy=(h-ch)/2;
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            if(x>=dx && x<dx+cw && y>=dy && y<dy+ch)continue;
            float fibre=Grain(x,y,731)*1.8f;
            byte Channel(byte value)=>(byte)Math.Max(0,Math.Min(255,(int)Math.Round(value+fibre)));
            pixels[y*w+x]=new Pixel(Channel(background.R),Channel(background.G),Channel(background.B),background.A);
        }
        for(int y=0;y<ch;y++)
            Array.Copy(source,(height-crop[1]-ch+y)*width+crop[0],pixels,(dy+y)*w+dx,cw);
        return (w,h,pixels);
    }

    // Clean fine paper stock: small irregular fibres rather than a flat
    // digital fill or an ageing/stain effect. Printed artwork stays untouched.
    internal static Pixel[] StockGloss(int width,int height,int seed,bool recent)
    {
        if(width<2 || height<2 || width>1024 || height>1024)throw new InvalidDataException("Invalid paper finish dimensions.");
        var pixels=new Pixel[width*height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            byte value=(byte)Math.Round((recent?18:22)+Grain(x,y,seed)*(recent?4:6));
            pixels[y*width+x]=new Pixel(value,value,value,255);
        }
        return pixels;
    }
    private static float Grain(float x,float y,int seed)=>.65f*Noise(x*.37f+y*.09f,y*.67f,seed)
        +.35f*Noise(x*.11f-y*.04f,y*.82f,seed+571);
    private static float Noise(float x,float y,int seed)
    {
        int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y);float fx=x-ix,fy=y-iy;
        fx=fx*fx*(3-2*fx);fy=fy*fy*(3-2*fy);
        float Hash(int a,int b)
        {
            uint h=unchecked((uint)a*374761393u+(uint)b*668265263u+(uint)seed*1274126177u);
            h=unchecked((h^(h>>13))*1274126177u);h^=h>>16;
            return (h&65535)/32767.5f-1;
        }
        float low=Hash(ix,iy)*(1-fx)+Hash(ix+1,iy)*fx,high=Hash(ix,iy+1)*(1-fx)+Hash(ix+1,iy+1)*fx;
        return low*(1-fy)+high*fy;
    }
}
