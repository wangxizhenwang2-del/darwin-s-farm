using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Darwin.Rendering.Editor
{
    public static class MoebiusSnowMountainSceneApply
    {
        const string MaterialPath = "Assets/Rendering/Moebius/Content/SnowMountain/SnowMountain.mat";
        const string OutlinePath = "Assets/Rendering/Moebius/Content/SnowMountain/SnowMountainOutline.mat";

        [MenuItem("Tools/Moebius/Snow Mountain/Apply to Selected Scene Object")]
        public static void ApplyToSelection()
        {
            var selected = Selection.activeGameObject;
            if (!selected || !selected.scene.IsValid())
                throw new InvalidOperationException("Select the SnowMountain scene object first.");
            ApplyToObject(selected);
        }

        public static void ApplyToSavedScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) throw new ArgumentException("Scene path is required.");
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var targets = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name == "SnowMountain" && t.GetComponentsInChildren<MeshFilter>(true).Length > 0)
                .ToArray();
            if (targets.Length != 1)
                throw new InvalidOperationException($"Expected one SnowMountain in {scenePath}; found {targets.Length}.");
            ApplyToObject(targets[0].gameObject);
            EditorSceneManager.SaveScene(scene);
        }

        internal static void ApplyToObject(GameObject snowMountain)
        {
            if (snowMountain.name != "SnowMountain")
                throw new InvalidOperationException("Only an object named SnowMountain may be changed.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var outline = AssetDatabase.LoadAssetAtPath<Material>(OutlinePath);
            if (!material || !outline) throw new InvalidOperationException("SnowMountain materials are missing.");
            var renderers = snowMountain.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.gameObject.name != "InkOutline")
                .ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException("SnowMountain has no mesh renderer.");
            foreach (var renderer in renderers)
            {
                Undo.RecordObject(renderer, "Assign SnowMountain material");
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++) slots[i] = material;
                renderer.sharedMaterials = slots;
                EditorUtility.SetDirty(renderer);

                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) continue;
                var child = renderer.transform.Find("InkOutline");
                if (!child)
                {
                    var created = new GameObject("InkOutline");
                    Undo.RegisterCreatedObjectUndo(created, "Add SnowMountain outline");
                    created.transform.SetParent(renderer.transform, false);
                    child = created.transform;
                }
                var outlineFilter = child.GetComponent<MeshFilter>();
                if (!outlineFilter) outlineFilter = Undo.AddComponent<MeshFilter>(child.gameObject);
                var outlineRenderer = child.GetComponent<MeshRenderer>();
                if (!outlineRenderer) outlineRenderer = Undo.AddComponent<MeshRenderer>(child.gameObject);
                Undo.RecordObject(outlineFilter, "Assign SnowMountain outline mesh");
                Undo.RecordObject(outlineRenderer, "Assign SnowMountain outline material");
                outlineFilter.sharedMesh = filter.sharedMesh;
                outlineRenderer.sharedMaterial = outline;
                EditorUtility.SetDirty(outlineFilter);
                EditorUtility.SetDirty(outlineRenderer);
            }
            EditorSceneManager.MarkSceneDirty(snowMountain.scene);
            Debug.Log($"[Snow Mountain Apply] Applied dedicated material and outline to {snowMountain.name} in {snowMountain.scene.path}.");
        }
    }

}
