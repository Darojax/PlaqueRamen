using PlaqueRamen;

using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;
using System.Buffers.Binary;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
string game = args.Length > 0 ? args[0] : @"D:\SteamLibrary\steamapps\common\Ramen Shop Simulator";
var json = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true };
int count = 0;
void Check(bool ok, string reason) { if (!ok) throw new Exception("FAILED: " + reason); count++; }
void Refuses(Action action, string reason)
{
    bool refused = false; try { action(); } catch (InvalidDataException) { refused = true; }
    Check(refused, reason);
}

var arts = JsonSerializer.Deserialize<Artwork[]>(File.ReadAllText(Path.Combine(root, "assets/Artwork.json")), json)!;
Check(arts.Length == 10 && arts.Select(a => a.Id).Distinct().Count() == 10 && arts.Select(a => a.OrderGuid).Distinct().Count() == 10, "ten distinct artwork/recipe pairs");
foreach (var art in arts)
{
    Check(Guid.TryParse(art.OrderGuid, out _), "recipe GUID: " + art.Dish);
    var path = Path.Combine(root, "assets/art", art.File);
    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).Equals(art.Sha256, StringComparison.OrdinalIgnoreCase), "approved art hash: " + art.Dish);
    var png = File.ReadAllBytes(path);
    int height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
    var face = new FaceLayout(height, art.InkTop, art.InkBottom);
    Check(Math.Abs(face.SourceV(FaceLayout.BottomMargin) - face.SourceLow) < .000001f
        && Math.Abs(face.SourceV(1 - FaceLayout.TopMargin) - face.SourceHigh) < .000001f, "lettering and price fit the safe top/bottom margins: " + art.Dish);
    Check(face.SourceV(0) == 0 && face.SourceV(1) == 1, "full original grain height retained: " + art.Dish);
    var samples = Enumerable.Range(0, 1001).Select(i => face.SourceV(i / 1000f)).ToArray();
    Check(samples.Zip(samples.Skip(1)).All(p => p.Second > p.First) && samples.All(v => v >= 0 && v <= 1), "grain mapping is continuous with no flat fill or repeated edge bands: " + art.Dish);
}
Refuses(() => new FaceLayout(2169, 0, 0), "missing lettering measurements refused");
Refuses(() => new FaceLayout(2169, 2030, 400), "reversed lettering measurements refused");
Check(PlaqueSurface.Kind(new Pixel(75, 48, 32)) == PaintKind.Wood, "brown grain knots are not mistaken for black lettering");
Check(PlaqueSurface.Kind(new Pixel(35, 36, 33)) == PaintKind.Black && PlaqueSurface.Kind(new Pixel(132, 56, 57)) == PaintKind.Red,
    "native black and red paint are identified separately from wood");
var sampleWood = Enumerable.Range(0, 100).Select(i => new Pixel((byte)(160 + i / 4), (byte)(100 + i / 5), (byte)(50 + i / 6))).ToArray();
var targetWood = Enumerable.Range(0, 100).Select(i => new Pixel((byte)(130 + i / 3), (byte)(90 + i / 4), (byte)(70 + i / 5))).ToArray();
var targetStats = WoodStatistics.Measure(targetWood);
var woodGrade = new WoodGrade(WoodStatistics.Measure(sampleWood), targetStats);
var graded = sampleWood.Select(woodGrade.Apply).ToArray(); var gradedStats = WoodStatistics.Measure(graded);
Check(Enumerable.Range(0, 3).All(k => Math.Abs(gradedStats.Mean[k] - targetStats.Mean[k]) < 1), "material matching reaches the native wood colour");
Check(Enumerable.Range(0, 3).All(k => Math.Abs(gradedStats.Deviation[k] - targetStats.Deviation[k]) < 1), "material matching retains native grain contrast");
Check(graded.Zip(graded.Skip(1)).All(p => p.First.R <= p.Second.R && p.First.G <= p.Second.G && p.First.B <= p.Second.B),
    "material matching preserves the spatial order of grain tones");
