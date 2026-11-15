using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Interactable_Infos.Common;
using Runtime_Classes.Common;
using Runtime_Classes.Pickups;
using SaveSystem;
using Tools.Helpers;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PlaqueRamen;

// Reuse native delivery/storage/save handling. The original certificate changes
// paper and shop text; added variants own their geometry, materials, colliders and GUIDs.
internal sealed class CertificateCatalog : IDisposable
{
    private readonly CertificateArt[] _art;
    private readonly string _folder;
    private readonly string _preparedFolder;
    private readonly Dictionary<string,PreparedCertificate> _prepared;
    private readonly Action<string> _log;
    private readonly GameObject _templates;
    private readonly List<(SavableObject Root,SavableVariant Variant)> _added=new();
    private readonly Dictionary<SerializableGuid,SerializableGuid> _unlockAliases=new();
    private readonly Dictionary<string,Surface> _surfaces=new();
    private readonly Dictionary<MeshFilter,Binding> _bindings=new();
    private readonly HashSet<int> _rejected=new();
    private readonly Dictionary<string,string> _texts=new(StringComparer.Ordinal);
    private Metadata? _originalMetadata;
    private MainInteractableInfo? _nativeInfo;
    private Mesh? _emptySupport;
    private readonly Dictionary<MeshFilter,(Mesh Mesh,Material[] Materials)> _supports=new();
    private readonly Dictionary<Decoration,(PlacementType Type,bool Rigid)> _mounts=new();
    private bool _registering;
    internal bool Ready {get;private set;}
    internal string Message {get;private set;}="Waiting for the native certificate catalogue.";
    internal object Status=>new {registered=Ready,message=Message,applied=_bindings.Count(p=>p.Key),
        textures=_surfaces.Count,mountOverrides=_mounts.Count(p=>p.Key),
        observedMounts=_mounts.Where(p=>p.Key).Take(12).Select(p=>new{name=p.Key.name,mode=p.Key.placementType.ToString(),rigidbodyOffOnPlace=p.Key.SetRBOffOnPlace}).ToArray(),
        items=_added.Select(p=>new{p.Variant.DisplayName,price=p.Variant.Price,id=p.Variant.SerializableGUID.ToString()}).ToArray()};

    private sealed class Surface
    {
        internal Mesh Mesh=null!;
        internal Material Frame=null!,Paper=null!;
        internal Texture2D Texture=null!;
        internal Texture2D Gloss=null!;
        internal Texture2D? FrameTexture;
        internal Texture2D? IconTexture;
        internal Mesh? Stand;
        internal Sprite? Icon;
        internal bool OwnsFrame;
    }
    private sealed class Binding
    {
        internal MeshFilter Filter=null!;
        internal Renderer Renderer=null!;
        internal Mesh Original=null!,Replacement=null!;
        internal Material[] Materials=Array.Empty<Material>();
    }
    private sealed class Metadata
    {
        internal readonly SavableVariant Item;
        private readonly string _name,_description,_nameKey,_descriptionKey;
        internal Metadata(SavableVariant item)
        { Item=item;_name=item.DisplayName;_description=item.Description;_nameKey=item.LocalizationNameKey;_descriptionKey=item.LocalizationDescriptionKey; }
        internal void Restore()
        { Item.DisplayName=_name;Item.Description=_description;Item.LocalizationNameKey=_nameKey;Item.LocalizationDescriptionKey=_descriptionKey; }
    }
    internal static string TextKey(CertificateArt art,bool name)=>"plaqueramen-certificate-"+art.Id+(name?"-name":"-description");
    internal bool TryText(string key,out string text)
    { text="";if(!_texts.TryGetValue(key,out var value))return false;text=value;return true; }

