using PlaqueRamen;

internal static class DrinkPreview
{
    internal static void Run(string root, Artwork[] arts, Action<bool,string> check)
    {
        string input=Path.Combine(root,"artifacts/drink-preview-inputs"), output=Path.Combine(root,"artifacts/drink-preview-0.2.1");
        Directory.CreateDirectory(output);
        var atlas=DarkPreview.Read(Path.Combine(root,"artifacts/dark-preview-inputs/original.ppm"));
        var gloss=DarkPreview.Read(Path.Combine(root,"artifacts/dark-preview-inputs/gloss.ppm"));
        Pixel[] Crop(Pixel[] p,int left,int bottom,int w,int h)=>Enumerable.Range(0,h).SelectMany(y=>Enumerable.Range(0,w).Select(x=>p[(bottom+y)*2048+left+x])).ToArray();
        var oldLight=Crop(atlas.Pixels,PlaqueSurface.X,PlaqueSurface.Y,PlaqueSurface.Width,PlaqueSurface.Height);
        var light=new PlaqueSurface(oldLight,Crop(gloss.Pixels,PlaqueSurface.X,PlaqueSurface.Y,PlaqueSurface.Width,PlaqueSurface.Height));
        var oldDark=Crop(atlas.Pixels,DarkPlaqueSurface.X,DarkPlaqueSurface.Y,DarkPlaqueSurface.Width,DarkPlaqueSurface.Height);
        var dark=new DarkPlaqueSurface(oldDark,Crop(gloss.Pixels,DarkPlaqueSurface.X,DarkPlaqueSurface.Y,DarkPlaqueSurface.Width,DarkPlaqueSurface.Height));
        foreach(var art in arts)
        {
            var source=TopInput(input,art.Id+".light.ppm");
            var composed=light.ComposeDrink(source.Pixels,source.Width,source.Height,art);
            var view=new Pixel[187*819];
            for(int y=0;y<819;y++)for(int x=0;x<187;x++) view[y*187+x]=PlaqueSurface.Sample(composed.Albedo,PlaqueSurface.Width,PlaqueSurface.Height,
                FaceLayout.FaceLeft-PlaqueSurface.X+(x+.5f)/187*(FaceLayout.FaceRight-FaceLayout.FaceLeft)-.5f,
                FaceLayout.FaceBottom-PlaqueSurface.Y+(y+.5f)/819*(FaceLayout.FaceTop-FaceLayout.FaceBottom)-.5f);
            DarkPreview.Write(Path.Combine(output,art.Id+".light.ppm"),view,187,819);
            var names=view.Select((p,i)=>(p,i)).Where(t=>PlaqueSurface.IsBlue(t.p)).Select(t=>t.i).ToArray();
            var prices=view.Select((p,i)=>(p,i)).Where(t=>t.p.R>t.p.G*1.65&&t.p.R>t.p.B*1.45).Select(t=>t.i).ToArray();
            check(names.Length>500&&prices.Length>500,"actual light drink render retains readable blue name and red price: "+art.Id);
            check(names.Average(i=>view[i].R)<65&&names.Average(i=>view[i].G)<90&&names.Average(i=>view[i].B)<115,"light drink names use dark ink blue rather than bright cyan: "+art.Id);
            var ink=names.Concat(prices).ToArray();
            check(ink.Min(i=>i/187)>819*.07&&ink.Max(i=>i/187)<819*.93,"actual light drink ink clears the top pin and bottom edge: "+art.Id);
            check(names.Average(i=>i/187)>prices.Average(i=>i/187),"actual light drink name is above the price: "+art.Id);
            var d=TopInput(input,art.Id+".dark.ppm");var m=TopInput(input,art.Id+".paint.ppm");
            var a=DarkPreview.Read(Path.Combine(root,"artifacts/dark-preview-inputs",Path.GetFileNameWithoutExtension(art.DarkGrainA)+".ppm"));
            var b=DarkPreview.Read(Path.Combine(root,"artifacts/dark-preview-inputs",Path.GetFileNameWithoutExtension(art.DarkGrainB)+".ppm"));
            var second=new GrainImage(b.Pixels,b.Width,b.Height);
            var rendered=dark.ComposeDrink(d.Pixels,m.Pixels,d.Width,d.Height,a.Pixels,a.Width,a.Height,art,second);
            var bare=dark.ComposeDrink(d.Pixels,new Pixel[m.Pixels.Length],d.Width,d.Height,a.Pixels,a.Width,a.Height,art,second);
            var darkView=new Pixel[256*819];var woodView=new Pixel[256*819];
            bool exact=true,edges=true;
            for(int y=0;y<DarkPlaqueSurface.Height;y++)for(int x=0;x<DarkPlaqueSurface.Width;x++)
            {
                int i=y*DarkPlaqueSurface.Width+x;var p=rendered.Albedo[i];
                if(DarkPlaqueSurface.Inside(x,y))
                {
                    int j=(y-9)*256+x-8;darkView[j]=p;woodView[j]=bare.Albedo[i];var q=bare.Albedo[i];
                    if(m.Pixels[j].R==0)exact&=p.R==q.R&&p.G==q.G&&p.B==q.B;
                }
                else {var q=oldDark[i];edges&=p.R==q.R&&p.G==q.G&&p.B==q.B;}
            }
            check(exact,"actual dark drink has continuous unchanged wood outside its paint mask: "+art.Id);
            check(edges,"actual dark drink never changes native frame/edge pixels: "+art.Id);
            var gold=darkView.Select((p,i)=>(p,i)).Where(t=>t.i%256>128&&m.Pixels[t.i].R>245).Select(t=>t.p).ToArray();
            check(gold.Length>500&&gold.Select(p=>p.R).Distinct().Count()>12,"actual gold brush paint has visible native pigment variation: "+art.Id);
            var stats=WoodStatistics.Measure(woodView);
            check(stats.Deviation.All(d=>d>5)&&Enumerable.Range(0,3).All(k=>Math.Abs(stats.Mean[k]-dark.NativeWood.Mean[k])<8),"actual drink grain uses the ramen dark stain and contrast family: "+art.Id);
            DarkPreview.Write(Path.Combine(output,art.Id+".dark.ppm"),darkView,256,819);
            DarkPreview.Write(Path.Combine(output,art.Id+".wood.ppm"),woodView,256,819);
        }
    }
    private static (int Width,int Height,Pixel[] Pixels) TopInput(string input,string file)
    {
        var r=DarkPreview.Read(Path.Combine(input,file));var flipped=new Pixel[r.Pixels.Length];
        for(int y=0;y<r.Height;y++)Array.Copy(r.Pixels,y*r.Width,flipped,(r.Height-1-y)*r.Width,r.Width);
        return (r.Width,r.Height,flipped);
    }
}
