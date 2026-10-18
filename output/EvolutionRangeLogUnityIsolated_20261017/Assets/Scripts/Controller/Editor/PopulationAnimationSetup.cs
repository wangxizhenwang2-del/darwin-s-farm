using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Rebuilds only the generated locomotion assets; never edits or saves a scene.
public static class PopulationAnimationSetup
{
    public const string Root = "Assets/Art/Animals/Animations";
    public const string PeacockFolder = Root + "/PeacockWalk";
    public const string SetPath = PeacockFolder + "/PeacockAnimation.asset";
    private const string SpeciesPath = "Assets/BiowebV46/Species/孔雀-f0686f45.asset";

    [MenuItem("Tools/Darwin's Farm/Animation/Rebuild Peacock Locomotion")]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
            throw new System.InvalidOperationException("Build animation assets outside Play mode.");
        if (!AssetDatabase.IsValidFolder(Root + "/Common"))
            AssetDatabase.CreateFolder(Root, "Common");

        var frames = new Sprite[8];
        for (int i = 0; i < frames.Length; i++)
        {
            string path = PeacockFolder + "/frame-" + i + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 512f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(0.5f, 0.5f);
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            frames[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (frames[i] == null) throw new System.InvalidOperationException("Missing sprite " + path);
        }

        // All frames share the same full canvas, pivot and scale, including Idle.
        AnimationClip idle = SpriteClip(PeacockFolder + "/PeacockIdle.anim", new[] { frames[0] });
        AnimationClip walk = SpriteClip(PeacockFolder + "/PeacockWalk.anim", frames);
        AnimationClip idleSlot = SpriteClip(Root + "/Common/PopulationIdle.anim", new Sprite[1]);
        AnimationClip walkSlot = SpriteClip(Root + "/Common/PopulationWalk.anim", new Sprite[1]);
        string controllerPath = Root + "/Common/PopulationLocomotion.controller";
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.parameters = new[]
        {
            new AnimatorControllerParameter { name = "IsMoving", type = AnimatorControllerParameterType.Bool },
            new AnimatorControllerParameter { name = "WalkSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f }
        };
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState child in machine.states) machine.RemoveState(child.state);
        AnimatorState idleState = machine.AddState("Idle", new Vector3(240f, 40f));
        idleState.motion = idleSlot;
        idleState.writeDefaultValues = false;
        AnimatorState walkState = machine.AddState("Walk", new Vector3(500f, 40f));
        walkState.motion = walkSlot;
        walkState.writeDefaultValues = false;
        walkState.speedParameter = "WalkSpeed";
        walkState.speedParameterActive = true;
        machine.defaultState = idleState;
        Transition(idleState, walkState, AnimatorConditionMode.If);
        Transition(walkState, idleState, AnimatorConditionMode.IfNot);
        EditorUtility.SetDirty(controller);

        string overridePath = PeacockFolder + "/Peacock.overrideController";
        AnimatorOverrideController overrides = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(overridePath);
        if (overrides == null)
        {
            overrides = new AnimatorOverrideController();
            AssetDatabase.CreateAsset(overrides, overridePath);
        }
        overrides.runtimeAnimatorController = controller;
        overrides.ApplyOverrides(new List<KeyValuePair<AnimationClip, AnimationClip>>
        {
            new KeyValuePair<AnimationClip, AnimationClip>(idleSlot, idle),
            new KeyValuePair<AnimationClip, AnimationClip>(walkSlot, walk)
        });
        EditorUtility.SetDirty(overrides);

        PopulationAnimationSet set = AssetDatabase.LoadAssetAtPath<PopulationAnimationSet>(SetPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<PopulationAnimationSet>();
            AssetDatabase.CreateAsset(set, SetPath);
        }
        var serializedSet = new SerializedObject(set);
        serializedSet.FindProperty("controller").objectReferenceValue = overrides;
        serializedSet.FindProperty("idleSprite").objectReferenceValue = frames[0];
        serializedSet.FindProperty("referenceMoveSpeed").floatValue = 1.8f;
        serializedSet.FindProperty("artworkFacesLeft").boolValue = true;
        serializedSet.ApplyModifiedPropertiesWithoutUndo();

        var species = new SerializedObject(AssetDatabase.LoadAssetAtPath<SpeciesData>(SpeciesPath));
        species.FindProperty("populationAnimation").objectReferenceValue = set;
        species.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Debug.Log("Peacock locomotion ready: static Idle, 8 fps looping Walk, shared controller.");
    }

    private static AnimationClip SpriteClip(string path, Sprite[] frames)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();
        clip.frameRate = 8f;
        clip.wrapMode = WrapMode.Loop;
        // Sprite curves include the final sample's duration. An extra closing
        // key at 1.0 s would make the loop 1.125 s and hold frame 0 too long.
        var keys = new ObjectReferenceKeyframe[frames.Length == 1 ? 2 : frames.Length];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = new ObjectReferenceKeyframe
            {
                time = frames.Length == 1 ? i * 0.875f : i / 8f,
                value = frames.Length == 1 ? frames[0] : frames[i]
            };
        AnimationUtility.SetObjectReferenceCurve(clip,
            EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        settings.loopBlend = false;
        settings.startTime = 0f;
        settings.stopTime = 1f;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static void Transition(AnimatorState from, AnimatorState to, AnimatorConditionMode condition)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = 0f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(condition, 0f, "IsMoving");
    }
}