Check(graded.Select(p => p.R).Distinct().Count() >= sampleWood.Select(p => p.R).Distinct().Count(), "material matching does not flatten this grain pattern");
var egg = arts.Single(a => a.Id == "instant-egg");
Check(egg.Dish == "Egg Instant" && egg.Yen == 550, "agreed Egg Instant name and price");
Check(arts.Single(a => a.Id == "hakata").Yen == 1000, "actual Hakata recipe price, not decorative vanilla price");
MenuEntry Entry(int slot, int art) => new(slot, arts[art].OrderGuid, arts[art].Yen, true);
var unordered = new[] { Entry(8, 8), Entry(3, 3), Entry(1, 1), Entry(0, 0), Entry(2, 2), Entry(7, 7), Entry(4, 4), Entry(6, 6), Entry(5, 5) };
var full = MenuPlan.Build(unordered, arts);
Check(full.Select(p => p.ButtonId).SequenceEqual(Enumerable.Range(0, 9)), "machine reading order is across the top row, then middle, then bottom");
Check(full[1].Art == arts[1] && full[3].Art == arts[3], "top-row neighbor precedes the item below");
var mixed = new[] { Entry(7, 3), new MenuEntry(0, "drink", 450, false), Entry(2, 1), Entry(4, 2) };
var plan = MenuPlan.Build(mixed, arts);
Check(plan.Select(p => p.ButtonId).SequenceEqual(new[] { 2, 4, 7 }), "empty cells and drinks are skipped without reordering dishes");
Check(MenuPlan.Build(new[] { Entry(7, 0), Entry(1, 0), Entry(3, 2) }, arts).Select(p => p.ButtonId).SequenceEqual(new[] { 1, 3 }), "duplicate recipes occupy one plaque, at their first selected slot");
Check(MenuPlan.Build(Array.Empty<MenuEntry>(), arts).Length == 0, "empty menu has no food plaques");
Check(MenuPlan.Build(new[] { new MenuEntry(4, "any drink", 550, false) }, arts).Length == 0, "drink-only menu has no food plaques");
Check(MenuPlan.Build(new[] { new MenuEntry(0, arts[0].OrderGuid.ToUpperInvariant(), arts[0].Yen, true) }, arts)[0].Art == arts[0], "recipe GUID matching is case insensitive");
Refuses(() => MenuPlan.Build(new[] { Entry(9, 0) }, arts), "unreviewed tenth ticket button refused");
Refuses(() => MenuPlan.Build(new[] { Entry(-1, 0) }, arts), "negative button refused");
Refuses(() => MenuPlan.Build(new[] { Entry(0, 0), Entry(0, 1) }, arts), "duplicate button identity refused");
Refuses(() => MenuPlan.Build(new[] { new MenuEntry(0, Guid.NewGuid().ToString(), 450, true) }, arts), "new recipe without approved artwork refused");
Refuses(() => MenuPlan.Build(new[] { new MenuEntry(0, arts[0].OrderGuid, arts[0].Yen + 50, true) }, arts), "changed price cannot produce a misleading plaque");
Refuses(() => MenuPlan.Build(new[] { Entry(0, 0) }, arts.Concat(new[] { arts[0] }).ToArray()), "ambiguous artwork refused");
Check(MenuPlan.Build(new[] { Entry(2, 5), Entry(0, 4) }, arts).Select(p => p.Art.Id).SequenceEqual(new[] { arts[4].Id, arts[5].Id }), "replacement menu uses selected recipes");
Check(MenuPlan.Build(new[] { Entry(2, 5) }, arts).Length == 1, "clearing a selected button removes its plaque");

