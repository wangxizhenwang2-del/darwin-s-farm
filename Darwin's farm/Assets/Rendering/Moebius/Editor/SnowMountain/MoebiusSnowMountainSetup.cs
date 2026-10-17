using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Darwin.Rendering.Editor
{
    public static class MoebiusSnowMountainSetup
    {
        const string ModelPath = "Assets/Rendering/Moebius/Samples/Biomes/Meshes/SnowMountain.fbx";
        const string Folder = "Assets/Rendering/Moebius/Samples/Biomes/Materials/SnowMountain";
        const string MaterialPath = Folder + "/SnowMountain.mat";
        const string OutlinePath = Folder + "/SnowMountainOutline.mat";
        const string PrefabPath = "Assets/Rendering/Moebius/Samples/Biomes/Prefabs/SnowMountain_Template.prefab";

        [MenuItem("Tools/Moebius/Snow Mountain/Build Material and Prefab")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder);
            var shader = Shader.Find("Darwin/Moebius Snow Mountain");
            var outlineShader = Shader.Find("Darwin/Moebius Volcano Ink Outline");
            if (!shader || !outlineShader) throw new InvalidOperationException("Snow mountain or outline shader is missing.");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!model) throw new InvalidOperationException("SnowMountain.fbx is missing.");
            var sourceFilters = model.GetComponentsInChildren<MeshFilter>(true);
            if (sourceFilters.Length == 0) throw new InvalidOperationException("SnowMountain has no mesh.");

            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            foreach (var filter in sourceFilters)
            {
                var mesh = filter.sharedMesh;
                if (!mesh) continue;
                // The shader evaluates each renderer's local position, so use raw mesh coordinates.
                minY = Mathf.Min(minY, mesh.bounds.min.y);
                maxY = Mathf.Max(maxY, mesh.bounds.max.y);
            }
            if (maxY - minY < 0.001f) throw new InvalidOperationException("SnowMountain mesh height is invalid.");

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material) { material = new Material(shader) { name = "SnowMountain" }; AssetDatabase.CreateAsset(material, MaterialPath); }
            material.shader = shader;
            material.SetColor("_RockColor", new Color(0.29f, 0.36f, 0.43f));
            material.SetColor("_SnowColor", new Color(0.91f, 0.94f, 0.95f));
            material.SetVector("_SnowHeightRange", new Vector4(minY, maxY, 0, 0));
            material.SetFloat("_SnowCapStart", 0.68f);
            material.SetFloat("_SnowCapFull", 0.82f);
            material.SetFloat("_SnowSlopeStart", 0.28f);
            material.SetFloat("_SnowSlopeFull", 0.65f);
            material.SetFloat("_SnowLineVariation", 0.035f);
            material.SetFloat("_SnowHatchLocalScale", 100f);
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_ShadowColor", new Color(0.70f, 0.77f, 0.84f));
            material.SetColor("_DeepShadowColor", new Color(0.49f, 0.57f, 0.66f));
            material.SetColor("_HighlightColor", new Color(0.97f, 0.97f, 0.94f));
            material.SetColor("_InkColor", new Color(0.055f, 0.065f, 0.085f));
            material.SetFloat("_ShadowThreshold", 0.48f);
            material.SetFloat("_DeepShadowThreshold", 0.17f);
            material.SetFloat("_BrightThreshold", 0.81f);
            material.SetFloat("_ShadowSoftness", 0.018f);
            material.SetFloat("_SurfaceTintStrength", 0.42f);
            material.SetFloat("_PaperLift", 0.025f);
            material.SetFloat("_HatchScale", 55f);
            material.SetFloat("_HatchStrength", 0.48f);
            material.SetFloat("_HatchContrast", 1f);
            material.SetFloat("_HatchThreshold", 0.68f);
            material.SetFloat("_HatchRotation", -25f);
            material.SetFloat("_HatchLineWidth", 1f);
            material.SetFloat("_HatchIrregularity", 0.65f);
            material.SetFloat("_HatchCrossStrength", 0.22f);
            material.SetFloat("_HatchThirdStrength", 0.12f);
            material.SetFloat("_CastShadowHatchStrength", 0.6f);
            material.SetFloat("_CastShadowTintStrength", 0.2f);
            material.SetFloat("_HighlightStrength", 0f);
            material.SetFloat("_UseHatching", 1f);
            material.EnableKeyword("_USE_HATCHING");
            material.SetFloat("_UseTriplanar", 1f);
            material.EnableKeyword("_USE_TRIPLANAR");
            EditorUtility.SetDirty(material);

            var outline = AssetDatabase.LoadAssetAtPath<Material>(OutlinePath);
            if (!outline) { outline = new Material(outlineShader) { name = "SnowMountainOutline" }; AssetDatabase.CreateAsset(outline, OutlinePath); }
            outline.shader = outlineShader;
            outline.SetColor("_InkColor", new Color(0.055f, 0.065f, 0.085f));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            instance.name = "SnowMountain";
            // The FBX has a scale transform; outline width is measured in world units.
            var modelRenderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            var visibleBounds = modelRenderers[0].bounds;
            for (int i = 1; i < modelRenderers.Length; i++) visibleBounds.Encapsulate(modelRenderers[i].bounds);
            outline.SetFloat("_Width", visibleBounds.size.y * 0.0055f);
            EditorUtility.SetDirty(outline);
            foreach (var renderer in modelRenderers)
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) continue;
                var child = new GameObject("InkOutline");
                child.transform.SetParent(renderer.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                child.AddComponent<MeshRenderer>().sharedMaterial = outline;
            }
            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            UnityEngine.Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Snow Mountain] Material and prefab ready. Mesh height {minY:F2} to {maxY:F2}.");
        }

    }
}
