using PlaqueRamen;

internal static class CertificateIconChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> refuses)
    {
        bool Equal(Pixel a,Pixel b)=>a.R==b.R&&a.G==b.G&&a.B==b.B&&a.A==b.A;
        foreach(var (w,h) in new[]{(6,10),(10,6),(8,8)})
        {
            var source=new Pixel[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)source[y*w+x]=new Pixel((byte)(x*20),(byte)(y*20),90);
            var copy=source.ToArray();var icon=CertificateIcon.Compose(source,w,h);
            var visible=Enumerable.Range(0,icon.Length).Where(i=>icon[i].A>0).ToArray();
            int left=visible.Min(i=>i%512),right=visible.Max(i=>i%512),bottom=visible.Min(i=>i/512),top=visible.Max(i=>i/512);
            check(icon.Length==512*512 && left>=16&&bottom>=16&&right<496&&top<496,"native-size square icon keeps transparent safety margins: "+w+"x"+h);
            check(Math.Abs((right-left+1)/(float)(top-bottom+1)-w/(float)h)<.003f
                && Math.Abs(left-(511-right))<=1 && Math.Abs(bottom-(511-top))<=1,"portrait, landscape and square icons retain aspect and centre: "+w+"x"+h);
            check(Equal(icon[bottom*512+left],source[0]) && Equal(icon[top*512+right],source[^1])
                && Equal(icon[top*512+left],source[(h-1)*w]) && Equal(icon[bottom*512+right],source[w-1]),
                "all four image corners survive icon fitting rather than vertical or horizontal cropping: "+w+"x"+h);
            check(source.Zip(copy).All(p=>Equal(p.First,p.Second)),"inventory fitting never modifies the high-resolution wall artwork: "+w+"x"+h);
        }
        var transparent=Enumerable.Repeat(new Pixel(10,20,30,128),12).ToArray();var fitted=CertificateIcon.Compose(transparent,3,4);
        check(fitted.Where(p=>p.A>0).All(p=>p.A==128)&&fitted.Any(p=>p.A==0),"icon fitting retains source alpha and fully transparent padding");
        refuses(()=>CertificateIcon.Compose(Array.Empty<Pixel>(),0,0),"empty icon inputs are refused");
        refuses(()=>CertificateIcon.Compose(new Pixel[4],3,4),"inconsistent icon buffers are refused");
        refuses(()=>CertificateIcon.Compose(Array.Empty<Pixel>(),8192,8192),"unbounded icon sources are refused");
    }
}
