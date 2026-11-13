using System.Security.Cryptography;
using System.Text.Json;
using PlaqueRamen;

// Orthographic inspection render of the actual native triangles/UVs and the
// runtime paper compositor. No simulated game lighting or live acceptance.
internal static class CertificatePreview
{
    private sealed class Fixture
    {
        public float[][] vertices=Array.Empty<float[]>(),uv=Array.Empty<float[]>();
        public int[] indices=Array.Empty<int>();
    }
    private sealed class Image
    {
        internal int Width,Height;
        internal Pixel[] Pixels=Array.Empty<Pixel>();
    }
    internal static void Run(string root,Action<bool,string> check)
    {
        var json=new JsonSerializerOptions{IncludeFields=true};
        var fixture=JsonSerializer.Deserialize<Fixture>(File.ReadAllText(Path.Combine(root,"tests/certificate-fixture.json")),json)!;
        var support=JsonSerializer.Deserialize<Fixture>(File.ReadAllText(Path.Combine(root,"tests/certificate-support-fixture.json")),json)!;
        var arts=JsonSerializer.Deserialize<CertificateArt[]>(File.ReadAllText(Path.Combine(root,"assets/Certificates.json")),json)!;
        string input=Path.Combine(root,"artifacts/certificate-preview-inputs"),output=Path.Combine(root,"artifacts/certificate-preview-renders");
        string preview=Path.Combine(root,"resources/certificates-preview-v2");
        Directory.CreateDirectory(output);Directory.CreateDirectory(preview);
        Image Read(string name)
        {
            var ppm=DarkPreview.Read(Path.Combine(input,name+".ppm"));var pixels=new Pixel[ppm.Pixels.Length];
            for(int y=0;y<ppm.Height;y++)Array.Copy(ppm.Pixels,y*ppm.Width,pixels,(ppm.Height-1-y)*ppm.Width,ppm.Width);
            return new Image{Width=ppm.Width,Height=ppm.Height,Pixels=pixels};
        }
        var native=Read("native-atlas");var nativeCopy=native.Pixels.ToArray();
        (float[][] Vertices,float[][] Uv,int[] Frame) Model(CertificateShape shape,CertificateArt art,bool original=false)
        {
            var frame=original?fixture.indices:shape.Frame;
            if(art.FlushWall)return(shape.Vertices,shape.Uv,frame);
            var vs=CertificateSupport.Create(support.vertices,art);
            return(shape.Vertices.Concat(vs).ToArray(),shape.Uv.Concat(support.uv).ToArray(),frame.Concat(support.indices.Select(i=>i+shape.Vertices.Length)).ToArray());
        }
        var oldModel=Model(CertificateLayout.Create(fixture.vertices,fixture.uv,fixture.indices,0,0),arts[0],true);
        var old=Draw(oldModel.Vertices,oldModel.Uv,oldModel.Frame,Array.Empty<int>(),native,native,new float[]{1,1,1});
        DarkPreview.Write(Path.Combine(output,"native-original.ppm"),old.Pixels,old.Width,old.Height);
        var records=new List<object>();
        foreach(var art in arts)
        {
            var shape=CertificateLayout.Create(fixture.vertices,fixture.uv,fixture.indices,art);
            var source=Read(art.Id);var fit=CertificatePaper.Fit(source.Pixels,source.Width,source.Height,art.Crop,shape.PaperAspect);
            var paper=new Image{Width=fit.Width,Height=fit.Height,Pixels=fit.Pixels};
            var model=Model(shape,art);
            var frame=new Image{Width=native.Width,Height=native.Height,Pixels=CertificateFrameFinish.Compose(native.Pixels,native.Width,native.Height,art,shape)};
            var rendered=Draw(model.Vertices,model.Uv,model.Frame,shape.Paper,frame,paper,art.FrameTint);
            var angled=Draw(model.Vertices,model.Uv,model.Frame,shape.Paper,frame,paper,art.FrameTint,32);
            DarkPreview.Write(Path.Combine(output,art.Id+".ppm"),rendered.Pixels,rendered.Width,rendered.Height);
            DarkPreview.Write(Path.Combine(output,art.Id+".paper.ppm"),paper.Pixels,paper.Width,paper.Height);
            DarkPreview.Write(Path.Combine(output,art.Id+".angle.ppm"),angled.Pixels,angled.Width,angled.Height);
            if(art.Width!=0)
            {
                var icon=CertificateIcon.Compose(fit.Pixels,fit.Width,fit.Height);
                var bytes=new byte[icon.Length*4];
                for(int i=0;i<icon.Length;i++){bytes[i*4]=icon[i].R;bytes[i*4+1]=icon[i].G;bytes[i*4+2]=icon[i].B;bytes[i*4+3]=icon[i].A;}
                File.WriteAllBytes(Path.Combine(output,art.Id+".icon.rgba"),bytes);
                var visible=Enumerable.Range(0,icon.Length).Where(i=>icon[i].A>0).ToArray();
                int w=visible.Max(i=>i%512)-visible.Min(i=>i%512)+1,h=visible.Max(i=>i/512)-visible.Min(i=>i/512)+1;
                check(Math.Abs(w/(float)h-fit.Width/(float)fit.Height)<.003f,
                    "actual inventory icon preserves the full paper's aspect in a native square sprite: "+art.Id);
            }
            int dx=(fit.Width-art.Crop[2])/2,dy=(fit.Height-art.Crop[3])/2;
            check(Enumerable.Range(0,art.Crop[3]).All(y=>Enumerable.Range(0,art.Crop[2]).All(x=>Equal(
                source.Pixels[(source.Height-art.Crop[1]-art.Crop[3]+y)*source.Width+art.Crop[0]+x],
                fit.Pixels[(dy+y)*fit.Width+dx+x]))),"actual high-resolution paper copies all selected source pixels exactly: "+art.Id);
            check(fit.Width*fit.Height>1_000_000 && fit.Width<=2048 && fit.Height<=2048,"actual certificate paper uses a sharp but bounded texture: "+art.Id);
            check(rendered.PaperMask.Count(p=>p)>40_000,"actual framed preview has a substantial visible high-resolution paper face: "+art.Id);
            if(art.Width==0)
            {
                check(fit.Pixels[0].R>220 && fit.Pixels[0].G>210 && fit.Pixels[0].B>170 && fit.Pixels[0].R-fit.Pixels[0].G<25,
                    "original certificate aspect margins match cream paper rather than the photograph's pink surround");
                check(old.Width==rendered.Width && old.Height==rendered.Height,"original and upgraded certificates have identical rendered physical bounds");
                check(Enumerable.Range(0,old.Pixels.Length).Where(i=>!rendered.PaperMask[i]).All(i=>Equal(old.Pixels[i],rendered.Pixels[i])),
                    "all pixels outside the upgraded paper face, including native wood and background, remain identical");
            }
            records.Add(new{id=art.Id,width=rendered.Width,height=rendered.Height,paperWidth=fit.Width,paperHeight=fit.Height,
                angleWidth=angled.Width,angleHeight=angled.Height,mount=art.Mount,displayLedge=art.DisplayLedge,frameStyle=art.FrameStyle,borderScale=art.BorderScale,gilt=art.Gilt,
                paperAspect=shape.PaperAspect,iconSize=art.Width==0?0:CertificateIcon.Size,sourcePixelCopyExact=true,render=art.Id+".png"});
        }
        check(native.Pixels.Zip(nativeCopy).All(p=>Equal(p.First,p.Second)),"certificate rendering never changes the original game atlas");
        File.WriteAllText(Path.Combine(preview,"render-verification.json"),JsonSerializer.Serialize(new{pixelsPerMetre=2200,records},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static bool Equal(Pixel a,Pixel b)=>a.R==b.R&&a.G==b.G&&a.B==b.B&&a.A==b.A;
    private sealed class Render
    {
        internal int Width,Height;
        internal Pixel[] Pixels=Array.Empty<Pixel>();
        internal bool[] PaperMask=Array.Empty<bool>();
    }
    private static Render Draw(float[][] vertices,float[][] uv,int[] frame,int[] paper,Image native,Image artwork,float[] tint,float yaw=0)
    {
        if(yaw!=0)
        {
            float angle=yaw*(float)Math.PI/180,c=(float)Math.Cos(angle),s=(float)Math.Sin(angle);
            vertices=vertices.Select(v=>new[]{v[0]*c+v[2]*s,v[1],-v[0]*s+v[2]*c}).ToArray();
        }
        const float scale=2200;const int pad=18;
        float minX=vertices.Min(v=>v[0]),maxX=vertices.Max(v=>v[0]),minY=vertices.Min(v=>v[1]),maxY=vertices.Max(v=>v[1]);
        int w=(int)Math.Ceiling((maxX-minX)*scale)+pad*2,h=(int)Math.Ceiling((maxY-minY)*scale)+pad*2;
        var render=new Render{Width=w,Height=h,Pixels=Enumerable.Repeat(new Pixel(43,45,43),w*h).ToArray(),PaperMask=new bool[w*h]};
        var depth=Enumerable.Repeat(float.PositiveInfinity,w*h).ToArray();
        void Triangles(int[] indices,Image image,bool isPaper)
        {
            for(int t=0;t<indices.Length;t+=3)
            {
                int a=indices[t],b=indices[t+1],c=indices[t+2];
                float X(int i)=>(vertices[i][0]-minX)*scale+pad;
                float Y(int i)=>(vertices[i][1]-minY)*scale+pad;
                float ax=X(a),ay=Y(a),bx=X(b),by=Y(b),cx=X(c),cy=Y(c);
                float area=(by-cy)*(ax-cx)+(cx-bx)*(ay-cy);if(Math.Abs(area)<.00001f)continue;
                int x0=Math.Max(0,(int)Math.Floor(Math.Min(ax,Math.Min(bx,cx)))),x1=Math.Min(w-1,(int)Math.Ceiling(Math.Max(ax,Math.Max(bx,cx))));
                int y0=Math.Max(0,(int)Math.Floor(Math.Min(ay,Math.Min(by,cy)))),y1=Math.Min(h-1,(int)Math.Ceiling(Math.Max(ay,Math.Max(by,cy))));
                for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
                {
                    float px=x+.5f,py=y+.5f;
                    float wa=((by-cy)*(px-cx)+(cx-bx)*(py-cy))/area,wb=((cy-ay)*(px-cx)+(ax-cx)*(py-cy))/area,wc=1-wa-wb;
                    if(wa<-.000001f||wb<-.000001f||wc<-.000001f)continue;
                    float z=vertices[a][2]*wa+vertices[b][2]*wb+vertices[c][2]*wc;int i=y*w+x;
                    if(z>depth[i]+.000001f)continue;depth[i]=z;
                    float u=uv[a][0]*wa+uv[b][0]*wb+uv[c][0]*wc,v=uv[a][1]*wa+uv[b][1]*wb+uv[c][1]*wc;
                    var p=PlaqueSurface.Sample(image.Pixels,image.Width,image.Height,u*image.Width-.5f,v*image.Height-.5f);
                    if(!isPaper)p=new Pixel((byte)Math.Min(255,p.R*tint[0]),(byte)Math.Min(255,p.G*tint[1]),(byte)Math.Min(255,p.B*tint[2]));
                    render.Pixels[i]=p;render.PaperMask[i]=isPaper;
                }
            }
        }
        Triangles(frame,native,false);Triangles(paper,artwork,true);return render;
    }
}
