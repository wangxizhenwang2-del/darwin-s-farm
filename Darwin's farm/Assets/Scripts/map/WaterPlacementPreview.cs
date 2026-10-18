using System.Collections.Generic;
using Darwin.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

// Owns only temporary preview material/texture. The placement controller owns its mesh.
// Activation never registers a tile or rebuilds the live map's river network.
public sealed class WaterPlacementPreview : MonoBehaviour
{
    private Material waterMaterial;
    private Texture2D shore;

    public static GameObject Create(Transform parent, MapTileDefinition definition,
        Material bankMaterial, out Mesh waterMesh)
    {
        waterMesh = null;
        var assets = Resources.Load<MapWaterAssets>("Water/MapWaterAssets");
        if (!assets || !parent || !bankMaterial) return null;
        var prefab = assets.Resolve(definition, out int rotation);
        if (!prefab) return null;
        var root = new GameObject("WaterPlacementPreview");
        root.transform.SetParent(parent, false);
        var owner = root.AddComponent<WaterPlacementPreview>();
        var model = Instantiate(prefab, root.transform);
        model.name = "PreviewRiver";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0, rotation * 90, 0);
        var river = model.GetComponent<MoebiusRiverTile>();
        if (!river || !river.IsRiver) { Release(root); return null; }
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
        foreach (Behaviour component in model.GetComponentsInChildren<Behaviour>(true)) component.enabled = false;
        foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true))
        { body.isKinematic = true; body.detectCollisions = false; }
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            var materials = new Material[renderer.sharedMaterials.Length];
            for (int i = 0; i < materials.Length; i++) materials[i] = bankMaterial;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        if (river.originalWater) river.originalWater.forceRenderingOff = true;
        // Match the placed surface's authored geometry, height and object-space shore mask.
        // Its blue water remains readable while the existing green/red tint marks the banks.
        var ends = new List<RiverEnd>();
        waterMesh = RiverConnectionGeometry.Water(river.profile, river.waterLevel, ends);
        owner.shore = RiverConnectionGeometry.Shore(river.profile, ends);
        owner.waterMaterial = new Material(river.profile.waterMaterial) { name = "Placement Water", hideFlags = HideFlags.DontSave };
        owner.waterMaterial.SetFloat("_AnimationPhase", 0);
        owner.waterMaterial.SetFloat("_EnableWaterfallImpact", 0);
        owner.waterMaterial.SetFloat("_EnableShoreMask", 1);
        owner.waterMaterial.SetFloat("_ShoreObjectSpace", 1);
        owner.waterMaterial.SetVector("_ShoreWorldRect", river.profile.shoreRect);
        owner.waterMaterial.SetFloat("_ShoreDistanceRange", river.profile.shoreRange);
        owner.waterMaterial.SetTexture("_ShorelineMask", owner.shore);
        var surface = new GameObject("PreviewWaterSurface");
        surface.transform.SetParent(model.transform, false); surface.layer = 2;
        surface.AddComponent<MeshFilter>().sharedMesh = waterMesh;
        var water = surface.AddComponent<MeshRenderer>(); water.sharedMaterial = owner.waterMaterial;
        water.shadowCastingMode = ShadowCastingMode.Off;
        return root;
    }

    private void OnDestroy() { Release(waterMaterial); Release(shore); }
    private static void Release(Object value)
    {
        if (!value) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
