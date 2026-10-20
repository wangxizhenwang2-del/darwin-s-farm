using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AnimalAnimationSizeValidation
{
    public static void BakeAndValidate()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Batch validation only.");
        int sets = 0, frames = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:PopulationAnimationSet"))
        {
            var set = AssetDatabase.LoadAssetAtPath<PopulationAnimationSet>(AssetDatabase.GUIDToAssetPath(guid));
            if (set == null || set.IdleSprite == null) continue;
            float height = PopulationAnimationSizing.Bake(set);
            Require(height > 0 && !float.IsNaN(height), "Invalid reference height: " + set.name);
            AssetDatabase.SaveAssetIfDirty(set);
            var owner = new GameObject("Size validation");
            try
            {
                var image = owner.AddComponent<SpriteRenderer>();
                PopulationSpriteDisplay.Configure(image, set.IdleSprite, Color.white, set.ReferenceVisualHeight);
                Vector3 expectedScale = image.transform.localScale;
                Require(Mathf.Abs(height * expectedScale.y - 1.5f) < .001f,
                    "Visible reference height differs: " + set.name);
                var allFrames = new[] { set.WalkFrames, set.IdleFrames, set.CommonFrames, set.TransitionFrames }
                    .Where(sequence => sequence != null).SelectMany(sequence => sequence).Where(sprite => sprite != null);
                foreach (Sprite frame in allFrames)
                {
                    PopulationSpriteDisplay.Configure(image, frame, Color.white, set.ReferenceVisualHeight);
                    Require(image.transform.localScale == expectedScale, "Scale changes between frames: " + set.name);
                    frames++;
                }
                if (set.HasFrameSequences)
                {
                    var player = owner.AddComponent<PopulationFrameAnimationController>();
                    player.Bind(image, set, Color.white);
                    if (!player.IsCommonLoop) { player.RequestStart(); player.BeginWalk(10f, 1.8f); }
                    for (int step = 0; step < 50; step++)
                    {
                        player.Tick(.05f, Time.time + step * .05f);
                        Require(image.transform.localScale == expectedScale,
                            "Playback overrides normalized size: " + set.name);
                    }
                }
                Debug.Log("SIZE PASS: " + set.name + " reference=" + height.ToString("F4"));
                sets++;
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
        Require(sets >= 14 && frames > 100, "Animation coverage missing.");
        UpdateLabPreview();
        Debug.Log($"ANIMAL SIZE PASS: {sets} sets, {frames} frames; reference silhouettes normalized to 1.5 units; fixed scale during playback.");
    }

    private static void UpdateLabPreview()
    {
        const string path = "Assets/Scenes/EightAnimalAnimationLab.unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) return;
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var lab in root.GetComponentsInChildren<AnimationLabController>(true))
                {
                    var animals = new SerializedObject(lab).FindProperty("animals");
                    for (int i = 0; i < animals.arraySize; i++)
                    {
                        var animal = animals.GetArrayElementAtIndex(i);
                        var set = animal.FindPropertyRelative("animations").objectReferenceValue as PopulationAnimationSet;
                        var image = animal.FindPropertyRelative("sprite").objectReferenceValue as SpriteRenderer;
                        if (set != null && image != null)
                            PopulationSpriteDisplay.Configure(image, image.sprite, image.color, set.ReferenceVisualHeight);
                    }
                }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