    internal CertificateCatalog(Transform owner,string folder,Action<string> log)
    {
        _folder=Path.Combine(folder,"certificates");_preparedFolder=Path.Combine(_folder,"prepared");_log=log;
        string catalogPath=Path.Combine(folder,"Certificates.json");
        _art=JsonConvert.DeserializeObject<CertificateArt[]>(File.ReadAllText(catalogPath))
            ??throw new InvalidDataException("Missing certificate definitions.");
        if(_art.Length!=9 || _art.Select(a=>a.Guid).Distinct().Count()!=9
            || _art[0].Guid!=CertificateLayout.NativeGuid || _art[1].Guid!=CertificateLayout.HygieneGuid || _art[2].Guid!=CertificateLayout.CommunityGuid
            || _art[3].Guid!=CertificateLayout.Permit9Guid || _art[4].Guid!=CertificateLayout.Permit68Guid
            || _art[5].Guid!=CertificateLayout.CulinaryGuid || _art[6].Guid!=CertificateLayout.TrainingGuid
            || _art[7].Guid!=CertificateLayout.PortraitGuid || _art[8].Guid!=CertificateLayout.FamilyPortraitGuid)
            throw new InvalidDataException("Certificate identities changed.");
        foreach(var art in _art)
        {
            art.Validate();
            _texts.Add(TextKey(art,true),art.Name);_texts.Add(TextKey(art,false),art.Description);
        }
        var prepared=JsonConvert.DeserializeObject<CertificatePreparedCatalog>(File.ReadAllText(Path.Combine(_preparedFolder,"PreparedCertificates.json")))
            ??throw new InvalidDataException("Prepared certificate textures are missing.");
        using(var sha=SHA256.Create())prepared.Validate(_art,BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(catalogPath))).Replace("-",""));
        _prepared=prepared.Items.ToDictionary(p=>p.Id,StringComparer.Ordinal);
        foreach(var texture in prepared.Items.SelectMany(p=>p.Textures))
        {
            using var sha=SHA256.Create();using var stream=File.OpenRead(Path.Combine(_preparedFolder,texture.File));
            string hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
            if(!hash.Equals(texture.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Prepared certificate texture changed: "+texture.File);
        }
        if(_art[0].Width!=0 || _art[0].Height!=0 || _art[0].Mount!="Native" || _art[0].FrameStyle!="native" || _art[0].BorderScale!=1 || _art[0].Gilt
            || !_art[0].FrameTint.SequenceEqual(new float[]{1,1,1}))
            throw new InvalidDataException("The original appreciation certificate must retain its native size and frame finish.");
        using(var sha=SHA256.Create())
            if(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(_folder,CertificateFrameFinish.SourceFile)))).Replace("-","")!=CertificateFrameFinish.SourceSha)
                throw new InvalidDataException("Native frame wood source changed.");
        _templates=new GameObject("PlaqueRamen_CertificateTemplates");_templates.SetActive(false);_templates.transform.SetParent(owner,false);
    }

    internal void Register(ItemsInfoDatabase database)
    {
        if(Ready || _registering || !database || database.RootObjects==null)return;
        _registering=true;var staged=new List<(SavableObject Root,SavableVariant Variant)>();
        try
        {
            var candidates=database.RootObjects.Where(r=>r?.Variants!=null).SelectMany(r=>r.Variants.Select(v=>(Root:r,Variant:v)))
                .Where(p=>p.Variant?.variantIO && p.Variant.SerializableGUID.ToString()==CertificateLayout.NativeGuid).ToArray();
            if(candidates.Length!=1)throw new InvalidDataException("Expected one native appreciation certificate, found "+candidates.Length+".");
            var source=candidates[0];
            _nativeInfo=source.Root.rootIOInfo;
            var native=source.Variant.variantIO.GetComponentsInChildren<MeshFilter>(true).Where(IsNativeFace).ToArray();
            if(native.Length!=1)throw new InvalidDataException("Native certificate frame or paper face changed: "+string.Join("; ",
                source.Variant.variantIO.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.name+" / "+f.sharedMesh?.name+" / vertices "+f.sharedMesh?.vertexCount+" / slots "+f.GetComponent<Renderer>()?.sharedMaterials.Length)));
            // Stage the added variants before modifying the native paper, so every
            // new item is derived from the reviewed original mesh/material.
            foreach(var art in _art.Skip(1))
            {
                var id=new SerializableGuid(Guid.Parse(art.Guid));
                if(database.RootObjects.SelectMany(r=>r.Variants??new List<SavableVariant>()).Any(v=>v!=null&&v.SerializableGUID==id))
                    throw new InvalidDataException("Certificate identity is already in use.");
                var prefab=UnityEngine.Object.Instantiate(source.Variant.variantIO,_templates.transform,false);
                var next=new SavableVariant{variantIO=prefab};staged.Add((source.Root,next));
                var identity=prefab.GetComponent<SerializableGUIDComponent>();
                if(!identity)throw new InvalidDataException("Native certificate has no persistent identity.");
                identity.GUID=id;prefab.name="PlaqueRamen_"+art.Id;
                var face=prefab.GetComponentsInChildren<MeshFilter>(true).Single(IsNativeFace);
                var surface=Apply(face,art);StyleSupport(prefab,art,surface);ResizePlacement(prefab,art);
                next.DisplayName=art.Name;next.Description=art.Description;next.Price=art.ResolvePrice(source.Variant.Price);
                next.LocalizationNameKey=TextKey(art,true);next.LocalizationDescriptionKey=TextKey(art,false);
                next.Icon=surface.Icon?surface.Icon:source.Variant.Icon;next.MainItemType=source.Variant.MainItemType;
                next.Subtypes=source.Variant.Subtypes==null?new():new(source.Variant.Subtypes);
                next.DemoLocked=source.Variant.DemoLocked;next.NoStacking=source.Variant.NoStacking;
                next.DecorationUpgrade=source.Variant.DecorationUpgrade;next.EquipmentUpgrade=source.Variant.EquipmentUpgrade;
                if(next.SerializableGUID!=id)throw new InvalidDataException("Certificate identity was not isolated from its original.");
            }
            Apply(native[0],_art[0]);
            foreach(var entry in staged)entry.Root.Variants.Add(entry.Variant);
            _added.AddRange(staged);staged.Clear();
            foreach(var entry in _added)_unlockAliases.Add(entry.Variant.SerializableGUID,source.Variant.SerializableGUID);
            _originalMetadata=new Metadata(source.Variant);
            source.Variant.DisplayName=_art[0].Name;source.Variant.Description=_art[0].Description;
            source.Variant.LocalizationNameKey=TextKey(_art[0],true);source.Variant.LocalizationDescriptionKey=TextKey(_art[0],false);
            Ready=true;
            Message="Certificate of Appreciation updated; "+_added.Count+" framed decoration variants registered.";_log(Message);
        }
        catch
        {
            _originalMetadata?.Restore();_originalMetadata=null;
            foreach(var entry in staged)entry.Root.Variants.Remove(entry.Variant);
            foreach(var binding in _bindings.Values)Restore(binding);
            _bindings.Clear();
            RestoreSupports();
            foreach(var entry in _added){entry.Root.Variants.Remove(entry.Variant);if(entry.Variant.variantIO)UnityEngine.Object.Destroy(entry.Variant.variantIO);}
            _added.Clear();_unlockAliases.Clear();Ready=false;throw;
        }
        finally
        {
            foreach(var entry in staged)if(entry.Variant.variantIO)UnityEngine.Object.Destroy(entry.Variant.variantIO);
            _registering=false;
        }
    }

    internal bool TryNative(SerializableGuid id,out SerializableGuid original)=>_unlockAliases.TryGetValue(id,out original);

    internal void ConfigurePlacement(Decoration decoration)
    {
        if(!Ready || !decoration)return;
        var runtime=decoration.GetComponent<RuntimeInteractable>();
        if(!runtime || runtime.GetInfo!=_nativeInfo)return;
        var current=runtime.CurrentVariant;
        var identity=current?current.GetComponent<SerializableGUIDComponent>():null;
        var art=identity?_art.FirstOrDefault(a=>a.Guid==identity.GUID.ToString()):null;
        if(art==null || art.Mount=="Native")
        {
            if(_mounts.TryGetValue(decoration,out var original)){decoration.placementType=original.Type;decoration.SetRBOffOnPlace=original.Rigid;_mounts.Remove(decoration);}
            return;
        }
        if(!_mounts.ContainsKey(decoration))_mounts.Add(decoration,(decoration.placementType,decoration.SetRBOffOnPlace));
        decoration.placementType=art.Mount=="Table"?PlacementType.Floor:PlacementType.Wall;
        decoration.SetRBOffOnPlace=art.Mount!="Table";
    }

    internal void Observe(MeshFilter filter)
    {
        if(!filter || _bindings.ContainsKey(filter) || _rejected.Contains(filter.GetInstanceID()))return;
        var mesh=filter.sharedMesh;
        if(!mesh || mesh.vertexCount!=72 || mesh.subMeshCount!=1)return;
        if(!IsNativeFace(filter)){_rejected.Add(filter.GetInstanceID());return;}
        var ids=filter.GetComponentsInParent<SerializableGUIDComponent>(true);
        var art=_art.FirstOrDefault(a=>ids.Any(c=>c.GUID.ToString()==a.Guid));
        if(art==null)return;
        try{Apply(filter,art);}
        catch(Exception ex){_rejected.Add(filter.GetInstanceID());Message="Certificate paper left original: "+ex.Message;_log(Message);}
    }

    private static bool IsNativeFace(MeshFilter filter)
    {
        var mesh=filter.sharedMesh;var renderer=filter.GetComponent<Renderer>();
        if(!mesh || mesh.vertexCount!=72 || mesh.subMeshCount!=1 || !mesh.isReadable || !renderer)return false;
        var mats=renderer.sharedMaterials;
        return mats.Length is 1 or 2 && mats[0] && mats[0].HasProperty("_MainTex") && mats[0].HasProperty("_NormalMap") && mats[0].HasProperty("_MaskGloss")
            && (mats.Length==1 || (mats[1] && mats[1].HasProperty("_RimColor") && !mats[1].HasProperty("_MainTex")))
            && mats[0].GetTexture("_MainTex")?.name=="T_Diplomas_BASE";
    }

    private Surface Apply(MeshFilter filter,CertificateArt art)
    {
        var original=filter.sharedMesh;var renderer=filter.GetComponent<Renderer>();var materials=renderer.sharedMaterials;
        string key=art.Id+":"+original.GetInstanceID()+":"+materials.Length;
        if(!_surfaces.TryGetValue(key,out var surface))
        {
            var shape=CertificateLayout.Create(original.vertices.Select(v=>new[]{v.x,v.y,v.z}).ToArray(),
                original.uv.Select(v=>new[]{v.x,v.y}).ToArray(),original.GetTriangles(0),art);
            surface=CreateSurface(original,materials,shape,art);_surfaces.Add(key,surface);
        }
        _bindings.Add(filter,new Binding{Filter=filter,Renderer=renderer,Original=original,Replacement=surface.Mesh,Materials=materials});
        var assigned=new Material[materials.Length+1];assigned[0]=art.Width==0?materials[0]:surface.Frame;assigned[1]=surface.Paper;
        Array.Copy(materials,1,assigned,2,materials.Length-1);
        filter.sharedMesh=surface.Mesh;renderer.sharedMaterials=assigned;
        return surface;
    }

    private Surface CreateSurface(Mesh original,Material[] materials,CertificateShape shape,CertificateArt art)
    {
        var material=materials[0];
        var surface=new Surface();
        try
        {
            var prepared=_prepared[art.Id];
            if(Math.Abs(prepared.PaperAspect-shape.PaperAspect)>.00001f)throw new InvalidDataException("Prepared paper aspect does not match its frame: "+art.Id);
            surface.Texture=LoadPrepared(prepared.Paper,"PlaqueRamen_"+art.Id,FilterMode.Trilinear,4);
            if(art.Width!=0)
            {
                surface.IconTexture=LoadPrepared(prepared.Icon!,"PlaqueRamen_"+art.Id+"_InventoryIcon",FilterMode.Trilinear);
                surface.Icon=Sprite.Create(surface.IconTexture,new Rect(0,0,CertificateIcon.Size,CertificateIcon.Size),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect);
            }
            surface.Mesh=UnityEngine.Object.Instantiate(original);surface.Mesh.name="PlaqueRamen_"+art.Id;
            // The original certificate uses byte-identical vertex positions.
            if(art.Width!=0)surface.Mesh.vertices=shape.Vertices.Select(p=>new Vector3(p[0],p[1],p[2])).ToArray();
            surface.Mesh.uv=shape.Uv.Select(p=>new Vector2(p[0],p[1])).ToArray();
            // The native renderer repeats its sole submesh for M_OutlineShelf.
            // Give that UV-independent rim material the whole original mesh,
            // after the separate frame and paper passes, retaining its effect.
            var passes=shape.Passes(materials.Length);surface.Mesh.subMeshCount=passes.Length;
            for(int pass=0;pass<passes.Length;pass++)surface.Mesh.SetTriangles(passes[pass],pass);
            surface.Mesh.RecalculateBounds();
            if(art.Mount=="Wall")surface.Mesh.RecalculateNormals();
            surface.OwnsFrame=art.Width!=0;surface.Frame=surface.OwnsFrame?new Material(material):material;
            if(surface.OwnsFrame)
            {
                surface.FrameTexture=LoadPrepared(prepared.Frame!,"PlaqueRamen_"+art.Id+"_Frame",FilterMode.Bilinear);
                surface.Frame.SetTexture("_MainTex",surface.FrameTexture);
                if(art.FrameStyle=="metal")
                {surface.Frame.DisableKeyword("N_F_NM_ON");surface.Frame.SetTexture("_NormalMap",null);surface.Frame.SetTexture("_MaskGloss",Texture2D.grayTexture);}
            }
            if(surface.OwnsFrame && surface.Frame.HasProperty("_MainColor"))surface.Frame.SetColor("_MainColor",new Color(art.FrameTint[0],art.FrameTint[1],art.FrameTint[2],1));
            surface.Paper=new Material(material){name="PlaqueRamen_"+art.Id+"_Paper"};surface.Paper.SetTexture("_MainTex",surface.Texture);
            surface.Paper.SetTextureScale("_MainTex",Vector2.one);surface.Paper.SetTextureOffset("_MainTex",Vector2.zero);
            // Remapped paper UVs must not sample the old atlas's frame normals
            // or its old printed letters in the gloss mask.
            surface.Paper.DisableKeyword("N_F_NM_ON");surface.Paper.SetTexture("_NormalMap",null);
            surface.Gloss=LoadPrepared(prepared.Gloss,"PlaqueRamen_"+art.Id+"_PaperFinish",FilterMode.Trilinear);
            surface.Paper.SetTexture("_MaskGloss",surface.Gloss);
            if(art.IsPhotograph)
            {
                // Live-calibrated on the placed Family Portrait: the inherited
                // colour power of .15 suppresses the print's midtones. Preserve
                // the accepted .6 colour power / 3 ambient combination and the
                // exact neutral colour-adjustment variant used in that trial.
                // Saturation and tint remain native; no self-lighting, world
                // light, source-pixel, frame or shadow changes are involved.
                float nativeAmbient=surface.Paper.GetFloat("_EnvironmentalLightingIntensity");
                surface.Paper.SetFloat("_EnvironmentalLightingIntensity",3f);
                surface.Paper.EnableKeyword("N_F_CA_ON");surface.Paper.SetFloat("_N_F_CA",1f);
                surface.Paper.SetFloat("_MaiColPo",.6f);
                // Use a reviewed native shader variant with a flat normal.
                // Normal-off variants with every inherited feature enabled
                // are stripped in this game build; disabling a keyword alone
                // can select a different fallback. The old atlas stays cleared.
                surface.Paper.EnableKeyword("N_F_NM_ON");
                surface.Paper.SetFloat("_NormalScale",0f);
                _log(art.Name+": native lit print; ambient="+nativeAmbient+" -> "+surface.Paper.GetFloat("_EnvironmentalLightingIntensity")
                    +"; colour power="+surface.Paper.GetFloat("_MaiColPo")+"; normal strength="+surface.Paper.GetFloat("_NormalScale")
                    +"; native frame and shadows retained; prepared print texture loaded.");
            }
            return surface;
        }
        catch {Destroy(surface);throw;}
    }

    private Texture2D LoadPrepared(PreparedTexture prepared,string name,FilterMode filter,int anisotropy=1)
    {
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,true,false)
            {name=name,wrapMode=TextureWrapMode.Clamp,filterMode=filter,anisoLevel=anisotropy};
        try
        {
            // PNG decode uploads directly; no source-sized CPU arrays, crops,
            // inventory resampling or procedural finishes are built in-game.
            if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(Path.Combine(_preparedFolder,prepared.File)),true)
                || texture.width!=prepared.Width || texture.height!=prepared.Height)
                throw new InvalidDataException("Prepared certificate texture could not be loaded: "+prepared.File);
            return texture;
        }
        catch {UnityEngine.Object.Destroy(texture);throw;}
    }

    private void StyleSupport(GameObject prefab,CertificateArt art,Surface surface)
    {
        var stand=prefab.GetComponentsInChildren<MeshFilter>(true).SingleOrDefault(f=>f.name=="Stand");
        if(!stand || art.Mount=="Native")return;
        var renderer=stand.GetComponent<Renderer>();
        if(!renderer)throw new InvalidDataException("Native display ledge renderer changed.");
        if(stand.sharedMesh.vertexCount!=36 || stand.sharedMesh.subMeshCount!=1 || stand.sharedMesh.GetIndexCount(0)!=60)
            throw new InvalidDataException("Native display ledge geometry changed.");
        _supports.Add(stand,(stand.sharedMesh,renderer.sharedMaterials));
        if(art.FlushWall)
        {
            if(!_emptySupport)
            {
                // Keep a valid native vertex/normal layout for the outline and
                // renderer caches, while no pass can draw the old ledge.
                _emptySupport=UnityEngine.Object.Instantiate(stand.sharedMesh);_emptySupport.name="PlaqueRamen_NoDisplayLedge";
                _emptySupport.SetTriangles(Array.Empty<int>(),0);_emptySupport.bounds=new Bounds(Vector3.zero,Vector3.zero);
            }
            stand.sharedMesh=_emptySupport;
        }
        else
        {
            if(!surface.Stand)
            {
                var source=stand.sharedMesh;surface.Stand=UnityEngine.Object.Instantiate(source);
                var vertices=CertificateSupport.Create(source.vertices.Select(v=>new[]{v.x,v.y,v.z}).ToArray(),art);
                surface.Stand.vertices=vertices.Select(v=>new Vector3(v[0],v[1],v[2])).ToArray();
                surface.Stand.RecalculateBounds();surface.Stand.RecalculateNormals();
            }
            stand.sharedMesh=surface.Stand;
            renderer.sharedMaterials=renderer.sharedMaterials.Select((m,i)=>i==0?surface.Frame:m).ToArray();
        }
    }

    private static void ResizePlacement(GameObject prefab,CertificateArt art)
    {
        // The native base/stand stays on the same surface. Resize only the
        // interaction envelope and the placement footprint of the added item.
        var filters=prefab.GetComponentsInChildren<MeshFilter>(true);
        foreach(var box in prefab.GetComponentsInChildren<BoxCollider>(true))
        {
            if(box.name=="PS")
            {
                if(art.FlushWall)
                {box.transform.localPosition=Vector3.zero;box.transform.localRotation=Quaternion.identity;box.transform.localScale=Vector3.one;
                    float centreY=filters.Single(f=>f.sharedMesh&&f.sharedMesh.vertexCount==72).sharedMesh.bounds.center.y;
                    box.center=new Vector3(0,centreY,-.002f);box.size=new Vector3(art.Width,art.Height,.004f);}
                else{var size=box.size;size.x+=(art.Width-.3457062f)/Math.Abs(box.transform.localScale.x);box.size=size;}
                continue;
            }
            if(box.name!="IC")continue;
            Bounds? bounds=null;
            foreach(var filter in filters)
            {
                if(!filter.sharedMesh || filter.sharedMesh.vertexCount==0 || filter.sharedMesh.name=="PlaqueRamen_NoDisplayLedge")continue;
                var b=filter.sharedMesh.bounds;
                for(int corner=0;corner<8;corner++)
                {
                    var p=b.center+Vector3.Scale(b.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                    p=box.transform.InverseTransformPoint(filter.transform.TransformPoint(p));
                    if(bounds.HasValue){var value=bounds.Value;value.Encapsulate(p);bounds=value;}else bounds=new Bounds(p,Vector3.zero);
                }
            }
            if(!bounds.HasValue)throw new InvalidDataException("Certificate placement bounds unavailable.");
            var result=bounds.Value;var scale=box.transform.lossyScale;
            result.Expand(new Vector3(.012f/Math.Abs(scale.x),.012f/Math.Abs(scale.y),.012f/Math.Abs(scale.z)));
            box.center=result.center;box.size=result.size;
        }
    }

    internal void SceneUnloaded(Scene scene)
    {
        foreach(var filter in _bindings.Keys.Where(f=>!f || f.gameObject.scene==scene).ToArray())_bindings.Remove(filter);
        foreach(var decoration in _mounts.Keys.Where(d=>!d || d.gameObject.scene==scene).ToArray())_mounts.Remove(decoration);
    }
    private static void Destroy(Surface surface)
    {
        if(surface.Mesh)UnityEngine.Object.Destroy(surface.Mesh);if(surface.Paper)UnityEngine.Object.Destroy(surface.Paper);
        if(surface.OwnsFrame && surface.Frame)UnityEngine.Object.Destroy(surface.Frame);if(surface.Icon)UnityEngine.Object.Destroy(surface.Icon);
        if(surface.IconTexture)UnityEngine.Object.Destroy(surface.IconTexture);
        if(surface.Texture)UnityEngine.Object.Destroy(surface.Texture);if(surface.Gloss)UnityEngine.Object.Destroy(surface.Gloss);
        if(surface.FrameTexture)UnityEngine.Object.Destroy(surface.FrameTexture);if(surface.Stand)UnityEngine.Object.Destroy(surface.Stand);
    }
    public void Dispose()
    {
        _originalMetadata?.Restore();_originalMetadata=null;_texts.Clear();
        foreach(var binding in _bindings.Values)Restore(binding);
        _bindings.Clear();foreach(var entry in _added)entry.Root.Variants.Remove(entry.Variant);_added.Clear();_unlockAliases.Clear();
        RestoreSupports();foreach(var entry in _mounts)if(entry.Key){entry.Key.placementType=entry.Value.Type;entry.Key.SetRBOffOnPlace=entry.Value.Rigid;}_mounts.Clear();
        foreach(var surface in _surfaces.Values)Destroy(surface);_surfaces.Clear();
        if(_emptySupport)UnityEngine.Object.Destroy(_emptySupport);
        if(_templates)UnityEngine.Object.Destroy(_templates);
    }
    private static void Restore(Binding binding)
    {
        if(binding.Filter && binding.Filter.sharedMesh==binding.Replacement){binding.Filter.sharedMesh=binding.Original;if(binding.Renderer)binding.Renderer.sharedMaterials=binding.Materials;}
    }
    private void RestoreSupports()
    {
        foreach(var entry in _supports)if(entry.Key)
        {entry.Key.sharedMesh=entry.Value.Mesh;var renderer=entry.Key.GetComponent<Renderer>();if(renderer)renderer.sharedMaterials=entry.Value.Materials;}
        _supports.Clear();
    }
}
