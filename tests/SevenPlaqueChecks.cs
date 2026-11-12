using PlaqueRamen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class SevenPlaqueChecks
{
    internal static void Run(string root, Artwork[] arts, Action<bool, string> check, Action<Action, string> refuses)
    {
        var json = new JsonSerializerOptions { IncludeFields = true };
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests/seven-holder-fixture.json")));
        var holder = JsonSerializer.Deserialize<HolderDefinition>(doc.RootElement.GetProperty("Manifest").GetRawText(), json)!;
        var parts = JsonSerializer.Deserialize<HolderParts>(File.ReadAllText(Path.Combine(root, "src/HolderParts.Seven.json")), json)!;
        var indices = doc.RootElement.GetProperty("Indices").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var vertices = doc.RootElement.GetProperty("Vertices").EnumerateArray().Select(v => new HookPoint(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle())).ToArray();
        HolderGeometry.Validate(holder, vertices.Length, 1, indices); parts.Validate(holder, indices);
        check(holder.Dark && holder.Capacity == 7 && vertices.Length == 1609 && indices.Length == 2194 * 3, "actual dark seven-plaque native geometry verified");
        var row = AutoLayout.RightToLeft(holder);
        check(row.SequenceEqual(Enumerable.Range(1, 7).Reverse()), "dark holder reads right to left in the native front view");
        var boardTriangles = parts.Plaques.SelectMany(p => p.BoardTriangleOffsets).ToHashSet();
        var fastenerTriangles = parts.Plaques.SelectMany(p => p.HookTriangleOffsets).ToHashSet();
        for (int n = 0; n <= 7; n++)
        {
            var groups = MenuPlan.Submeshes(indices, holder, parts, row, n);
            var chosen = MenuPlan.CenteredPlaques(row, n);
            check(groups.Length == 8 && groups.Sum(g => g.Length) == (2110 + 12 * n) * 3, "only unused dark boards are hidden for count " + n);
            check(groups[0].SequenceEqual(Enumerable.Range(0, 2194).Where(i => !boardTriangles.Contains(i)).SelectMany(i => indices.Skip(i * 3).Take(3))),
                "all seven pins and the native frame remain fixed for count " + n);
            var ranks = chosen.Select(id => Array.IndexOf(row, id)).ToArray();
            check(n == 0 || ranks.Zip(ranks.Skip(1)).All(p => p.Second == p.First + 1), "dark plaques stay together for count " + n);
            check(n == 0 || ranks.Contains(3) && (n % 2 == 0 ? ranks.Count(i => i < 3) == ranks.Count(i => i > 3) + 1 : ranks.Count(i => i < 3) == ranks.Count(i => i > 3)),
                "dark group balances odd counts and leans right for even count " + n);
        }
        var mounted = HookMount.Connect(holder, parts, indices, vertices);
        var changed = new HashSet<int>();
        foreach (var p in parts.Plaques)
        {
            var pin = p.HookTriangleOffsets.SelectMany(t => indices.Skip(t * 3).Take(3)).Distinct().ToArray();
            float rear = pin.Max(i => vertices[i].Z), depth = rear - pin.Min(i => vertices[i].Z);
            var expectedCap = pin.Where(i => vertices[i].Z >= rear - depth * .05f).ToArray();
            var actualCap = pin.Where(i => mounted[i] != vertices[i].Z).ToArray();
            check(actualCap.Length == 21 && actualCap.Order().SequenceEqual(expectedCap.Order()), "only the rear pin cap extends at dark slot " + p.Id);
            check(actualCap.All(i => mounted[i] > -.01928f && mounted[i] < 0), "dark rear pin cap ends inside the frame at slot " + p.Id);
            changed.UnionWith(actualCap);
        }
        check(changed.Count == 147 && Enumerable.Range(0, vertices.Length).Except(changed).All(i => mounted[i] == vertices[i].Z), "dark pin heads, boards and rail keep their native positions");
        check(HookMount.Connect(holder, parts, indices, vertices.Select((p, i) => new HookPoint(p.X, p.Y, mounted[i])).ToArray()).SequenceEqual(mounted), "dark mounting cannot creep over repeated syncs");

        var selected = new[] { "instant-chicken", "instant-egg", "miso-butter-corn", "su-tonkotsu", "kitakata", "hakata", "sapporo", "giro", "iekei" };
        var menu = MenuPlan.Build(selected.Select((id, i) => { var a = arts.Single(a => a.Id == id); return new MenuEntry(i, a.OrderGuid, a.Yen, true); }), arts);
        var seven = MenuPlan.ForCapacity(menu, 7);
        check(seven.Length == 7 && seven.All(p => p.Art.Yen >= 600) && seven.Select(p => p.ButtonId).SequenceEqual(Enumerable.Range(2, 7)), "seven-holder overflow omits the two cheapest dishes and retains machine reading order");
        check(MenuPlan.ForCapacity(menu, 9).Select(p => p.Art.Id).SequenceEqual(menu.Select(p => p.Art.Id)), "nine-holder display keeps every selected dish in the original order");
        var tied = menu.Take(8).ToArray();
        var oneCheap = MenuPlan.ForCapacity(tied, 7);
        check(oneCheap.All(p => p.Art.Id != "instant-chicken") && oneCheap.Any(p => p.Art.Id == "instant-egg"), "price priority changes with the actual selected menu");
        var tiePlan = menu.Where(p => p.Art.Yen >= 600).Concat(menu.Take(1)).ToArray();
        check(MenuPlan.ForCapacity(tiePlan, 7).Select(p => p.ButtonId).SequenceEqual(Enumerable.Range(2, 7)), "price selection is independent of incoming enumeration order");
        var high = arts.Where(a => a.Yen > 600).OrderBy(a => a.Yen).Select((a, i) => new MenuEntry(i, a.OrderGuid, a.Yen, true));
        MenuEntry With(string id, int button) { var a = arts.Single(a => a.Id == id); return new MenuEntry(button, a.OrderGuid, a.Yen, true); }
        var cutoffTie = MenuPlan.Build(high.Concat(new[] { With("instant-chicken", 6), With("su-tonkotsu", 7), With("miso-butter-corn", 8) }), arts);
        var keptTie = MenuPlan.ForCapacity(cutoffTie, 7);
        check(keptTie.Any(p => p.Art.Id == "su-tonkotsu") && !keptTie.Any(p => p.Art.Id == "miso-butter-corn")
            && keptTie.Select(p => p.ButtonId).SequenceEqual(new[] { 0, 1, 2, 3, 4, 5, 7 }), "equal-price cutoff favours the earlier machine button without reordering the retained menu");
        check(MenuPlan.ForCapacity(Array.Empty<PlannedPlaque>(), 7).Length == 0, "empty dark menus leave only frame and pins");
        refuses(() => MenuPlan.ForCapacity(menu, 8), "unreviewed holder capacity is refused");

        var uv = doc.RootElement.GetProperty("UV0").EnumerateArray().Select(p => (U: p[0].GetSingle() * 2048, V: p[1].GetSingle() * 2048)).ToArray();
        check(uv.Any(p => Math.Abs(p.U - DarkPlaqueSurface.Left) < .02f && Math.Abs(p.V - DarkPlaqueSurface.Bottom) < .02f)
            && uv.Any(p => Math.Abs(p.U - DarkPlaqueSurface.Right) < .02f && Math.Abs(p.V - DarkPlaqueSurface.Top) < .02f), "dark material uses actual front-face UV bounds, not a guessed crop");
        using var bright = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests/holder-vertices-fixture.json")));
        var buv = bright.RootElement.GetProperty("UV0").EnumerateArray().Select(p => (U: p[0].GetSingle() * 2048, V: p[1].GetSingle() * 2048)).ToArray();
        var bi = bright.RootElement.GetProperty("Indices").EnumerateArray().Select(p => p.GetInt32()).ToArray();
        var bp = JsonSerializer.Deserialize<HolderParts>(File.ReadAllText(Path.Combine(root, "src/HolderParts.json")), json)!;
        bool Overlaps(int t)
        {
            var p = bi.Skip(t * 3).Take(3).Select(i => buv[i]).ToArray();
            return p.Max(v => v.U) > DarkPlaqueSurface.Left && p.Min(v => v.U) < DarkPlaqueSurface.Right
                && p.Max(v => v.V) > DarkPlaqueSurface.Bottom && p.Min(v => v.V) < DarkPlaqueSurface.Top;
        }
        check(!bp.Plaques.SelectMany(p => p.BoardTriangleOffsets).Any(Overlaps), "dark atlas writes cannot change any face or edge of the bright boards");
    }
}
