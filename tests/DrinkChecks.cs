using PlaqueRamen;
using System.Buffers.Binary;
using System.Security.Cryptography;

internal static class DrinkChecks
{
    internal static void Run(string root, Artwork[] arts, Action<bool,string> check, Action<Action,string> refuses)
    {
        var bottle = arts.Single(a => a.Id == "horizon-bottle"); var draft = arts.Single(a => a.Id == "horizon-draft");
        var food = arts.Where(a => !a.Drink).ToArray();
        check(bottle.Drink && bottle.Yen == 450 && draft.Drink && draft.Yen == 550, "actual bottle and draft selling prices");
        foreach(var drink in new[]{bottle,draft})
            check(drink.DarkGrainA!=drink.DarkGrainB&&arts.Any(a=>!a.Drink&&a.File==drink.DarkGrainA)&&arts.Any(a=>!a.Drink&&a.File==drink.DarkGrainB),"drink grain combines two approved ramen wood masters: "+drink.Id);
        check(bottle.DarkGrainA!=draft.DarkGrainA&&bottle.DarkGrainB!=draft.DarkGrainB,"drinks have different complete grain patterns");
        foreach (var art in new[] { bottle, draft })
            foreach (var file in new[] { (art.DarkFile, art.DarkSha256), (art.DarkMaskFile, art.DarkMaskSha256) })
            {
                var bytes = File.ReadAllBytes(Path.Combine(root,"assets/art",file.Item1));
                check(Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.Item2, StringComparison.OrdinalIgnoreCase), "approved dark drink layer hash: " + file.Item1);
                check(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4)) == 256 && BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4)) == 819, "dark drink and mask use native face dimensions: " + file.Item1);
            }
        var grouped = MenuPlan.Build(new[] { new MenuEntry(0, draft.OrderGuid,550,false), new MenuEntry(1,bottle.OrderGuid,450,false),
            new MenuEntry(3,food[0].OrderGuid,food[0].Yen,true), new MenuEntry(6,food[4].OrderGuid,food[4].Yen,true) }, arts);
        check(grouped.Select(p=>p.Art.Id).SequenceEqual(new[] {food[0].Id,food[4].Id,draft.Id,bottle.Id}), "food stays right of drinks even when drinks occur first on the machine");
        check(MenuPlan.Build(new[] {new MenuEntry(0,bottle.OrderGuid,450,false),new MenuEntry(8,bottle.OrderGuid,450,false)},arts).Length == 1, "repeated drink occupies one plaque");
        check(MenuPlan.SlotRanks(grouped,9).SequenceEqual(new[] {2,3,5,6}), "mixed four-item group has a centred one-slot separator");
        PlannedPlaque P(Artwork art, int i) => new(i,art);
        var crowded = food.Take(6).Select(P).Concat(new[] { P(draft,6), P(bottle,7) }).ToArray();
        var seven = MenuPlan.ForCapacity(crowded,7);
        check(seven.Length == 7 && seven.Last().Art == bottle && seven.Count(p=>!p.Art.Drink) == 6, "six dishes plus both drinks on dark wood keeps Bottle, not Draft");
        check(MenuPlan.SlotRanks(seven,7).SequenceEqual(Enumerable.Range(0,7)), "full dark holder has no separating gap");
        var onlyDraft = MenuPlan.ForCapacity(crowded.Where(p=>p.Art!=bottle).ToArray(),7);
        check(onlyDraft.Last().Art==draft && onlyDraft.Length==7, "draft is displayed when it is the only selected drink with one free slot");
        var all = food.Take(7).Select(P).Concat(new[] {P(bottle,7),P(draft,8)}).ToArray();
        check(MenuPlan.ForCapacity(all,7).All(p=>!p.Art.Drink), "seven selected dishes take precedence over all drinks");
        check(MenuPlan.ForCapacity(all,9).Length==9 && MenuPlan.SlotRanks(MenuPlan.ForCapacity(all,9),9).SequenceEqual(Enumerable.Range(0,9)), "nine-slot board displays every selected dish and drink with no gap");
        var five = food.Take(5).Select(P).Concat(new[] {P(bottle,5),P(draft,6)}).ToArray();
        check(MenuPlan.SlotRanks(MenuPlan.ForCapacity(five,7),7).SequenceEqual(Enumerable.Range(0,7)), "five dishes and two drinks fill the dark board");
        var four = food.Take(4).Select(P).Concat(new[] {P(bottle,4),P(draft,5)}).ToArray();
        check(MenuPlan.SlotRanks(MenuPlan.ForCapacity(four,7),7).SequenceEqual(new[]{0,1,2,3,5,6}), "four dishes leave one empty fixed hook before both drinks");
        foreach(int cap in new[]{7,9}) foreach(int foodCount in Enumerable.Range(0,10)) foreach(int drinkCount in Enumerable.Range(0,3))
        {
            var selected=MenuPlan.ForCapacity(food.Take(foodCount).Select(P).Concat(new[]{P(bottle,10),P(draft,11)}.Take(drinkCount)).ToArray(),cap);
            var slots=MenuPlan.SlotRanks(selected,cap);
            check(slots.Length==selected.Length && slots.Distinct().Count()==slots.Length && slots.All(s=>s>=0&&s<cap), $"slot limits and unique hooks: {cap}/{foodCount}/{drinkCount}");
            check(selected.TakeWhile(p=>!p.Art.Drink).Count()==Math.Min(cap,foodCount), $"food priority: {cap}/{foodCount}/{drinkCount}");
        }
        refuses(()=>MenuPlan.Build(new[]{new MenuEntry(0,bottle.OrderGuid,500,false)},arts), "changed drink price cannot show stale artwork");
        refuses(()=>MenuPlan.Build(new[]{new MenuEntry(0,bottle.OrderGuid,450,true)},arts), "changed recipe category is refused");

        int w=DarkPlaqueSurface.Width,h=DarkPlaqueSurface.Height;
        var original=Enumerable.Repeat(new Pixel(40,35,30),w*h).ToArray();
        var gloss=Enumerable.Repeat(new Pixel(40,40,40),w*h).ToArray();
        for(int y=300;y<330;y++)for(int x=100;x<130;x++){original[y*w+x]=new Pixel(160,158,160);gloss[y*w+x]=new Pixel(180,180,180);}
        var surface=new DarkPlaqueSurface(original,gloss);
        var face=Enumerable.Range(0,256*819).Select(i=>new Pixel((byte)(25+i%19),(byte)(22+i%17),(byte)(20+i%13))).ToArray();
        var mask=Enumerable.Repeat(new Pixel(0,0,0),face.Length).ToArray(); mask[100*256+150]=new Pixel(255,255,255);
        var grain=Enumerable.Range(0,725*2170).Select(i=>new Pixel((byte)(150+i%11),(byte)(106+i/725%9),(byte)(80+i%5))).ToArray();
        var artFixture=new Artwork{InkTop=232,InkBottom=1986,Drink=true};
        var bare=new DarkDrinkWood(grain,725,2170,artFixture,surface.NativeWood);
        var result=surface.ComposeDrink(face,mask,256,819,grain,725,2170,artFixture);
        bool faceExact=true,edgeExact=true;
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            var actual=result.Albedo[y*w+x];var expected=original[y*w+x];
            if(DarkPlaqueSurface.Inside(x,y))
            {
                float u=(DarkPlaqueSurface.X+x+.5f-DarkPlaqueSurface.Left)/(DarkPlaqueSurface.Right-DarkPlaqueSurface.Left);
                float v=(DarkPlaqueSurface.Y+y+.5f-DarkPlaqueSurface.Bottom)/(DarkPlaqueSurface.Top-DarkPlaqueSurface.Bottom);
                expected=bare.Sample(u,v);
                if(mask[(y-9)*256+x-8].R>0)continue;
            }
            bool equal=actual.R==expected.R&&actual.G==expected.G&&actual.B==expected.B;
            if(DarkPlaqueSurface.Inside(x,y))faceExact&=equal;else edgeExact&=equal;
        }
        check(faceExact,"clean dark grain stays exact everywhere outside the independent paint mask");
        check(edgeExact,"native dark edges and unrelated atlas pixels are unchanged");
        check(result.Gloss[(100+9)*w+150+8].R>120 && result.Gloss[(100+9)*w+150+8].R<=180 && result.Gloss[10*w+10].R==40,"worn paint gloss follows its independent mask and never a rectangular text area");
        var repeat=surface.ComposeDrink(face,mask,256,819,grain,725,2170,artFixture);
        check(repeat.Albedo.Select(p=>(p.R,p.G,p.B)).SequenceEqual(result.Albedo.Select(p=>(p.R,p.G,p.B))),"paint wear is deterministic and does not shimmer or change on a menu refresh");
        refuses(()=>surface.ComposeDrink(face,mask,819,256,grain,725,2170,artFixture),"rotated drink texture cannot activate");
    }
}
