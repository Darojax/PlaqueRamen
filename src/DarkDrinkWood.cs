using System;
using System.Collections.Generic;
using System.IO;

namespace PlaqueRamen;

internal readonly struct GrainImage
{
    internal readonly Pixel[] Pixels;
    internal readonly int Width, Height;
    internal GrainImage(Pixel[] pixels,int width,int height){Pixels=pixels;Width=width;Height=height;}
}

// Each drink keeps its own authored grain. Use the same stain, contrast and
// soft highlight/shadow treatment as the approved dark ramen material.
internal sealed class DarkDrinkWood
{
    private readonly Pixel[] _image;
    private readonly int _width, _height;
    private readonly GrainImage _second;
    private readonly FaceLayout _layout;
    private readonly WoodStatistics _native;
    private readonly double _mean, _deviation;

    internal DarkDrinkWood(Pixel[] image, int width, int height, Artwork art, WoodStatistics native, GrainImage? second = null)
    {
        if (width != 725 || height < 2169 || height > 2170 || image.Length != width * height)
            throw new InvalidDataException("Invalid drink grain source.");
        _image=image;_width=width;_height=height;_layout=new FaceLayout(height,art.InkTop,art.InkBottom);_native=native;
        _second=second??new GrainImage(image,width,height);
        if(_second.Width!=725||_second.Height<2169||_second.Height>2170||_second.Pixels.Length!=_second.Width*_second.Height)
            throw new InvalidDataException("Invalid companion drink grain source.");
        var wood=new List<Pixel>();var clear=new List<Pixel>();
        foreach(var sourceImage in new[]{new GrainImage(image,width,height),_second})
        for(int y=16;y<sourceImage.Height-16;y+=2)for(int x=120;x<600;x+=2)
        {
            var p=sourceImage.Pixels[y*sourceImage.Width+x];
            if(!PlaqueSurface.IsBlue(p)&&PlaqueSurface.Kind(p)==PaintKind.Wood)wood.Add(p);
        }
        for(int y=0;y<208;y++)for(int x=0;x<64;x++)clear.Add(Grain((x+.5f)/64,(_layout.SourceV((y+.5f)/208))*(height-1)));
        var source=WoodStatistics.Measure(wood);var grain=WoodStatistics.Measure(clear);
        _mean=Luma(grain.Mean);_deviation=Math.Max(1,Math.Max(Luma(source.Deviation),Luma(grain.Deviation)));
    }
    internal Pixel Sample(float u,float v)
    {
        var p=Grain(u,_layout.SourceV(v)*(_height-1));
        double tone=(p.R*.2126+p.G*.7152+p.B*.0722-_mean)/_deviation;
        if(tone>2)tone=2+.75*(1-Math.Exp(-(tone-2)/.75));
        if(tone<-1.5)tone=-1.5-.6*(1-Math.Exp((tone+1.5)/.6));
        byte C(int k)=>(byte)Math.Max(0,Math.Min(255,Math.Round(_native.Mean[k]+tone*_native.Deviation[k])));
        return new Pixel(C(0),C(1),C(2));
    }
    private Pixel Grain(float u,float y)
    {
        y=Math.Max(16,Math.Min(_height-17,y));
        // Combine clear margins from two approved ramen wood masters. Each
        // drink has a different pair, producing a distinct complete grain
        // pattern in the same coarse painted wood family as the dish boards.
        var a=PlaqueSurface.Sample(_image,_width,_height,90+u*120,y);
        var b=PlaqueSurface.Sample(_second.Pixels,_second.Width,_second.Height,550+u*85,y/(_height-1)*(_second.Height-1));
        float t=u*u*(3-2*u);
        byte Mix(byte c,byte d)=>(byte)Math.Round(c+(d-c)*t);
        return new Pixel(Mix(a.R,b.R),Mix(a.G,b.G),Mix(a.B,b.B));
    }
    private static double Luma(double[] a)=>a[0]*.2126+a[1]*.7152+a[2]*.0722;
}
