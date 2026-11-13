using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using SaveSystem;
using Runtime_Classes.Common;
using Runtime_Classes.Pickups;
using Tools.Helpers;
using RSServiceLocator.Scripts;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.UIElements;

namespace PlaqueRamen;

// Separate stable variant identities, on the game's existing decoration root:
// native purchase/delivery/storage/save paths remain responsible for the item.
internal sealed class LiveBoardCatalog : IDisposable
{
    internal const string BrightId = "c520fa65-7cdb-4c6b-b7de-9748c08a8332";
    internal const string DarkId = "86d52ae5-37b4-48fa-9ad9-03b78218aacb";
    // The database also contains two native copies of the seven-slot model.
    // Use the reviewed variant identities as well as the full mesh contract.
    internal const string NativeBrightId = "36f7a9c8-9c9b-490a-8b69-b1c7e9d5125d";
    internal const string NativeDarkId = "1d89a3f5-dcae-444d-a4b8-7345bbdd3564";
    internal const string NameKey = "plaqueramen-live-menu-name";
    internal const string DisplayName = "Live Menu Board Shelf";
    internal const string BrightDescriptionKey = "plaqueramen-live-menu-description-bright";
    internal const string DarkDescriptionKey = "plaqueramen-live-menu-description-dark";
    internal const string DescriptionSuffix = "This variant automatically reflects changes to the shop's menu.";
    private readonly HolderDefinition[] _definitions;
    private readonly Action<string> _log;
    private readonly List<(SavableObject Root, SavableVariant Variant, SerializableGuid Native)> _added = new();
    private readonly Dictionary<SerializableGuid,SerializableGuid> _unlockAliases=new();
    private readonly Dictionary<string,SavableVariant> _descriptionSources=new();
    private readonly GameObject _templates;
    private bool _registering;
    internal bool Ready => _added.Count == 2;
    internal object Status => new { registered = Ready, items = _added.Select(p => new { p.Variant.DisplayName, yen = p.Variant.Price,
        id = p.Variant.SerializableGUID.ToString(), nativeId = p.Native.ToString() }).ToArray() };

    internal LiveBoardCatalog(Transform owner, HolderDefinition bright, HolderDefinition dark, Action<string> log)
    {
        _definitions = new[] { bright, dark }; _log = log;
        _templates = new GameObject("PlaqueRamen_InactiveTemplates"); _templates.SetActive(false);
        _templates.transform.SetParent(owner, false);
    }

