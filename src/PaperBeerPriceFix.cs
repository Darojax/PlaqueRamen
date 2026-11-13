using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

using UnityEngine;
using Runtime_Classes.Pickups;

namespace PlaqueRamen;

internal sealed class PaperBeerPriceFix : IDisposable
{
    internal const string OriginalSha = "F219E5EF31754A4D8BF9B5DCD16F2938FEE4456DEEE8877907A963A4F9EA3856";
    internal const string ReplacementSha = "6C6D61436A966089A396599ACD165FE78ABF7239D1B30796D72E878A86F24661";
    private static readonly int MainTex = Shader.PropertyToID("_MainTex");
    private readonly string _folder;
    private readonly Action<string> _log;
    private readonly Action _changed;
    private readonly Queue<float> _scans = new();
    private readonly Dictionary<Texture2D, Texture2D> _copies = new();
    private readonly HashSet<int> _rejected = new();
    private readonly List<Override> _overrides = new();
    private Pixel[]? _expected, _replacement;
    private int _width, _height;
    internal int Applied => _overrides.Count(o => o.Renderer);
    internal int Readbacks { get; private set; }
    internal double MatchError { get; private set; }
    internal string Message { get; private set; } = "Waiting for a Horizon draft beer paper sign.";

    private sealed class Override
    {
        internal Renderer Renderer = null!;
        internal int Slot;
        internal MaterialPropertyBlock Previous = null!;
        internal Texture2D Corrected = null!;
    }

    internal PaperBeerPriceFix(string folder, Action<string> log, Action changed)
    { _folder = Path.Combine(folder, "paper-beer"); _log = log; _changed = changed; RequestScan(); }

    // Bounded startup/load passes catch decorations spawned while loading a save.
    // No continuous renderer search, texture writes or GPU readback loop.
    internal void RequestScan()
    {
        _scans.Clear();
        foreach (float delay in new[] { .5f, 2f, 8f, 20f }) _scans.Enqueue(Time.realtimeSinceStartup + delay);
    }

    internal void Update()
    {
        if (_scans.Count == 0 || Time.realtimeSinceStartup < _scans.Peek()) return;
        _scans.Dequeue();
        try { Scan(); }
        catch (Exception ex) { Message = "Paper price correction skipped: " + ex.Message; _log(Message); }
        _changed();
    }

    internal void Observe(Decoration decoration)
    {
        if(!decoration)return;
        int before=_overrides.Count;
        try{Scan(decoration.GetComponentsInChildren<Renderer>(true));}
        catch(Exception ex){Message="Paper price correction skipped: "+ex.Message;_log(Message);}
        if(_overrides.Count!=before)_changed();
    }