using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests/holder-fixture.json")));
var holder = JsonSerializer.Deserialize<HolderDefinition>(fixture.RootElement.GetProperty("Manifest").GetRawText(), json)!;
var source = fixture.RootElement.GetProperty("Indices").EnumerateArray().Select(n => n.GetInt32()).ToArray();
var original = (int[])source.Clone();
HolderGeometry.Validate(holder, 872, 1, source);
Check(source.Length == 970 * 3, "reviewed complete holder geometry");
var points = holder.Plaques.Select(p => new PlaquePoint(p.Id, (p.Min[0] + p.Max[0]) / 2, (p.Min[1] + p.Max[1]) / 2, (p.Min[2] + p.Max[2]) / 2)).ToArray();
var rtl = RowSelection.Select(points, 5, 1, 0).AsEnumerable().Reverse().ToArray();
Check(rtl.SequenceEqual(Enumerable.Range(1, 9).Reverse()), "first dish occupies rightmost board from the viewing side");
var rotated = points.Select(p => new PlaquePoint(p.Id, 4 + p.X * .6f, p.Y, -2 + p.X * .8f)).ToArray();
Check(RowSelection.Select(rotated, 3, .6f, .8f).AsEnumerable().Reverse().SequenceEqual(rtl), "rotated holder preserves right-to-left rank");
Check(RowSelection.Select(points, 3, -1, 0).AsEnumerable().Reverse().SequenceEqual(rtl.Reverse()), "viewing side reverses physical IDs correctly");
var occupied = holder.Plaques.SelectMany(p => p.TriangleOffsets).ToHashSet();
var rail = Enumerable.Range(0, 970).Where(t => !occupied.Contains(t)).SelectMany(t => source.Skip(t * 3).Take(3)).ToArray();
var parts = JsonSerializer.Deserialize<HolderParts>(File.ReadAllText(Path.Combine(root, "src/HolderParts.json")), json)!;
parts.Validate(holder, source);
var woodTriangles = parts.Plaques.SelectMany(p => p.BoardTriangleOffsets).ToHashSet();
var frameTriangles = Enumerable.Range(0, 970).Where(t => !woodTriangles.Contains(t)).ToHashSet();
var fixedFrame = Enumerable.Range(0, 970).Where(frameTriangles.Contains).SelectMany(t => source.Skip(t * 3).Take(3)).ToArray();
Check(rail.Length == 16 * 3 && fixedFrame.Length == (16 + 9 * 86) * 3, "fixed frame contains the rail and all nine complete metal hooks");
Check(parts.Plaques.All(p => p.HookTriangleOffsets.All(frameTriangles.Contains)), "every hook is in the permanent original-material group");
for (int visible = 0; visible <= 9; visible++)
{
    var groups = MenuPlan.Submeshes(source, holder, parts, rtl, visible);
    var selected = MenuPlan.CenteredPlaques(rtl, visible);
    Check(groups.Length == 10 && groups[0].SequenceEqual(fixedFrame), "all hooks and rail stay identical for visible count " + visible);
    for (int i = 0; i < 9; i++)
    {
        var expected = i < visible ? parts.Plaques.Single(p => p.Id == selected[i]).BoardTriangleOffsets.SelectMany(t => source.Skip(t * 3).Take(3)).ToArray() : Array.Empty<int>();
        Check(groups[i + 1].SequenceEqual(expected), "only wooden board geometry at rank " + i + " count " + visible);
    }
    Check(groups.Sum(g => g.Length) == (790 + 20 * visible) * 3, "only unused wood is hidden; nine hooks remain for count " + visible);
}
Check(MenuPlan.CenteredPlaques(rtl, 1).SequenceEqual(new[] { 5 }), "one dish selects the middle board");
Check(MenuPlan.CenteredPlaques(rtl, 3).SequenceEqual(new[] { 6, 5, 4 }), "three dishes occupy the central boards in right-to-left order");
Check(MenuPlan.CenteredPlaques(rtl, 2).SequenceEqual(new[] { 6, 5 }), "two dishes use the middle and right-adjacent hooks");
Check(MenuPlan.CenteredPlaques(rtl, 4).SequenceEqual(new[] { 7, 6, 5, 4 }), "four dishes have their extra board on the right");
Check(MenuPlan.CenteredPlaques(rtl, 6).SequenceEqual(new[] { 8, 7, 6, 5, 4, 3 }), "six dishes form one continuous group leaning right");
Check(MenuPlan.CenteredPlaques(rtl, 8).SequenceEqual(new[] { 9, 8, 7, 6, 5, 4, 3, 2 }), "eight dishes leave the leftmost hook vacant");
using var geometry = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests/holder-vertices-fixture.json")));
var originalX = geometry.RootElement.GetProperty("Vertices").EnumerateArray().Select(v => v[0].GetSingle()).ToArray();
Check(geometry.RootElement.GetProperty("Indices").EnumerateArray().Select(v => v.GetInt32()).SequenceEqual(source), "position fixture belongs to the reviewed holder index layout");
var fixedVertices = fixedFrame.Distinct().ToHashSet();
var woodVertices = parts.Plaques.SelectMany(p => p.BoardTriangleOffsets).SelectMany(t => source.Skip(t * 3).Take(3)).Distinct().ToHashSet();
Check(fixedVertices.Count == 26 + 9 * 62 && woodVertices.Count == 9 * 32 && !fixedVertices.Overlaps(woodVertices), "fixed hooks/rail and wooden boards have entirely separate vertices");
Check(fixedVertices.Concat(woodVertices).Distinct().Count() == originalX.Length, "all original vertices are accounted for by wood, hooks and rail");
foreach (var direction in new[] { rtl, rtl.Reverse().ToArray() })
    for (int visible = 0; visible <= 9; visible++)
    {
        var selected = MenuPlan.CenteredPlaques(direction, visible);
        var groups = MenuPlan.Submeshes(source, holder, parts, direction, visible);
        Check(groups[0].SequenceEqual(fixedFrame), "fixed native hooks are unchanged from either viewing side, count " + visible);
        var ranks = selected.Select(id => Array.IndexOf(direction, id)).ToArray();
        Check(ranks.Zip(ranks.Skip(1)).All(p => p.Second - p.First == 1), "occupied hooks form one continuous group for count " + visible);
        if (visible == 0) { Check(groups.Skip(1).All(g => g.Length == 0), "empty menu leaves only the frame and nine hooks"); continue; }
        Check(ranks.Contains(4), "the middle hook stays occupied for count " + visible);
        int right = ranks.Count(rank => rank < 4), left = ranks.Count(rank => rank > 4);
        Check(visible % 2 == 0 ? right == left + 1 : right == left, "even groups lean right and odd groups balance for count " + visible);
        Check(groups.Skip(1).SelectMany(g => g).All(woodVertices.Contains), "all menu slots reference only original wooden vertices, count " + visible);
    }