    internal void Register(ItemsInfoDatabase database)
    {
        if (Ready || _registering || !database || database.RootObjects == null) return;
        _registering = true;
        var staged = new List<(SavableObject Root, SavableVariant Variant, SerializableGuid Native)>();
        try
        {
            foreach (var definition in _definitions)
            {
                var candidates = new List<(SavableObject Root, SavableVariant Variant)>();
                foreach (var root in database.RootObjects)
                {
                    if (root?.Variants == null) continue;
                    foreach (var variant in root.Variants)
                    {
                        if (variant?.variantIO == null || variant.SerializableGUID.ToString() != (definition.Dark ? NativeDarkId : NativeBrightId)) continue;
                        foreach (var filter in variant.variantIO.GetComponentsInChildren<MeshFilter>(true))
                        {
                            var mesh = filter.sharedMesh; var renderer = filter.GetComponent<Renderer>();
                            if (!mesh || mesh.vertexCount != definition.VertexCount || mesh.subMeshCount != definition.SubmeshCount
                                || !mesh.isReadable || !renderer) continue;
                            var materials = renderer.sharedMaterials;
                            if (materials.Length != 1 || !materials[0] || !materials[0].HasProperty("_MainTex")
                                || materials[0].GetTexture("_MainTex")?.name != "T_Tanzaku001_BASE") continue;
                            HolderGeometry.Validate(definition, mesh.vertexCount, mesh.subMeshCount, mesh.GetTriangles(definition.MaterialSlot));
                            candidates.Add((root, variant)); break;
                        }
                    }
                }
                if (candidates.Count != 1) throw new InvalidDataException("Expected one native " + (definition.Dark ? "dark" : "bright") + " Menu Board Shelf, found " + candidates.Count + ".");
                var source = candidates.Single();
                var id = new SerializableGuid(Guid.Parse(definition.Dark ? DarkId : BrightId));
                if (database.RootObjects.SelectMany(r => r.Variants ?? new List<SavableVariant>()).Any(v => v != null && v.SerializableGUID == id))
                    throw new InvalidDataException("Live menu board identity is already in use.");
                var prefab = UnityEngine.Object.Instantiate(source.Variant.variantIO, _templates.transform, false);
                staged.Add((source.Root, new SavableVariant { variantIO = prefab }, source.Variant.SerializableGUID));
                var identity = prefab.GetComponent<SerializableGUIDComponent>();
                if (!identity) throw new InvalidDataException("Native board variant has no persistent identity.");
                identity.GUID = id; prefab.name = definition.Dark ? "PlaqueRamen_LiveMenuBoard_Dark" : "PlaqueRamen_LiveMenuBoard_Bright";
                var next = staged[staged.Count - 1].Variant;
                next.DisplayName = DisplayName; next.Price = 2000;
                next.LocalizationNameKey = NameKey;
                next.LocalizationDescriptionKey = definition.Dark ? DarkDescriptionKey : BrightDescriptionKey;
                _descriptionSources[next.LocalizationDescriptionKey] = source.Variant;
                next.Description = AppendDescription(source.Variant.Description);
                next.Icon = source.Variant.Icon; next.MainItemType = source.Variant.MainItemType;
                next.Subtypes = source.Variant.Subtypes == null ? new() : new(source.Variant.Subtypes);
                next.DemoLocked = source.Variant.DemoLocked; next.NoStacking = source.Variant.NoStacking;
                next.DecorationUpgrade = source.Variant.DecorationUpgrade; next.EquipmentUpgrade = source.Variant.EquipmentUpgrade;
                if (next.SerializableGUID != id || next.SerializableGUID == source.Variant.SerializableGUID)
                    throw new InvalidDataException("Live board identity was not isolated from its original.");
            }
            foreach (var entry in staged) entry.Root.Variants.Add(entry.Variant);
            _added.AddRange(staged); staged.Clear();
            foreach(var entry in _added)_unlockAliases.Add(entry.Variant.SerializableGUID,entry.Native);
            foreach (var entry in _added) _log("Registered " + entry.Variant.variantIO.name + " / " + entry.Variant.SerializableGUID + " for 2000 yen; native item unchanged.");
        }
        finally
        {
            foreach (var entry in staged) if (entry.Variant.variantIO) UnityEngine.Object.Destroy(entry.Variant.variantIO);
            _registering = false;
        }
    }

    internal bool TryNative(SerializableGuid id, out SerializableGuid original)
    {
        return _unlockAliases.TryGetValue(id,out original);
    }
    internal bool TryDescription(string key,out string nativeKey,out string fallback)
    {
        nativeKey="";fallback="";
        if(!_descriptionSources.TryGetValue(key,out var source))return false;
        nativeKey=source.LocalizationDescriptionKey;fallback=source.Description;return true;
    }
    internal static string AppendDescription(string? original)=>string.IsNullOrWhiteSpace(original)?DescriptionSuffix:original!.TrimEnd()+" "+DescriptionSuffix;
    internal static bool IsId(SerializableGuid id) => id.ToString() == BrightId || id.ToString() == DarkId;
    internal static bool IsLive(MeshFilter filter) => filter.GetComponentsInParent<SerializableGUIDComponent>(true).Any(c => IsId(c.GUID));
    public void Dispose()
    {
        foreach (var entry in _added) entry.Root.Variants.Remove(entry.Variant);
        _added.Clear();_unlockAliases.Clear();_descriptionSources.Clear(); if (_templates) UnityEngine.Object.Destroy(_templates);
    }
}

