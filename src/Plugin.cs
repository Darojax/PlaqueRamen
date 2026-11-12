using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using RSServiceLocator.Scripts;
using Runtime_Classes.Environments;
using Runtime_Classes.Pickups;
using UI.Data_Scripts;
using UI.ViewController_Infos.Scripts;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace PlaqueRamen;

[BepInPlugin(Id, "PlaqueRamen", Version)]
[BepInProcess("Ramen Shop Simulator.exe")]
[BepInIncompatibility("com.darojax.ramenshop.menuplaques")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "com.darojax.ramenshop.plaqueramen", Version = "0.1.7";
    internal static Plugin? Instance;
    private readonly Dictionary<ViewController_Kenbaiki, RuntimeEnvironment_Kenbaiki> _menus = new();
    private readonly Dictionary<MeshFilter, HolderBinding> _holders = new();
    private readonly HashSet<Decoration> _pending = new();
    private readonly DiscoverySchedule _discovery = new();
    private readonly HashSet<int> _rejected = new();
    private Artwork[] _artwork = Array.Empty<Artwork>();
    private HolderDefinition _holder = null!;
    private HolderParts _parts = null!;
    private Harmony? _harmony;
    private PlaqueTextures? _textures;
    private PaperBeerPriceFix? _paperBeer;
    private PlannedPlaque[]? _plan;
    private string _folder = "", _signature = "", _message = "Waiting for the shop menu.";
    private bool _ready, _dirty = true;
    private float _nextMenuCheck;
    private int _imports, _updates, _sceneScans;
    private double _lastImportMs;

    private void Awake()
    {
        Instance = this;
        try
        {
            _folder = Path.Combine(Path.GetDirectoryName(Info.Location)!, "assets");
            Compatibility.Inspect(Path.Combine(Paths.ManagedPath, "Assembly-CSharp.dll"));
            _artwork = JsonConvert.DeserializeObject<Artwork[]>(File.ReadAllText(Path.Combine(_folder, "Artwork.json")))
                ?? throw new InvalidDataException("The plaque catalogue is missing.");
            if (_artwork.Length != 10 || _artwork.Select(a => a.OrderGuid).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 10)
                throw new InvalidDataException("The ten approved dishes must have unique recipe identities.");
            foreach (var art in _artwork)
            {
                if (!Guid.TryParse(art.OrderGuid, out _) || Path.GetFileName(art.File) != art.File || art.Yen <= 0)
                    throw new InvalidDataException("Invalid plaque catalogue entry.");
                using var sha = SHA256.Create(); using var stream = File.OpenRead(Path.Combine(_folder, "art", art.File));
                string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                if (!hash.Equals(art.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Approved artwork changed: " + art.File);
            }
            T Read<T>(string name) where T : class
            {
                using var stream = GetType().Assembly.GetManifestResourceStream("PlaqueRamen." + name);
                using var reader = new StreamReader(stream ?? throw new InvalidDataException("Missing holder definition: " + name));
                return JsonConvert.DeserializeObject<T>(reader.ReadToEnd()) ?? throw new InvalidDataException("Invalid holder definition: " + name);
            }
            _holder = Read<HolderDefinition>("HolderManifest.json"); _parts = Read<HolderParts>("HolderParts.json");
            AutoLayout.RightToLeft(_holder);
            _harmony = new Harmony(Id); _harmony.PatchAll(GetType().Assembly);
            if (_harmony.GetPatchedMethods().Count() != 5) throw new InvalidOperationException("All five menu and decoration observation hooks are required.");
            _paperBeer = new PaperBeerPriceFix(_folder, text => Logger.LogInfo(text), WriteStatus);
            SceneManager.sceneLoaded += SceneLoaded; SceneManager.sceneUnloaded += SceneUnloaded;
            _ready = true; RequestDiscovery();
            Logger.LogInfo("PlaqueRamen ready. Nine-plaque holders follow the ticket machine automatically; no shortcut or aiming is required.");
        }
        catch (Exception ex) { _harmony?.UnpatchSelf(); _message = ex.Message; Logger.LogError("PlaqueRamen inactive: " + ex); }
        WriteStatus();
    }

    internal void Observe(ViewController_Kenbaiki view, DecalProjector[] anchors)
    {
        if (!_ready) return;
        try
        {
            if (anchors == null || anchors.Length != 9 || !anchors[0]) return;
            var machine = anchors[0].GetComponentInParent<RuntimeEnvironment_Kenbaiki>();
            if (!machine || !machine.gameObject.scene.IsValid()) return;
            var face = anchors[0].transform.parent?.parent;
            if (!face || !face.IsChildOf(machine.transform)) throw new InvalidDataException("Could not confirm the ticket-machine face.");
            MachineLayout.Validate(anchors.Select(a =>
            {
                if (!a || a.transform.parent?.parent != face || a.GetComponentInParent<RuntimeEnvironment_Kenbaiki>() != machine)
                    throw new InvalidDataException("Ticket-machine anchors do not belong to one machine.");
                var p = face.InverseTransformPoint(a.transform.position); return new MachineButton(p.x, p.y, p.z);
            }).ToArray());
            if (!_menus.ContainsKey(view)) RequestDiscovery();
            _menus[view] = machine; _dirty = true;
        }
        catch (Exception ex) { Logger.LogWarning("Could not observe the menu: " + ex.Message); }
    }

    internal void Changed(ViewController_Kenbaiki view) { if (_menus.ContainsKey(view)) _dirty = true; }
    internal void QueueDecoration(Decoration decoration) { if (_ready && decoration) _pending.Add(decoration); }
    internal void RequestDiscovery()
    {
        _discovery.Request(Time.realtimeSinceStartup); _paperBeer?.RequestScan(); _dirty = true;
    }

    private void Update()
    {
        if (!_ready) return;
        _paperBeer?.Update();
        if (_discovery.Due(Time.realtimeSinceStartup))
        {
            _sceneScans++;
            foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)) Discover(filter);
        }
        if (_pending.Count > 0)
        {
            foreach (var decoration in _pending.ToArray())
                if (decoration) foreach (var filter in decoration.GetComponentsInChildren<MeshFilter>(true)) Discover(filter);
            _pending.Clear();
        }
        if (!_dirty && Time.realtimeSinceStartup < _nextMenuCheck) return;
        _dirty = false; _nextMenuCheck = Time.realtimeSinceStartup + .5f;
        try { Synchronize(); }
        catch (Exception ex)
        {
            foreach (var holder in _holders.Values) holder.Restore();
            SetMessage("Plaque sync waiting: " + ex.Message);
        }
    }

    private void Discover(MeshFilter filter)
    {
        if (!filter || _holders.ContainsKey(filter) || _rejected.Contains(filter.GetInstanceID()) || !filter.gameObject.scene.IsValid()
            || !filter.gameObject.scene.isLoaded || !HolderBinding.IsPlaced(filter)) return;
        var mesh = filter.sharedMesh;
        if (!mesh || mesh.vertexCount != _holder.VertexCount || mesh.subMeshCount != _holder.SubmeshCount) return;
        var renderer = filter.GetComponent<Renderer>();
        if (!renderer || !renderer.enabled || renderer.forceRenderingOff) return;
        var mats = renderer.sharedMaterials;
        int mainTex = Shader.PropertyToID("_MainTex");
        if (mats.Length != 1 || !mats[0] || !mats[0].HasProperty(mainTex) || !mats[0].GetTexture(mainTex)
            || mats[0].GetTexture(mainTex).name != "T_Tanzaku001_BASE") return;
        try
        {
            _holders.Add(filter, new HolderBinding(filter, renderer, _holder, _parts));
            _dirty = true; Logger.LogInfo("Automatically found Menu Board Shelf: " + filter.name);
        }
        catch (Exception ex) { _rejected.Add(filter.GetInstanceID()); Logger.LogWarning("Unchanged holder skipped: " + ex.Message); }
    }

    private void Synchronize()
    {
        foreach (var key in _holders.Where(p => !p.Value.Alive).Select(p => p.Key).ToArray())
        { _holders[key].Dispose(); _holders.Remove(key); }
        foreach (var view in _menus.Where(p => !p.Key || !p.Value || !p.Value.gameObject.scene.IsValid()).Select(p => p.Key).ToArray()) _menus.Remove(view);
        if (_menus.Count != 1)
        {
            foreach (var holder in _holders.Values) holder.Restore();
            _plan = null; _signature = ""; SetMessage("Waiting for one initialized ticket machine."); return;
        }
        var menu = _menus.Single().Key;
        var entries = menu.SelectedRecipeIndexDict.Select(p => new MenuEntry(p.Key, p.Value ? p.Value.GUID.ToString() : "", p.Value ? p.Value.price : 0, p.Value is RamenOrderData)).ToArray();
        string signature = string.Join("|", entries.OrderBy(e => e.ButtonId).Select(e => e.ButtonId + ":" + e.Guid + ":" + e.Yen + ":" + e.Ramen));
        if (_plan == null || signature != _signature) { _plan = MenuPlan.Build(entries, _artwork); _signature = signature; }
        if (_holders.Count == 0) { SetMessage("Waiting for a nine-plaque Menu Board Shelf."); return; }
        _textures ??= new PlaqueTextures(_folder);
        var missing = _plan.FirstOrDefault(p => !_textures.Has(p.Art));
        if (missing != null)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew(); _textures.Prepare(missing.Art); timer.Stop();
            _lastImportMs = timer.Elapsed.TotalMilliseconds; _imports++; _dirty = true;
            SetMessage("Preparing plaque artwork: " + missing.Art.Dish); return;
        }
        int changed = 0;
        foreach (var holder in _holders.Values) if (holder.Apply(_plan, signature, _textures)) changed++;
        if (changed > 0) _updates++;
        SetMessage("Automatically displaying " + _plan.Length + " ramen dishes on " + _holders.Values.Count(h => h.Applied) + " holder(s); all nine hooks remain fixed.", changed > 0);
    }

    private void SetMessage(string message, bool refresh = false)
    {
        if (_message == message && !refresh) return;
        _message = message; Logger.LogInfo(message); WriteStatus();
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode) => RequestDiscovery();
    private void SceneUnloaded(Scene scene)
    {
        foreach (var key in _holders.Where(p => !p.Value.Alive || p.Key.gameObject.scene == scene).Select(p => p.Key).ToArray())
        { _holders[key].Dispose(); _holders.Remove(key); }
        foreach (var view in _menus.Where(p => !p.Key || !p.Value || p.Value.gameObject.scene == scene).Select(p => p.Key).ToArray()) _menus.Remove(view);
        _pending.RemoveWhere(d => !d || d.gameObject.scene == scene);
        _paperBeer?.SceneUnloaded(); _plan = null; _signature = ""; _dirty = true; WriteStatus();
    }

    private void WriteStatus()
    {
        try
        {
            var displayed = _plan?.Select((p, i) => new { rightToLeftRank = i + 1, machineButton = p.ButtonId + 1, dish = p.Art.Dish, yen = p.Art.Yen }).ToArray();
            string text = JsonConvert.SerializeObject(new { version = Version, utc = DateTime.UtcNow.ToString("O"), ready = _ready,
                automatic = true, observedMachines = _menus.Count, trackedHolders = _holders.Count, appliedHolders = _holders.Values.Count(h => h.Applied),
                message = _message, syncUpdates = _updates, importedTextures = _imports, lastImportMs = _lastImportMs, sceneScans = _sceneScans,
                hooksPerHolder = 9, displayed, holders = _holders.Values.Select(h => h.Status).ToArray(),
                paperBeer = new { appliedSlots = _paperBeer?.Applied ?? 0, readbacks = _paperBeer?.Readbacks ?? 0,
                    sourceMatchError = _paperBeer?.MatchError ?? 0, message = _paperBeer?.Message } }, Formatting.Indented);
            File.WriteAllText(Path.Combine(Paths.ConfigPath, Id + ".status.json"), text);
        }
        catch (Exception ex) { Logger.LogWarning("Could not write plaque status: " + ex.Message); }
    }

    private void OnDestroy()
    {
        _ready = false; SceneManager.sceneLoaded -= SceneLoaded; SceneManager.sceneUnloaded -= SceneUnloaded; _harmony?.UnpatchSelf();
        foreach (var holder in _holders.Values) holder.Dispose();
        _holders.Clear(); _textures?.Dispose(); _paperBeer?.Dispose(); Instance = null;
    }
}