var badParts = JsonSerializer.Deserialize<HolderParts>(JsonSerializer.Serialize(parts, json), json)!;
badParts.Plaques[0].BoardTriangleOffsets[0] = badParts.Plaques[0].HookTriangleOffsets[0];
Refuses(() => badParts.Validate(holder, source), "a hook triangle cannot be misclassified as a wooden board");
var nativePoints = geometry.RootElement.GetProperty("Vertices").EnumerateArray()
    .Select(p => new HookPoint(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle())).ToArray();
var mountedZ = HookMount.Connect(holder, parts, source, nativePoints);
var railVertices = rail.Distinct().ToHashSet();
float railFront = railVertices.Min(i => nativePoints[i].Z), railBack = railVertices.Max(i => nativePoints[i].Z);
var extended = new HashSet<int>();
foreach (var part in parts.Plaques)
{
    var hook = part.HookTriangleOffsets.SelectMany(t => source.Skip(t * 3).Take(3)).Distinct().ToArray();
    float nativeRear = hook.Max(i => nativePoints[i].Z);
    var changedMount = hook.Where(i => mountedZ[i] != nativePoints[i].Z).ToArray();
    Check(nativeRear < railFront, "fixture reproduces the original floating-hook gap at slot " + part.Id);
    Check(changedMount.Length == 9 && changedMount.All(i => nativePoints[i].Z > nativeRear - .001f), "only the rear terminal ring/cap is extended at slot " + part.Id);
    Check(changedMount.All(i => mountedZ[i] > railFront && mountedZ[i] < railBack), "rear mounting cap ends inside the frame at slot " + part.Id);
    Check(changedMount.All(i => mountedZ[i] > nativePoints[i].Z), "mount extends towards the frame at slot " + part.Id);
    Check(hook.Except(changedMount).All(i => mountedZ[i] == nativePoints[i].Z), "front bend and plaque contact point stay native at slot " + part.Id);
    extended.UnionWith(changedMount);
}
Check(extended.Count == 81, "all nine rear mounts close their gaps");
Check(woodVertices.Concat(railVertices).All(i => mountedZ[i] == nativePoints[i].Z), "all wooden boards and the frame retain their exact original depth");
Check(Enumerable.Range(0, nativePoints.Length).Except(extended).All(i => mountedZ[i] == nativePoints[i].Z), "every vertex outside the mounting caps remains unchanged");
var connectedPoints = nativePoints.Select((p, i) => new HookPoint(p.X, p.Y, mountedZ[i])).ToArray();
Check(HookMount.Connect(holder, parts, source, connectedPoints).SequenceEqual(mountedZ), "mounting is idempotent and cannot creep backwards across updates");
var missingFrame = nativePoints.Select((p, i) => railVertices.Contains(i) ? new HookPoint(p.X + 10, p.Y, p.Z) : p).ToArray();
Refuses(() => HookMount.Connect(holder, parts, source, missingFrame), "a frame outside the mounting shafts is refused");
var distantFrame = nativePoints.Select((p, i) => railVertices.Contains(i) ? new HookPoint(p.X, p.Y, p.Z + 1) : p).ToArray();
Refuses(() => HookMount.Connect(holder, parts, source, distantFrame), "an unreasonably large mount extension is refused");
var uvs = geometry.RootElement.GetProperty("UV0").EnumerateArray().Select(p => (U: p[0].GetSingle() * 2048, V: p[1].GetSingle() * 2048))
    .Where(p => p.U >= 1211 && p.U <= 1398 && p.V >= 950 && p.V <= 1780).ToArray();
