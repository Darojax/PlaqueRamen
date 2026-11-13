using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using PlaqueRamen;

internal static class CertificateChecks
{
    private sealed class Fixture
    {
        public float[][] vertices=Array.Empty<float[]>(),uv=Array.Empty<float[]>();
        public int[] indices=Array.Empty<int>();
        public int nativeMaterialCount=0;
        public string nativeOverlayMaterial="",nativeOverlayShader="";
    }
    internal static void Run(string root,Action<bool,string> check,Action<Action,string> refuses)
    {
        var options=new JsonSerializerOptions{IncludeFields=true};
        var art=JsonSerializer.Deserialize<CertificateArt[]>(File.ReadAllText(Path.Combine(root,"assets/Certificates.json")),options)!;
        check(art.Length==9 && art.Select(a=>a.Guid).Distinct().Count()==9,"original appreciation certificate, two awards, two permits, two educational certificates and two portraits");
        check(art[0].Guid==CertificateLayout.NativeGuid && art[1].Guid==CertificateLayout.HygieneGuid && art[2].Guid==CertificateLayout.CommunityGuid
            && art[3].Guid==CertificateLayout.Permit9Guid && art[4].Guid==CertificateLayout.Permit68Guid
            && art[5].Guid==CertificateLayout.CulinaryGuid && art[6].Guid==CertificateLayout.TrainingGuid
            && art[7].Guid==CertificateLayout.PortraitGuid && art[8].Guid==CertificateLayout.FamilyPortraitGuid,
            "certificate save identities are stable and distinct from the native item");
        foreach(var a in art)
        {
            a.Validate();var bytes=File.ReadAllBytes(Path.Combine(root,"assets/certificates",a.File));
            check(Convert.ToHexString(SHA256.HashData(bytes))==a.Sha256,"certificate source pixels match approved file: "+a.Id);
            int width=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4)),height=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
            check(a.Crop[0]+a.Crop[2]<=width && a.Crop[1]+a.Crop[3]<=height,"certificate paper crop stays inside original image: "+a.Id);
        }
        check(art[0].Width==0 && art[0].Height==0 && art[0].FrameTint.SequenceEqual(new float[]{1,1,1}),"original certificate keeps native size and frame finish");
        check(new[]{0,3,4,6}.All(i=>art[i].RecentPaper) && new[]{1,2,5}.All(i=>!art[i].RecentPaper),
            "recent paper and the older award/diploma stocks retain their individual finishes");
        check(art.Take(7).Where((a,i)=>i<3||i>4).All(a=>a.Price==-1 && a.ResolvePrice(5000)==5000),"certificates, awards and diplomas retain the native price");
        check(art[7].Price==10000 && art[7].ResolvePrice(5000)==10000,"Suzuki ancestor portrait costs the requested 10000 yen");
        check(art[8].Price==10000 && art[8].ResolvePrice(5000)==10000,"new family portrait costs the requested 10000 yen");
        check(art.Select(a=>a.IsPhotograph).SequenceEqual(new[]{false,false,false,false,false,false,false,true,true}),
            "photo-only lighting correction excludes the seven accepted certificate and award materials");
        check(art[7].Name=="Ancestor Portrait" && art[8].Name=="Family Portrait"
            && art.All(a=>new[]{"Suzuki","Ichiro","Ichirō","鈴木","一郎"}.All(n=>(a.Name+" "+a.Description).IndexOf(n,StringComparison.OrdinalIgnoreCase)<0)),
            "all framed item titles and descriptions retain the requested generic feel without family names");
        check(art[8].Width<=.224f && art[8].Height<=.28f && art[8].Height>art[8].Width && art[8].Crop[3]>art[8].Crop[2]
            && art[8].FlushWall,"the supplied family photo uses a modest portrait wall frame with its own original proportions");
        check(Math.Abs(art[7].Width-.336f*2/3)<.000001f && Math.Abs(art[7].Height-.436f*2/3)<.000001f
            && Math.Abs(art[7].BorderScale-.7f*2/3)<.000001f,"ancestor portrait's width, height and frame profile are two-thirds of the accepted original size");
        check(art.Skip(3).Take(2).All(a=>a.Price==2000 && a.ResolvePrice(5000)==2000),"both address-specific permits cost 2000 yen rather than inheriting the normal certificate price");
        check(art[3].Name=="Operating Permit - Sunset Yokocho 9" && art[4].Name=="Operating Permit - Sunset Yokocho 68",
            "each permit's shop title identifies its printed address before purchase");
        check(art[3].File!=art[4].File && art[3].Sha256!=art[4].Sha256,"each address uses its own permit artwork");
        var pricing=new CertificateArt();
        check(pricing.ResolvePrice(5000)==5000,"omitting a price override inherits the native price");
        pricing.Price=0;check(pricing.ResolvePrice(5000)==0,"explicit zero is a free price");
        pricing.Price=450;check(pricing.ResolvePrice(5000)==450,"a positive override remains an explicit item price");
        int originalPrice=art[3].Price;art[3].Price=-2;
        refuses(()=>art[3].Validate(),"invalid negative item prices are refused");art[3].Price=originalPrice;
        var fixture=JsonSerializer.Deserialize<Fixture>(File.ReadAllText(Path.Combine(root,"tests/certificate-fixture.json")),options)!;
        var native=CertificateLayout.Create(fixture.vertices,fixture.uv,fixture.indices,0,0);
        check(fixture.nativeMaterialCount==2 && fixture.nativeOverlayMaterial=="M_OutlineShelf" && fixture.nativeOverlayShader=="Shader Graphs/RimShaderSmoothNormal",
            "native renderer fixture includes the actual second outline material that the mesh-only fixture missed");
        var passes=native.Passes(fixture.nativeMaterialCount);
        check(passes.Length==3 && passes[0].SequenceEqual(native.Frame) && passes[1].SequenceEqual(native.Paper) && passes[2].SequenceEqual(fixture.indices),
            "native outline still draws the complete certificate after frame and paper are separated");
        check(native.Passes(1).Length==2,"a certificate renderer without the optional outline uses exactly its frame and paper slots");
        refuses(()=>native.Passes(3),"unreviewed extra certificate materials are rejected rather than dropped");
        check(native.Vertices.Zip(fixture.vertices).All(p=>p.First.SequenceEqual(p.Second)),"higher-resolution original keeps every native vertex position byte-for-byte");
        check(native.Frame.Length==102 && native.Paper.Length==6 && native.Frame.Concat(native.Paper).Order().SequenceEqual(fixture.indices.Order()),
            "paper separation keeps every triangle without overlap or missing frame geometry");
        var paper=native.Paper.Distinct().ToArray();
        check(native.Frame.Distinct().All(i=>native.Uv[i].SequenceEqual(fixture.uv[i])) && paper.All(i=>native.Uv[i].All(v=>v>=0&&v<=1)),
            "frame atlas UVs are unchanged and only paper receives the full-resolution map");
        var originalVertices=fixture.vertices.Select(v=>v.ToArray()).ToArray();
        var originalUv=fixture.uv.Select(v=>v.ToArray()).ToArray();
        foreach(var a in art.Skip(1))
        {
            var shape=CertificateLayout.Create(fixture.vertices,fixture.uv,fixture.indices,a);
            float Width(float[][] vs)=>vs.Max(v=>v[0])-vs.Min(v=>v[0]);
            float Height(float[][] vs)=>vs.Max(v=>v[1])-vs.Min(v=>v[1]);
            check(Math.Abs(Width(shape.Vertices)-a.Width)<.000001f && Math.Abs(Height(shape.Vertices)-a.Height)<.000001f,
                "new certificate geometry reaches its own physical size: "+a.Id);
            check(shape.Vertices.Min(v=>v[1])==fixture.vertices.Min(v=>v[1]),"both mounting styles retain the saved base pivot instead of shifting existing placements: "+a.Id);
            foreach(int axis in new[]{0,1})
            {
                float Border(float[][] vs,bool upper)=>upper?vs.Max(v=>v[axis])-paper.Max(i=>vs[i][axis]):paper.Min(i=>vs[i][axis])-vs.Min(v=>v[axis]);
                check(Math.Abs(Border(shape.Vertices,false)-Border(fixture.vertices,false)*a.BorderScale)<.000001f
                    && Math.Abs(Border(shape.Vertices,true)-Border(fixture.vertices,true)*a.BorderScale)<.000001f,"individual frame profile thickness matches its intended scale on axis "+axis+": "+a.Id);
            }
            float bottom=paper.Min(i=>shape.Vertices[i][1]),top=paper.Max(i=>shape.Vertices[i][1]);
            float slope=(paper.Where(i=>Math.Abs(shape.Vertices[i][1]-top)<.00001f).Average(i=>shape.Vertices[i][2])
                -paper.Where(i=>Math.Abs(shape.Vertices[i][1]-bottom)<.00001f).Average(i=>shape.Vertices[i][2]))/(top-bottom);
            float nativeRise=fixture.vertices[paper.OrderBy(i=>fixture.vertices[i][1]).Last()][2]-fixture.vertices[paper.OrderBy(i=>fixture.vertices[i][1]).First()][2];
            float expectedSlope=a.FlushWall?0:a.DisplayLedge?nativeRise/(top-bottom):.047254f;
            check(Math.Abs(slope-expectedSlope)<.0001f,"paper has the correct direct-wall or display-ledge lean: "+a.Id);
            if(a.FlushWall)check(Math.Abs(shape.Vertices.Max(v=>v[2])+.001f)<.000001f,"wall frame's whole back clears its placement plane by 1mm: "+a.Id);
            if(a.DisplayLedge)check(shape.Vertices.Zip(fixture.vertices).All(p=>p.First[2]==p.Second[2]) && shape.Vertices.Max(v=>v[2])<=0,
                "taller hygiene frame keeps native ledge depth contacts without intersecting the wall");
            float imageAspect=a.Crop[2]/(float)a.Crop[3];
            float rise=paper.Max(i=>shape.Vertices[i][2])-paper.Min(i=>shape.Vertices[i][2]);
            float actualAspect=(paper.Max(i=>shape.Vertices[i][0])-paper.Min(i=>shape.Vertices[i][0]))/(float)Math.Sqrt((top-bottom)*(top-bottom)+rise*rise);
            check(Math.Abs(shape.PaperAspect-imageAspect)<.01f && Math.Abs(shape.PaperAspect-actualAspect)<.000001f,
                "paper fitting matches the actual tilted or upright face without stretching the source: "+a.Id);
        }
        check(art[0].Mount=="Native"&&art[0].FrameStyle=="native"&&art[0].BorderScale==1&&!art[0].Gilt,"family keeps the accepted original frame and ledge");
        check(art.Skip(1).All(a=>a.Mount=="Wall"),"all eight added framed decorations, including both portraits, use wall placement");
        check(art[1].DisplayLedge && art.Where((a,i)=>i!=1).All(a=>!a.DisplayLedge),"only the Food Hygiene Award adds an Appreciation-style ledge; other selected mounting styles stay unchanged");
        check(art[1].FrameStyle=="oak" && art[2].FrameStyle=="mahogany" && art[3].FrameStyle=="metal" && art[4].FrameStyle=="metal"
            && art[5].FrameStyle=="walnut" && art[5].Gilt && art[6].FrameStyle=="oak","each document has its chosen frame finish, with matching practical frames for the two permit addresses");
        var frameBytes=File.ReadAllBytes(Path.Combine(root,"assets/certificates",CertificateFrameFinish.SourceFile));
        check(Convert.ToHexString(SHA256.HashData(frameBytes))==CertificateFrameFinish.SourceSha,"frame finishes derive from the exact original native grain atlas");
        var support=JsonSerializer.Deserialize<Fixture>(File.ReadAllText(Path.Combine(root,"tests/certificate-support-fixture.json")),options)!;
        var supportCopy=support.vertices.Select(v=>v.ToArray()).ToArray();
        var ledge=CertificateSupport.Create(support.vertices,art[1]);
        float LedgeWidth(float[][] vs)=>vs.Max(v=>v[0])-vs.Min(v=>v[0]);
        float originalOverhang=LedgeWidth(support.vertices)-LedgeWidth(fixture.vertices);
        check(support.indices.Length==60 && Math.Abs((LedgeWidth(ledge)-art[1].Width)-originalOverhang)<.000001f,
            "hygiene ledge retains the original native overhang beyond each side of its wider frame");
        check(ledge.Zip(support.vertices).All(p=>p.First[1]==p.Second[1]&&p.First[2]==p.Second[2]),
            "ledge height, depth, mounting lip and wall contact stay byte-identical to the real native support");
        check(Math.Abs((ledge.Max(v=>v[0])+ledge.Min(v=>v[0]))-(support.vertices.Max(v=>v[0])+support.vertices.Min(v=>v[0])))<.000001f,
            "wider ledge retains its native centre on the wall");
        check(CertificateSupport.Create(support.vertices,art[0]).Zip(support.vertices).All(p=>p.First.SequenceEqual(p.Second))
            && support.vertices.Zip(supportCopy).All(p=>p.First.SequenceEqual(p.Second)),"native family ledge and shared source vertices remain unchanged");
        refuses(()=>CertificateSupport.Create(support.vertices.Take(35).ToArray(),art[1]),"changed native display-ledge topology is rejected");
        refuses(()=>CertificateLayout.Create(fixture.vertices,fixture.uv,fixture.indices,.3f,.4f,.01f,true),"impossibly thin frame profiles are rejected");
        check(fixture.vertices.Zip(originalVertices).All(p=>p.First.SequenceEqual(p.Second)) && fixture.uv.Zip(originalUv).All(p=>p.First.SequenceEqual(p.Second)),
            "creating differently sized variants never modifies their shared native source arrays");
        var bad=fixture.indices.ToArray();bad[0]=1000;
        refuses(()=>CertificateLayout.Create(fixture.vertices,fixture.uv,bad,0,0),"unknown certificate topology is rejected instead of damaging another object");
        refuses(()=>CertificateLayout.Create(fixture.vertices,fixture.uv,fixture.indices,-1,.3f),"invalid custom certificate dimensions are rejected");
        var source=Enumerable.Range(0,80).Select(i=>new Pixel((byte)i,0,0)).ToArray();
        var fitted=CertificatePaper.Fit(source,10,8,new[]{2,1,4,3},1);
        check(fitted.Width==4 && fitted.Height==4 && Enumerable.Range(0,3).All(y=>Enumerable.Range(0,4).All(x=>fitted.Pixels[y*4+x].R==source[(4+y)*10+2+x].R)),
            "paper fitting copies the chosen source pixels without scaling or vertical inversion");
        check(fitted.Pixels.Skip(12).All(p=>Math.Abs(p.R-source[62].R)<=2),"aspect correction uses matching fine paper rather than the photograph's wooden frame");
        refuses(()=>CertificatePaper.Fit(source,10,8,new[]{9,0,4,3},1),"paper crop cannot read beyond the image");
        refuses(()=>CertificatePaper.Fit(source,10,8,new[]{0,0,4,3},float.NaN),"invalid paper aspect is rejected");
        refuses(()=>CertificatePaper.Fit(new Pixel[8192],8192,1,new[]{0,0,8192,1},1),"paper fitting is bounded to a 4096 texture budget");
        var finish=CertificatePaper.StockGloss(128,128,20,true);var again=CertificatePaper.StockGloss(128,128,20,true);
        check(finish.Zip(again).All(p=>p.First.R==p.Second.R),"recent paper finish is stable rather than animated noise");
        check(finish.Select(p=>p.R).Distinct().Count()>5 && finish.All(p=>p.R>=14&&p.R<=22&&p.R==p.G&&p.G==p.B),
            "recent certificate has gentle matte fibre variation, not a uniform plastic sheen or colour ageing");
        check(!finish.Select(p=>p.R).SequenceEqual(CertificatePaper.StockGloss(128,128,21,true).Select(p=>p.R)),"individual paper stocks have distinct fibre patterns");
        check(Enumerable.Range(1,finish.Length-1).Where(i=>i%128!=0).Max(i=>Math.Abs(finish[i].R-finish[i-1].R))<=5,
            "paper finish varies softly between nearby texels");
        refuses(()=>CertificatePaper.StockGloss(2048,2048,1,true),"paper finish cannot allocate an unbounded extra texture");
    }
}
