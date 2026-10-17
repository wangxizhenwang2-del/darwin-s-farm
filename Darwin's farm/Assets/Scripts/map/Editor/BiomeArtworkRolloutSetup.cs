using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Builds adaptation assets without modifying the artist's scenes, meshes or materials.
public static class BiomeArtworkRolloutSetup
{
    private const string Folder = "Assets/Rendering/WhiteBoxCalibratedBiomes/";
    private const string Samples = "Assets/Rendering/Moebius/Samples/Biomes/";

    [MenuItem("Tools/Darwin's Farm/Set up biome artwork")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        Scene active = SceneManager.GetActiveScene();
        if (active.path != "Assets/Scenes/WhiteBox.unity") throw new InvalidOperationException("Open WhiteBox first.");
        string sceneText = File.ReadAllText(active.path);
        var layout = System.Text.RegularExpressions.Regex.Match(sceneText,
            @"(?s)--- !u!224 &737934004\s.*?m_AnchoredPosition: \{x: ([^,]+), y: ([^}]+)\}");

        SaveSceneBiome("Desert_Moebius", "Desert", BiomeType.Desert);
        SaveSceneBiome("Forest_Moebius", "Forest", BiomeType.Forest);
        SaveSceneBiome("Grassland_Moebius", "Grassland", BiomeType.Grassland);
        SaveSceneBiome("Highland_Colored", "Highland", BiomeType.Highland);
        SaveSceneBiome("RainForest", "Rainforest", BiomeType.Rainforest);
        SaveSceneBiome("Tundra_Colored", "Tundra", BiomeType.Tundra);
        SaveMountain(false);
        SaveMountain(true);

        var map = UnityEngine.Object.FindFirstObjectByType<MapTileColorTransitionSystem>();
        var settings = new SerializedObject(map);
        settings.FindProperty("preserveOtherBiomeArtwork").boolValue = true;
        var entries = new[]
        {
            (BiomeType.Forest, "Forest", "forestBiomeVisual"),
            (BiomeType.Desert, "Desert", "desertBiomeVisual"),
            (BiomeType.Grassland, "Grassland", "grasslandBiomeVisual"),
            (BiomeType.Highland, "Highland", "highlandBiomeVisual"),
            (BiomeType.Rainforest, "Rainforest", "rainforestBiomeVisual"),
            (BiomeType.Tundra, "Tundra", "tundraBiomeVisual"),
            (BiomeType.SnowMountain, "SnowMountain", "snowMountainBiomeVisual"),
            (BiomeType.Volcano, "Volcano", "volcanoBiomeVisual")
        };
        var palette = settings.FindProperty("otherBiomeGroundColors");
        palette.arraySize = entries.Length;
        var report = new System.Text.StringBuilder("# Biome artwork calibration\n\n");
        report.AppendLine("Forest keeps the accepted palette (0.356, 0.430, 0.284). Other palettes are sampled from their authored ground after paper lift, in linear light, then stored as sRGB Inspector colors.\n");
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + entry.Item2 + "_WhiteBoxVisual.prefab");
            if (prefab == null) throw new InvalidOperationException("Missing adapted biome " + entry.Item2);
            settings.FindProperty(entry.Item3).objectReferenceValue = prefab;
            Color color = SamplePalette(prefab, entry.Item1, out int samples, out float minimumAlpha);
            SerializedProperty item = palette.GetArrayElementAtIndex(i);
            item.FindPropertyRelative("biome").intValue = (int)entry.Item1;
            item.FindPropertyRelative("color").colorValue = color;
            report.AppendLine(entry.Item2 + ": " + color + "; " + samples + " ground samples; paint alpha min " + minimumAlpha);
        }
        Undo.RecordObject(map, "Enable authored biome artwork");
        settings.ApplyModifiedProperties();
        if (layout.Success)
        {
            RectTransform rect = active.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<RectTransform>(true))
                .First(r => GlobalObjectId.GetGlobalObjectIdSlow(r).targetObjectId == 737934004UL);
            rect.anchoredPosition = new Vector2(float.Parse(layout.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(layout.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
        }
        EditorSceneManager.MarkSceneDirty(active);
        EditorSceneManager.SaveScene(active);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(".utmp/biome-rollout");
        File.WriteAllText(".utmp/biome-rollout/calibration.md", report.ToString());
        Debug.Log(report.ToString());
    }

    private static void SaveSceneBiome(string sceneName, string name, BiomeType biome)
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(Samples + "Scenes/" + sceneName + ".unity");
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            GameObject source;
            if (biome == BiomeType.Forest || biome == BiomeType.Rainforest)
                source = roots.SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                    .First(t => t.name == (biome == BiomeType.Forest ? "Forest_no_edge" : "Rain_Forest_no_edge")).gameObject;
            else
                source = roots.First(g => g.GetComponentsInChildren<MeshRenderer>(true).Length > 0);
            GameObject vfx = biome == BiomeType.Highland ? AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Rendering/Moebius/Content/VFX/Highland/Prefabs/HighlandWindVFX.prefab") :
                roots.First(g => g.name == name + "VFX");
            SaveCalibrated(source, vfx, name, biome);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void SaveCalibrated(GameObject source, GameObject sourceVfx, string name, BiomeType biome)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        GameObject wrapper = new GameObject(name + "_WhiteBoxVisual");
        SceneManager.MoveGameObjectToScene(wrapper, scene);
        try
        {
            GameObject model = UnityEngine.Object.Instantiate(source, wrapper.transform, false);
            model.name = source.name;
            // A nested scene model's local transform omits its authored ancestors.
            model.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            model.transform.localScale = source.transform.lossyScale;
            Renderer ground = model.GetComponentsInChildren<MeshRenderer>(true).First(r => Ground(biome, r.name));
            PreserveReadableGround(ground.GetComponent<MeshFilter>(), name);
            // Calibrate the tile footprint, never the bounds of trees or neighbouring demo tiles.
            model.transform.RotateAround(ground.bounds.center, Vector3.up, -ground.transform.eulerAngles.y);
            Bounds bounds = ground.bounds;
            float factor = 15f / Mathf.Max(bounds.size.x, bounds.size.z);
            model.transform.localScale *= factor;
            model.transform.localPosition = (model.transform.localPosition -
                new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)) * factor;
            Matrix4x4 sceneToTile = model.transform.localToWorldMatrix * source.transform.worldToLocalMatrix;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Vector3 center = renderer.bounds.center;
                if (renderer != ground && (Ground(biome, renderer.name) ||
                    Mathf.Abs(center.x) > 7.5f || Mathf.Abs(center.z) > 7.5f ||
                    renderer.name.StartsWith("human", StringComparison.OrdinalIgnoreCase)))
                    renderer.gameObject.SetActive(false);
            }
            MeshCollider probe = ground.gameObject.AddComponent<MeshCollider>();
            probe.sharedMesh = ground.GetComponent<MeshFilter>().sharedMesh;
            BakeVfx(wrapper, sourceVfx, sceneToTile, probe, name, biome);
            UnityEngine.Object.DestroyImmediate(probe);
            PrefabUtility.SaveAsPrefabAsset(wrapper, Folder + name + "_WhiteBoxVisual.prefab");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    // Some painted .asset meshes intentionally discard their CPU copy. Preserve
    // every rendered channel in an adapter asset; never change Vincent's import
    // flags or try to read that non-readable mesh from a Player callback.
    private static void PreserveReadableGround(MeshFilter filter, string biome)
    {
        Mesh source = filter.sharedMesh;
        if (source.isReadable) return;
        string folder = Folder + "NativeGroundMeshes";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Folder.TrimEnd('/'), "NativeGroundMeshes");
        string path = folder + "/" + biome + "_Ground.asset";
        Mesh copy = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (copy == null) { copy = new Mesh(); AssetDatabase.CreateAsset(copy, path); }
        else copy.Clear();
        copy.name = biome + "_Ground";
        copy.indexFormat = source.indexFormat;
        copy.vertices = source.vertices;
        if (source.normals.Length > 0) copy.normals = source.normals;
        if (source.tangents.Length > 0) copy.tangents = source.tangents;
        if (source.colors.Length > 0) copy.colors = source.colors;
        for (int channel = 0; channel < 8; channel++)
        {
            var attribute = (UnityEngine.Rendering.VertexAttribute)((int)UnityEngine.Rendering.VertexAttribute.TexCoord0 + channel);
            if (!source.HasVertexAttribute(attribute)) continue;
            int dimension = source.GetVertexAttributeDimension(attribute);
            if (dimension == 2) { var uv = new List<Vector2>(); source.GetUVs(channel, uv); copy.SetUVs(channel, uv); }
            else if (dimension == 3) { var uv = new List<Vector3>(); source.GetUVs(channel, uv); copy.SetUVs(channel, uv); }
            else { var uv = new List<Vector4>(); source.GetUVs(channel, uv); copy.SetUVs(channel, uv); }
        }
        copy.subMeshCount = source.subMeshCount;
        for (int i = 0; i < source.subMeshCount; i++) copy.SetIndices(source.GetIndices(i), source.GetTopology(i), i, false);
        copy.bounds = source.bounds;
        EditorUtility.SetDirty(copy);
        filter.sharedMesh = copy;
    }

    private static void BakeVfx(GameObject wrapper, GameObject source, Matrix4x4 sceneToTile,
        MeshCollider ground, string name, BiomeType biome)
    {
        string meshFolder = Folder + "VFXMeshes";
        if (!AssetDatabase.IsValidFolder(meshFolder)) AssetDatabase.CreateFolder(Folder.TrimEnd('/'), "VFXMeshes");
        string sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(source);
        if (string.IsNullOrEmpty(sourcePath)) sourcePath = AssetDatabase.GetAssetPath(source);
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), wrapper.transform);
        rig.name = name + "VFX";
        rig.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        rig.transform.localScale = Vector3.one;
        foreach (Collider collider in rig.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        ParticleSystem[] originals = source.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem particles in rig.GetComponentsInChildren<ParticleSystem>(true))
        {
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem original = originals.First(p => p.name == particles.name);
            var shape = original.shape;
            Mesh mesh = shape.mesh;
            Vector3[] vertices = mesh.vertices;
            Matrix4x4 mapping = sceneToTile * original.transform.localToWorldMatrix *
                Matrix4x4.TRS(shape.position, Quaternion.Euler(shape.rotation), shape.scale);
            for (int i = 0; i < vertices.Length; i++) vertices[i] = mapping.MultiplyPoint3x4(vertices[i]);
            var outputVertices = new List<Vector3>();
            var outputTriangles = new List<int>();
            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                Vector3 center = (a + b + c) / 3f;
                if (Mathf.Abs(center.x) > 7.1f || Mathf.Abs(center.z) > 7.1f) continue;
                int start = outputVertices.Count;
                foreach (Vector3 point in new[] { a, b, c })
                {
                    Vector3 p = point;
                    p.x = Mathf.Clamp(p.x, -7.1f, 7.1f); p.z = Mathf.Clamp(p.z, -7.1f, 7.1f);
                    if (!ground.Raycast(new Ray(p + Vector3.up * 80f, Vector3.down), out RaycastHit hit, 160f))
                        p.y = .5f;
                    else p.y = Mathf.Max(.08f, p.y - hit.point.y);
                    outputVertices.Add(p);
                }
                outputTriangles.AddRange(new[] {start, start + 1, start + 2});
            }
            if (outputTriangles.Count == 0) throw new InvalidOperationException("No single-tile emission: " + name + "/" + particles.name);
            string path = meshFolder + "/" + name + "_" + particles.name + ".asset";
            Mesh adapted = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (adapted == null) { adapted = new Mesh(); AssetDatabase.CreateAsset(adapted, path); }
            else adapted.Clear();
            adapted.name = name + "_" + particles.name;
            adapted.SetVertices(outputVertices); adapted.SetTriangles(outputTriangles, 0);
            adapted.RecalculateBounds(); adapted.RecalculateNormals(); EditorUtility.SetDirty(adapted);
            particles.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            particles.transform.localScale = Vector3.one;
            var target = particles.shape; target.mesh = adapted;
            target.position = Vector3.zero; target.rotation = Vector3.zero; target.scale = Vector3.one;
        }
        Bounds tileBounds = new Bounds(new Vector3(0, 4, 0), new Vector3(17, 16, 17));
        var wood = rig.GetComponent<Darwin.Rendering.WoodlandWindController>();
        if (wood != null) { wood.localBounds = tileBounds; wood.animateEditModePreview = false; }
        var grass = rig.GetComponent<Darwin.Rendering.GrasslandWindController>();
        if (grass != null) { grass.localBounds = tileBounds; grass.animateEditModePreview = false; }
        var wind = rig.GetComponent<Darwin.Rendering.MoebiusWindController>();
        if (wind != null) { wind.localBounds = tileBounds; wind.animateEditModePreview = false; }
        rig.AddComponent<BiomeTileVfx>();
        // UV/relief probes and construction ghosts must never start an ambient rig.
        rig.SetActive(false);
    }

    private static void SaveMountain(bool volcano)
    {
        string name = volcano ? "Volcano" : "SnowMountain";
        Scene scene = EditorSceneManager.NewPreviewScene();
        GameObject wrapper = new GameObject(name + "_WhiteBoxVisual");
        SceneManager.MoveGameObjectToScene(wrapper, scene);
        try
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "BiomeGround";
            ground.transform.SetParent(wrapper.transform, false);
            ground.transform.localPosition = Vector3.down * .25f;
            ground.transform.localScale = new Vector3(15f, .5f, 15f);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(volcano ?
                "Assets/Rendering/Moebius/Samples/Lava/Materials/AshPaperGround.mat" :
                "Assets/Rendering/Moebius/Content/Biomes/Tundra/Materials/Tundra_Snow_Ivory.mat");
            ground.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(volcano ?
                Samples + "Prefabs/Volcano_Moebius.prefab" : Samples + "Prefabs/SnowMountain_Moebius.prefab");
            GameObject model = UnityEngine.Object.Instantiate(source, wrapper.transform, false);
            model.name = volcano ? "VolcanoModel" : "Mountain_Snow";
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            float factor = (volcano ? 6.5f : 5.8f) / Mathf.Max(bounds.size.x, bounds.size.z);
            Vector3 pivot = model.transform.localPosition;
            model.transform.localScale *= factor;
            model.transform.localPosition = (pivot - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)) * factor +
                new Vector3(-2.5f, .02f, 2f);
            PrefabUtility.SaveAsPrefabAsset(wrapper, Folder + name + "_WhiteBoxVisual.prefab");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static bool Ground(BiomeType biome, string name)
    {
        switch (biome)
        {
            case BiomeType.Desert: return name == "pPlane1";
            case BiomeType.Forest: return name.StartsWith("Fprest_Block");
            case BiomeType.Grassland: return name == "Land1";
            case BiomeType.Highland: return name == "Ground - Paint Here";
            case BiomeType.Rainforest: return name.StartsWith("Fprest_Block");
            case BiomeType.Tundra: return name == "Ground_Surface";
            default: return name == "BiomeGround";
        }
    }

    private static Color SamplePalette(GameObject prefab, BiomeType biome, out int count, out float minimumAlpha)
    {
        GameObject sample = UnityEngine.Object.Instantiate(prefab);
        sample.hideFlags = HideFlags.HideAndDontSave;
        sample.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var copies = new Dictionary<Texture, Texture2D>();
        count = 0; minimumAlpha = 1f;
        Color total = Color.black;
        try
        {
            Renderer ground = sample.GetComponentsInChildren<Renderer>(true).First(r => r.enabled &&
                r.gameObject.activeInHierarchy && Ground(biome, r.name));
            Mesh mesh = ground.GetComponent<MeshFilter>().sharedMesh;
            MeshCollider collider = ground.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            Material[] materials = ground.sharedMaterials;
            for (float z = -7f; z <= 7f; z += .5f)
                for (float x = -7f; x <= 7f; x += .5f)
                {
                    if (!collider.Raycast(new Ray(new Vector3(x, 80f, z), Vector3.down), out RaycastHit hit, 160f) || hit.normal.y < .15f)
                        continue;
                    int triangle = hit.triangleIndex, submesh = 0;
                    while (submesh < mesh.subMeshCount - 1 && triangle >= mesh.GetIndexCount(submesh) / 3)
                    { triangle -= (int)mesh.GetIndexCount(submesh) / 3; submesh++; }
                    Material material = materials[Mathf.Min(submesh, materials.Length - 1)];
                    if (!material.HasProperty("_UsePaintColor")) throw new InvalidOperationException("Unsupported ground shader " + material.name);
                    Color color = material.GetColor("_BaseColor").linear;
                    Texture texture = material.GetTexture("_PaintColorMap");
                    if (material.GetFloat("_UsePaintColor") > .5f && texture != null)
                    {
                        if (!copies.TryGetValue(texture, out Texture2D copy)) copies.Add(texture, copy = ReadLinear(texture));
                        Color paint = copy.GetPixelBilinear(hit.textureCoord.x, hit.textureCoord.y);
                        minimumAlpha = Mathf.Min(minimumAlpha, paint.a);
                        color = Color.Lerp(color, paint, paint.a);
                    }
                    color = Color.Lerp(color, material.GetColor("_HighlightColor").linear, material.GetFloat("_PaperLift"));
                    total += color; count++;
                }
            if (count == 0) throw new InvalidOperationException("No ground samples: " + biome);
            if (biome == BiomeType.Tundra && minimumAlpha < .999f)
                throw new InvalidOperationException("Tundra multi-material paint must fully cover the source ground.");
            Color mean = total / count; mean.a = 1f;
            return mean.gamma;
        }
        finally
        {
            foreach (Texture2D copy in copies.Values) UnityEngine.Object.DestroyImmediate(copy);
            UnityEngine.Object.DestroyImmediate(sample);
        }
    }

    private static Texture2D ReadLinear(Texture texture)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture buffer = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        try
        {
            Graphics.Blit(texture, buffer); RenderTexture.active = buffer;
            var copy = new Texture2D(256, 256, TextureFormat.RGBAFloat, false, true);
            copy.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); copy.Apply(); return copy;
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(buffer); }
    }
}