Check(Math.Abs(uvs.Min(p => p.V) - FaceLayout.FaceBottom) < .02f && Math.Abs(uvs.Max(p => p.V) - FaceLayout.FaceTop) < .02f,
    "text placement uses the actual front face UV limits, excluding top/bottom edge areas");
Check(source.SequenceEqual(original), "shared source index buffer is never modified");
Refuses(() => MenuPlan.Submeshes(source, holder, parts, new[] { 1, 1, 2, 3, 4, 5, 6, 7, 8 }, 3), "duplicate physical boards refused");
Refuses(() => MenuPlan.Submeshes(source, holder, parts, rtl, 10), "more than nine plaques refused");
Refuses(() => HolderGeometry.Validate(holder, 94, 1, source), "standalone plaque cannot be modified as a holder");
var changed = (int[])source.Clone(); changed[0]++;
Refuses(() => HolderGeometry.Validate(holder, 872, 1, changed), "changed mesh index data refused");

using var physical = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests/machine-fixture.json")));
var buttons = physical.RootElement.EnumerateArray().Select(b => new MachineButton(b.GetProperty("x").GetSingle(), b.GetProperty("y").GetSingle(), b.GetProperty("z").GetSingle())).ToArray();
MachineLayout.Validate(buttons); Check(true, "actual nine physical button anchors match native row-major IDs");
Refuses(() => MachineLayout.Validate(buttons.Reverse().ToArray()), "right-to-left machine array refused");
Refuses(() => MachineLayout.Validate(Enumerable.Range(0, 9).Select(i => buttons[i % 3 * 3 + i / 3]).ToArray()), "column-major machine array refused");
Refuses(() => MachineLayout.Validate(buttons.Take(8).ToArray()), "incomplete physical machine refused");

string assembly = Path.Combine(game, "Ramen Shop Simulator_Data/Managed/Assembly-CSharp.dll");
Compatibility.Inspect(assembly); Check(true, "current native menu API contract verified");
string scratch = Path.Combine(Path.GetTempPath(), "MenuPlaqueChecks-" + Guid.NewGuid().ToString("N") + ".dll");
try
{
    using var native = AssemblyDefinition.ReadAssembly(assembly);
    native.MainModule.Mvid = Guid.NewGuid(); native.Name.Version = new Version(99, 1, 0, 0); native.Write(scratch);
    Compatibility.Inspect(scratch); Check(true, "unrelated build identity/version changes do not disable PlaqueRamen");
    var decorationStart = native.MainModule.GetType("Runtime_Classes.Pickups.Decoration").Methods.Single(m => m.Name == "Start");
    decorationStart.Name = "RenamedStart"; native.Write(scratch);
    Refuses(() => Compatibility.Inspect(scratch), "changed automatic decoration-load hook disables activation");
    decorationStart.Name = "Start";
    var heldState = native.MainModule.GetType("Runtime_Classes.Common.RuntimeInteractable").Methods.Single(m => m.Name == "get_IsHeld");
    heldState.ReturnType = native.MainModule.TypeSystem.Single; native.Write(scratch);
    Refuses(() => Compatibility.Inspect(scratch), "changed held-decoration state API disables activation");
    heldState.ReturnType = native.MainModule.TypeSystem.Boolean;
    native.MainModule.GetType("UI.Data_Scripts.OrderData").Fields.Single(f => f.Name == "price").FieldType = native.MainModule.TypeSystem.Single;
    native.Write(scratch);
    Refuses(() => Compatibility.Inspect(scratch), "changed required price API disables activation");
}
finally { if (File.Exists(scratch)) File.Delete(scratch); }

