using System;
using System.IO;
using System.Linq;
using Runtime_Classes.Common;
using UnityEngine;

namespace PlaqueRamen;

internal sealed class HolderBinding : IDisposable
{
    private static readonly int MainTex = Shader.PropertyToID("_MainTex"), GlossTex = Shader.PropertyToID("_MaskGloss");
    private readonly MeshFilter _source;
    private readonly Renderer _renderer;
    private readonly Mesh _originalMesh;
    private readonly Material[] _originalMaterials;
    private readonly HolderDefinition _holder;
    private readonly HolderParts _parts;
    private readonly int[] _indices, _rightToLeft;
    private Mesh? _privateMesh;
    private string _signature = "";
    private int[] _occupied = Array.Empty<int>();
    internal bool Alive => _source && _renderer && _originalMesh && (_source.sharedMesh == _originalMesh || _privateMesh && _source.sharedMesh == _privateMesh);
    internal bool Applied => Alive && _privateMesh && _source.sharedMesh == _privateMesh;
    internal object Status => new { name = _source ? _source.name : "removed", applied = Applied, occupiedSlotsRightToLeft = _occupied,
        visibleHooks = Applied ? 9 : 0, vacantHooks = Applied ? 9 - _occupied.Length : 0 };

    internal HolderBinding(MeshFilter source, Renderer renderer, HolderDefinition holder, HolderParts parts)
    {
        _source = source; _renderer = renderer; _originalMesh = source.sharedMesh; _originalMaterials = renderer.sharedMaterials;
        _holder = holder; _parts = parts;
        if (!_originalMesh.isReadable) throw new InvalidDataException("The holder mesh is not readable.");
        _indices = _originalMesh.GetTriangles(holder.MaterialSlot);
        HolderGeometry.Validate(holder, _originalMesh.vertexCount, _originalMesh.subMeshCount, _indices);
        parts.Validate(holder, _indices); _rightToLeft = AutoLayout.RightToLeft(holder);
    }

    internal static bool IsPlaced(MeshFilter source)
    {
        var interactable = source.GetComponentInParent<RuntimeInteractable>();
        return source.gameObject.activeInHierarchy && (!interactable || !interactable.IsHeld);
    }

    internal bool Apply(PlannedPlaque[] plan, string signature, PlaqueTextures textures)
    {
        if (!Alive || !IsPlaced(_source)) return false;
        var materials = _renderer.sharedMaterials;
        if (Applied && signature == _signature && materials.Length == 10 && materials.All(m => m == _originalMaterials[0])) return false;
        var groups = MenuPlan.Submeshes(_indices, _holder, _parts, _rightToLeft, plan.Length);
        _occupied = MenuPlan.CenteredPlaques(_rightToLeft, plan.Length);
        if (!_privateMesh)
        {
            _privateMesh = UnityEngine.Object.Instantiate(_originalMesh); _privateMesh.name = "PlaqueRamen_PrivateHolder";
            _privateMesh.subMeshCount = 10;
            var vertices = _originalMesh.vertices;
            var mounted = HookMount.Connect(_holder, _parts, _indices, vertices.Select(v => new HookPoint(v.x, v.y, v.z)).ToArray());
            for (int i = 0; i < vertices.Length; i++) if (mounted[i] != vertices[i].z) vertices[i].z = mounted[i];
            _privateMesh.vertices = vertices; _privateMesh.RecalculateBounds();
        }
        for (int i = 0; i < groups.Length; i++) _privateMesh.SetTriangles(groups[i], i, false);
        _source.sharedMesh = _privateMesh; _renderer.sharedMaterials = Enumerable.Repeat(_originalMaterials[0], 10).ToArray();
        for (int i = 0; i < 9; i++)
        {
            if (i >= plan.Length) { _renderer.SetPropertyBlock(null, i + 1); continue; }
            var block = new MaterialPropertyBlock(); _renderer.GetPropertyBlock(block, 0);
            if (block.isEmpty) _renderer.GetPropertyBlock(block);
            var pair = textures.Get(plan[i].Art);
            block.SetTexture(MainTex, pair.Albedo); block.SetTexture(GlossTex, pair.Gloss); _renderer.SetPropertyBlock(block, i + 1);
        }
        _signature = signature; return true;
    }

    internal void Restore()
    {
        if (_privateMesh && _source && _source.sharedMesh == _privateMesh)
        {
            if (_renderer)
            {
                for (int i = 1; i < _renderer.sharedMaterials.Length; i++) _renderer.SetPropertyBlock(null, i);
                _renderer.sharedMaterials = _originalMaterials;
            }
            _source.sharedMesh = _originalMesh;
        }
        if (_privateMesh) UnityEngine.Object.Destroy(_privateMesh);
        _privateMesh = null; _signature = ""; _occupied = Array.Empty<int>();
    }
    public void Dispose() => Restore();
}