// Native previews bind localisation keys rather than raw metadata. Resolve
// our private certificate text and live-board descriptions; unrelated labels
// retain their normal localisation bindings.
[HarmonyPatch(typeof(LocalizationService),nameof(LocalizationService.SetUITBinding),new[]{typeof(Label),typeof(TableEntryReference),typeof(ValueTuple<string,string>[]),typeof(string)})]
internal static class LiveBoardDescriptions
{
    private static bool Prefix(LocalizationService __instance,Label __0,TableEntryReference __1)
    {
        // Storage hover has no raw DisplayName fallback, unlike Goods Shop.
        // Use the same private key in all native label-binding paths.
        if(__1.ReferenceType==TableEntryReference.Type.Name && __1.Key==LiveBoardCatalog.NameKey)
        {
            LiveBoardDividers.Restore(__0);if(__0!=null)__instance.SetNonLocalizedUIInfo(__0,LiveBoardCatalog.DisplayName);return false;
        }
        string text="";
        if(__1.ReferenceType==TableEntryReference.Type.Name && __1.Key.StartsWith("plaqueramen-certificate-",StringComparison.Ordinal)
            && Plugin.Instance?.TryCertificateText(__1.Key,out text)==true)
        {
            LiveBoardDividers.Restore(__0);if(__0!=null)__instance.SetNonLocalizedUIInfo(__0,text);return false;
        }
        if(__1.ReferenceType!=TableEntryReference.Type.Name || (__1.Key!=LiveBoardCatalog.BrightDescriptionKey && __1.Key!=LiveBoardCatalog.DarkDescriptionKey))
        { LiveBoardDividers.Restore(__0);return true; }
        string nativeKey="",fallback="";
        if(Plugin.Instance?.TryBoardDescription(__1.Key,out nativeKey,out fallback)!=true)return true;
        if(__0!=null)
        {
            string original=__instance.GetLocalizedStringOrFallback(nativeKey,fallback);
            __instance.SetNonLocalizedUIInfo(__0,LiveBoardCatalog.AppendDescription(original));
            LiveBoardDividers.Preserve(__0,original);
        }
        return false;
    }
}

[HarmonyPatch(typeof(ItemsInfoDatabase), nameof(ItemsInfoDatabase.GetOrLoad))]
internal static class EnsureLiveBoardDatabase
{
    private static void Postfix(ItemsInfoDatabase __result) => Plugin.Instance?.RegisterBoards(__result);
}
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.GetUnlockedVariantFromGUID))]
internal static class EnsureLiveBoardBeforeLoad
{
    private static void Prefix(SaveManager __instance) => Plugin.Instance?.RegisterBoards(__instance.ItemsInfoDatabase);
}
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.GetLockState))]
internal static class LiveBoardUnlock
{
    // Mirror the original board's availability without editing unlocks or saves.
    private static void Prefix(ref SerializableGuid guid) => Plugin.Instance?.MapBoardUnlock(ref guid);
}
[HarmonyPatch(typeof(RuntimeInteractable), nameof(RuntimeInteractable.ChangeVariant), new[] { typeof(SavableVariant) })]
internal static class ObserveLiveBoardVariant
{
    private static void Postfix(RuntimeInteractable __instance)
    {
        var decoration = __instance.GetComponent<Decoration>();
        if (decoration) Plugin.Instance?.QueueDecoration(decoration);
    }
}

[HarmonyPatch(typeof(RuntimeInteractable), nameof(RuntimeInteractable.LoadVariant), new[] { typeof(GameObject), typeof(bool) })]
internal static class ObserveCertificateVariantLoad
{
    private static void Postfix(RuntimeInteractable __instance)
    {
        var decoration=__instance.GetComponent<Decoration>();
        if(decoration)Plugin.Instance?.ConfigureCertificatePlacement(decoration);
    }
}