using var dll = AssemblyDefinition.ReadAssembly(Path.Combine(root, "src/bin/Release/netstandard2.1/PlaqueRamen.dll"));
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts) => ts.SelectMany(t => new[] { t }.Concat(Types(t.NestedTypes)));
var instructions = Types(dll.MainModule.Types).SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
var calls = instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
Check(Types(dll.MainModule.Types).Count(t => t.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch")) == 5, "three menu and two decoration observation hooks");
Check(!calls.Any(m => m.DeclaringType.FullName == "ES3" && m.Name.StartsWith("Save")), "no game save writes");
Check(!instructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.DeclaringType.Scope.Name == "Assembly-CSharp"), "no native recipe, price or menu field writes");
Check(!calls.Any(m => m.DeclaringType.FullName == "UnityEngine.Material" && m.Name is "SetTexture" or "set_mainTexture" or ".ctor"), "no shared material replacement or texture mutation");
Check(calls.Any(m => m.DeclaringType.FullName == "UnityEngine.Renderer" && m.Name == "SetPropertyBlock"), "per-slot texture overrides");
Check(calls.Any(m => m.DeclaringType.FullName == "UnityEngine.Object" && m.Name == "Instantiate"), "private clone of original mesh");
Check(calls.Count(m => m.DeclaringType.FullName == "UnityEngine.Mesh" && m.Name == "set_vertices") == 1,
    "private mesh has one controlled vertex update for the rear mount extension");
var syncBody = Types(dll.MainModule.Types).Single(t => t.FullName == "PlaqueRamen.HolderBinding").Methods.Single(m => m.Name == "Apply").Body.Instructions;
Check(!syncBody.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.DeclaringType.FullName == "UnityEngine.Vector3" && f.Name is "x" or "y"),
    "runtime never changes sideways or vertical hook/board coordinates");
Check(!Types(dll.MainModule.Types).Any(t => t.Name == "HolderPlacement"), "continuous translation of boards/hooks has been retired");
Check(!calls.Any(m => m.DeclaringType.FullName == "UnityEngine.GameObject" && m.Name == "SetActive"), "no saved game object activation changes");
Check(!calls.Any(m => m.DeclaringType.FullName == "UnityEngine.Time" && m.Name.StartsWith("set_")), "no global timing changes");
Check(!calls.Any(m => m.Name is "get_f9Key" or "get_f10Key"), "RTSS hotkeys avoided");
Check(!Types(dll.MainModule.Types).SelectMany(t => t.Methods).Any(m => m.Name == "OnGUI"), "no permanent development overlay");
Check(dll.MainModule.Resources.Any(r => r.Name == "PlaqueRamen.HolderManifest.json"), "reviewed holder partition embedded");
Check(dll.MainModule.Resources.Any(r => r.Name == "PlaqueRamen.HolderParts.json"), "reviewed hook/wood separation embedded");
Check(!dll.MainModule.AssemblyReferences.Any(a => a.Name == "Unity.InputSystem")
    && !calls.Any(m => m.DeclaringType.FullName.StartsWith("UnityEngine.InputSystem.") || m.Name is "ViewportPointToRay" or "get_main" && m.DeclaringType.FullName == "UnityEngine.Camera"),
    "automatic loading has no keyboard, crosshair, or camera dependency");
var pluginType = Types(dll.MainModule.Types).Single(t => t.FullName == "PlaqueRamen.Plugin");
Check(pluginType.Methods.Single(m => m.Name == "SceneLoaded").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "RequestDiscovery"),
    "every loaded scene schedules automatic holder discovery");
Check(Types(dll.MainModule.Types).Single(t => t.Name == "ObserveDecorationPlacement").Methods.Single(m => m.Name == "Postfix")
    .Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "RequestDiscovery"), "placing a decoration schedules automatic discovery");
Check(Types(dll.MainModule.Types).Single(t => t.Name == "ObserveDecorationLoad").Methods.Single(m => m.Name == "Postfix")
    .Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "QueueDecoration"), "decorations spawned after scene load are observed");
var paperType = Types(dll.MainModule.Types).Single(t => t.Name == "PaperBeerPriceFix");
foreach (var pair in new[] { ("OriginalSha", "price-70.original.png"), ("ReplacementSha", "price-55.generated.png") })
    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "assets/paper-beer", pair.Item2)))) == (string)paperType.Fields.Single(f => f.Name == pair.Item1).Constant,
        "paper price artwork matches the runtime hash: " + pair.Item2);
AutomaticChecks.Run(holder, Check, Refuses);
SevenPlaqueChecks.Run(root, arts, Check, Refuses);

PaperPriceChecks.Run(Check, Refuses);
if (args.Contains("--render-dark")) DarkPreview.Run(root, arts, Check);

Console.WriteLine($"{count} PlaqueRamen checks passed.");