    private void Scan(IEnumerable<Renderer>? selection = null)
    {
        _overrides.RemoveAll(o => !o.Renderer);
        foreach (var renderer in selection??UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!renderer || !renderer.gameObject.scene.IsValid() || !renderer.gameObject.scene.isLoaded) continue;
            var materials = renderer.sharedMaterials;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                if (_overrides.Any(o => o.Renderer == renderer && o.Slot == slot)) continue;
                var material = materials[slot];
                if (!material || !material.HasProperty(MainTex) || material.GetTexture(MainTex) is not Texture2D source
                    || source.name != PaperPricePatch.Atlas || source.width != PaperPricePatch.Size || source.height != PaperPricePatch.Size) continue;
                var previous = new MaterialPropertyBlock(); renderer.GetPropertyBlock(previous, slot);
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, slot);
                if (block.isEmpty) renderer.GetPropertyBlock(block);
                var overrideTexture = block.GetTexture(MainTex);
                if (overrideTexture && overrideTexture != source) continue;
                if (_rejected.Contains(source.GetInstanceID())) continue;
                if (!_copies.TryGetValue(source, out var corrected))
                {
                    try { corrected = CopyAndCorrect(source); _copies.Add(source, corrected); }
                    catch (Exception ex)
                    {
                        _rejected.Add(source.GetInstanceID()); Message = "Paper price correction skipped: " + ex.Message; _log(Message); continue;
                    }
                }
                block.SetTexture(MainTex, corrected); renderer.SetPropertyBlock(block, slot);
                _overrides.Add(new Override { Renderer = renderer, Slot = slot, Previous = previous, Corrected = corrected });
                Message = "Horizon draft paper sign: 550 yen. Corrected material slots: " + Applied + ".";
                _log(Message);
            }
        }
    }

    private Texture2D CopyAndCorrect(Texture2D source)
    {
        if (_expected == null)
        {
            var original = Load("price-70.original.png", OriginalSha);
            var replacement = Load("price-55.generated.png", ReplacementSha);
            if (original.Width != PaperPricePatch.Width || original.Height != PaperPricePatch.Height)
                throw new InvalidDataException("Original paper price crop dimensions changed.");
            _expected = original.Pixels; _replacement = replacement.Pixels; _width = replacement.Width; _height = replacement.Height;
        }

        // The native atlas is not CPU-readable. Copy it once, keeping sRGB and
        // restoring all render state even on failure. The source asset is untouched.
        var priorTarget = RenderTexture.active; bool priorSrgb = GL.sRGBWrite;
        var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Texture2D? copy = null;
        try
        {
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Graphics.Blit(source, target); RenderTexture.active = target;
            copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, false);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false); Readbacks++;
            var native = copy.GetPixels32().Select(p => new Pixel(p.r, p.g, p.b, p.a)).ToArray();
            MatchError = PaperPricePatch.MatchError(native, _expected);
            var patched = PaperPricePatch.Compose(native, _expected, _replacement!, _width, _height);
            copy.name = "MenuPlaqueSync_HorizonDraft550"; copy.filterMode = source.filterMode; copy.anisoLevel = source.anisoLevel;
            copy.wrapModeU = source.wrapModeU; copy.wrapModeV = source.wrapModeV;
            copy.SetPixels32(patched.Select(p => new Color32(p.R, p.G, p.B, p.A)).ToArray()); copy.Apply(true, true);
            var finished = copy; copy = null; return finished;
        }
        finally
        {
            RenderTexture.active = priorTarget; GL.sRGBWrite = priorSrgb;
            RenderTexture.ReleaseTemporary(target); if (copy) UnityEngine.Object.Destroy(copy);
        }
    }

    private (int Width, int Height, Pixel[] Pixels) Load(string name, string expectedHash)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(_folder, name));
        using var sha = SHA256.Create();
        if (!BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Paper price artwork changed: " + name);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        try
        {
            if (!ImageConversion.LoadImage(texture, bytes, false)) throw new InvalidDataException("Could not load " + name);
            return (texture.width, texture.height, texture.GetPixels32().Select(p => new Pixel(p.r, p.g, p.b, p.a)).ToArray());
        }
        finally { UnityEngine.Object.Destroy(texture); }
    }

    internal void SceneUnloaded()
    {
        _overrides.RemoveAll(o => !o.Renderer);
        foreach (var source in _copies.Keys.Where(t => !t).ToArray())
        { UnityEngine.Object.Destroy(_copies[source]); _copies.Remove(source); }
    }

    public void Dispose()
    {
        foreach (var item in _overrides)
        {
            if (!item.Renderer || item.Slot >= item.Renderer.sharedMaterials.Length) continue;
            var current = new MaterialPropertyBlock(); item.Renderer.GetPropertyBlock(current, item.Slot);
            if (current.GetTexture(MainTex) == item.Corrected) item.Renderer.SetPropertyBlock(item.Previous.isEmpty ? null : item.Previous, item.Slot);
        }
        _overrides.Clear();
        foreach (var copy in _copies.Values) if (copy) UnityEngine.Object.Destroy(copy);
        _copies.Clear(); _scans.Clear(); _expected = _replacement = null;
    }
}
