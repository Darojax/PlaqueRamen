using PlaqueRamen;
using System.Text.Json;

internal static class CertificateFinishChecks
{
    private sealed class Fixture
    {
        public float[][] vertices=Array.Empty<float[]>(),uv=Array.Empty<float[]>();
        public int[] indices=Array.Empty<int>();
    }
    internal static void Run(string root,Action<bool,string> check,Action<Action,string> refuses)
    {
        var options=new JsonSerializerOptions{IncludeFields=true};
        var f=JsonSerializer.Deserialize<Fixture>(File.ReadAllText(Path.Combine(root,"tests/certificate-fixture.json")),options)!;
        var support=JsonSerializer.Deserialize<Fixture>(File.ReadAllText(Path.Combine(root,"tests/certificate-support-fixture.json")),options)!;
        check(support.vertices.Length==36&&support.indices.Length==60,"reviewed native support is the small 43cm-wide display ledge, not an easel leg");
        check(Math.Abs(support.vertices.Max(v=>v[0])-support.vertices.Min(v=>v[0])-.42624436f)<.000001f,"native display-ledge width is retained for the family keepsake");
        var source=Enumerable.Range(0,512*512).Select(i=>new Pixel((byte)(35+i%80),(byte)(26+i%60),(byte)(18+i%42),255)).ToArray();
        var copy=source.ToArray();bool Equal(Pixel a,Pixel b)=>a.R==b.R&&a.G==b.G&&a.B==b.B&&a.A==b.A;
        var original=new CertificateArt();var native=CertificateLayout.Create(f.vertices,f.uv,f.indices,0,0);
        check(source.Zip(CertificateFrameFinish.Compose(source,512,512,original,native)).All(p=>Equal(p.First,p.Second)),"native finish leaves every original atlas pixel exact");
        var oak=new CertificateArt{Width=.452f,Height=.357f,BorderScale=1.35f,Mount="Wall",FrameStyle="oak"};
        var oakShape=CertificateLayout.Create(f.vertices,f.uv,f.indices,oak);var light=CertificateFrameFinish.Compose(source,512,512,oak,oakShape);
        check(light.Select(p=>p.R).Distinct().Count()>50&&light.Zip(source).All(p=>p.First.R>=p.Second.R&&p.First.A==p.Second.A),"lighter oak retains directional source variation and alpha instead of becoming a flat fill");
        var school=new CertificateArt{Width=.502f,Height=.370f,Mount="Wall",FrameStyle="walnut"};
        var schoolShape=CertificateLayout.Create(f.vertices,f.uv,f.indices,school);
        var plain=CertificateFrameFinish.Compose(source,512,512,school,schoolShape);school.Gilt=true;
        var gilt=CertificateFrameFinish.Compose(source,512,512,school,schoolShape);
        int changed=plain.Zip(gilt).Count(p=>!Equal(p.First,p.Second));
        check(changed>20&&changed<source.Length/8,"gilt changes a narrow inner fillet rather than coating the whole wooden frame ("+changed+" texels)");
        check(gilt.Zip(CertificateFrameFinish.Compose(source,512,512,school,schoolShape)).All(p=>Equal(p.First,p.Second)),"frame patina and grain are deterministic and never animate");
        var metal=new CertificateArt{FrameStyle="metal"};var satin=CertificateFrameFinish.Compose(source,512,512,metal,native);
        check(satin.All(p=>p.R==44&&p.G==49&&p.B==54&&p.A==255),"permit frames have their own neutral satin-metal base instead of darkened wood smudges");
        check(source.Zip(copy).All(p=>Equal(p.First,p.Second)),"all finish variants leave their shared original grain untouched");
        refuses(()=>CertificateFrameFinish.Compose(source,256,1024,school,schoolShape),"unreviewed atlas layouts cannot be recoloured accidentally");
    }
}