[HarmonyPatch(typeof(ViewController_Kenbaiki), "InitButtonsGrid", new[] { typeof(DecalProjector[]), typeof(bool) })]
internal static class ObserveMenuLoad
{
    private static void Postfix(ViewController_Kenbaiki __instance, DecalProjector[] __0) => Plugin.Instance?.Observe(__instance, __0);
}
[HarmonyPatch(typeof(ViewController_Kenbaiki), "SetDecalRecipe", new[] { typeof(int), typeof(int) })]
internal static class ObserveMenuAdd
{
    private static void Postfix(ViewController_Kenbaiki __instance) => Plugin.Instance?.Changed(__instance);
}
[HarmonyPatch(typeof(ViewController_Kenbaiki), "ClearRecipeButton", new Type[] { })]
internal static class ObserveMenuRemove
{
    private static void Postfix(ViewController_Kenbaiki __instance) => Plugin.Instance?.Changed(__instance);
}
[HarmonyPatch(typeof(Decoration), "Start", new Type[] { })]
internal static class ObserveDecorationLoad
{
    private static void Postfix(Decoration __instance) => Plugin.Instance?.QueueDecoration(__instance);
}
[HarmonyPatch(typeof(DecorationService), "PutDownDeco", new Type[] { })]
internal static class ObserveDecorationPlacement
{
    private static void Postfix() => Plugin.Instance?.RequestDiscovery();
}
