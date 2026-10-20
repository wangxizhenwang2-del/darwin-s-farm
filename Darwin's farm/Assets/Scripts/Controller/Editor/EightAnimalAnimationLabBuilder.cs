using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EightAnimalAnimationLabBuilder
{
    private const string Source = "Assets/Animation";
    private const string Output = "Assets/Art/Animals/Animations/EightAnimalLab";
    private const string ScenePath = "Assets/Scenes/EightAnimalAnimationLab.unity";

    private sealed class Entry
    {
        public readonly string name, idleFolder, walkFolder, speciesPath, transitionFolder;
        public readonly int idleFps, walkFps;
        public readonly bool artworkFacesLeft;
        public readonly bool stopAtLastIdleFrame;
        public Entry(string name, string idleFolder, string walkFolder, int idleFps, int walkFps,
            string speciesPath, string transitionFolder = null, bool artworkFacesLeft = true,
            bool stopAtLastIdleFrame = false)
        {
            this.name = name; this.idleFolder = idleFolder; this.walkFolder = walkFolder;
            this.idleFps = idleFps; this.walkFps = walkFps; this.speciesPath = speciesPath;
            this.transitionFolder = transitionFolder; this.artworkFacesLeft = artworkFacesLeft;
            this.stopAtLastIdleFrame = stopAtLastIdleFrame;
        }
    }

    private static readonly Entry[] Animals =
    {
        new Entry("大鲨母", "大鲨母 待机 fps8", "大鲨母 移动 fps8", 8, 8,
            "Assets/BiowebV46/Species/大鲨母-0143fd10.asset"),
        new Entry("鸽鸽", "鸽鸽 待机 fps6", "鸽鸽 移动 fps6", 6, 6,
            "Assets/BiowebV46/Species/鸡-7f113739.asset"),
        new Entry("鸡雀", "鸡雀 待机 fps12", "鸡雀 走路 fps12", 12, 12,
            "Assets/BiowebV46/Species/彩鸡-b59a4a82.asset"),
        new Entry("鹿", "鹿 待机 fps8", "鹿 走路 fps8", 8, 8,
            "Assets/BiowebV46/Species/鹿-fd1c574b.asset"),
        new Entry("鹿飞", "鹿飞 待机 fps8", "鹿飞 移动 fps8", 8, 8,
            "Assets/BiowebV46/Species/鹿飞-a61486cd.asset"),
        new Entry("鹿驯", "鹿驯 待机 fps7", "鹿驯 移动 fps7", 7, 7,
            "Assets/BiowebV46/Species/鹿驯-b2afc73a.asset"),
        new Entry("雀狮", "雀狮 待机 fps8", "雀狮 移动 fps8", 8, 8,
            "Assets/BiowebV46/Species/雀狮-ad3c3d0f.asset", stopAtLastIdleFrame: true),
        new Entry("雪狼", "雪狼 待机 fps6", "雪狼 移动 fps6", 6, 6,
            "Assets/BiowebV46/Species/雪狼-fb2d8345.asset"),
        new Entry("鲨母", "鲨母 待机 fps12", "鲨母 移动 fps12", 12, 12,
            "Assets/BiowebV46/Species/鲨母-6b5c947b.asset", artworkFacesLeft: false),
        new Entry("哈激麟", "哈激麟 待机 fps12", "哈激麟 移动 fps12", 12, 12,
            "Assets/BiowebV46/Species/哈激麟-b2e8cdba.asset", artworkFacesLeft: false),
    };

    private sealed class CommonEntry
    {
        public readonly string name, folder, speciesPath;
        public readonly int fps;
        public CommonEntry(string name, string folder, int fps, string speciesPath)
        { this.name = name; this.folder = folder; this.fps = fps; this.speciesPath = speciesPath; }
    }

    private static readonly CommonEntry[] CommonAnimals =
    {
        new CommonEntry("鹿游", "鹿游 通用 fps7", 7, "Assets/BiowebV46/Species/鹿游-103a527e.asset"),
        new CommonEntry("鲨鹿", "鲨鹿 通用 fps7", 7, "Assets/BiowebV46/Species/鲨鹿-080b0789.asset"),
        new CommonEntry("虾兔", "虾兔 通用 fps10", 10, "Assets/BiowebV46/Species/虾兔-c60ed314.asset"),
    };

    [MenuItem("Tools/Darwin's Farm/Animation/Build Animation Lab")]
    public static void Build()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EnsureFolder("Assets/Art/Animals/Animations", "EightAnimalLab");
        var sets = new PopulationAnimationSet[Animals.Length + CommonAnimals.Length];
        for (int i = 0; i < Animals.Length; i++)
            sets[i] = BuildAnimal(Animals[i]);
        for (int i = 0; i < CommonAnimals.Length; i++)
            sets[Animals.Length + i] = BuildCommonAnimal(CommonAnimals[i]);
        AssetDatabase.SaveAssets();
        BuildScene(sets);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Verify();
        Debug.Log($"AnimationLab: built {Animals.Length} walk/idle and {CommonAnimals.Length} common animation sets with scene.");
    }

    public static void Verify()
    {
        foreach (Entry animal in Animals)
        {
            string folder = Output + "/" + animal.name;
            var set = AssetDatabase.LoadAssetAtPath<PopulationAnimationSet>(folder + "/" + animal.name + "Animation.asset");
            if (set == null || !set.HasFrameSequences || !set.IsUsable ||
                set.WalkEnd < set.WalkStart || set.IdleEnd < set.IdleStart)
                throw new InvalidOperationException("Incomplete animation set: " + animal.name);
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(folder + "/Walk.anim") == null ||
                AssetDatabase.LoadAssetAtPath<AnimationClip>(folder + "/Idle.anim") == null ||
                AssetDatabase.LoadAssetAtPath<AnimatorController>(folder + "/Locomotion.controller") == null)
                throw new InvalidOperationException("Missing clips/controller: " + animal.name);
            SpeciesData species = AssetDatabase.LoadAssetAtPath<SpeciesData>(animal.speciesPath);
            if (File.Exists(animal.speciesPath) && (species == null || species.PopulationAnimation != set))
                throw new InvalidOperationException("Species not bound: " + animal.name);
        }
        foreach (CommonEntry animal in CommonAnimals)
        {
            string folder = Output + "/" + animal.name;
            var set = AssetDatabase.LoadAssetAtPath<PopulationAnimationSet>(folder + "/" + animal.name + "Animation.asset");
            if (set == null || set.Mode != PopulationAnimationMode.CommonLoop ||
                !set.HasFrameSequences || !set.IsUsable ||
                AssetDatabase.LoadAssetAtPath<AnimationClip>(folder + "/Common.anim") == null)
                throw new InvalidOperationException("Incomplete common animation: " + animal.name);
            SpeciesData species = AssetDatabase.LoadAssetAtPath<SpeciesData>(animal.speciesPath);
            if (File.Exists(animal.speciesPath) && (species == null || species.PopulationAnimation != set))
                throw new InvalidOperationException("Common species not bound: " + animal.name);
        }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            throw new InvalidOperationException("Animation lab scene was not saved.");
        Scene labScene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !labScene.isLoaded;
        if (openedHere) labScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            GameObject labObject = labScene.GetRootGameObjects().FirstOrDefault(item => item.name == "Animation Lab");
            AnimationLabController lab = labObject != null ? labObject.GetComponent<AnimationLabController>() : null;
            if (lab == null) throw new InvalidOperationException("Animation lab controller is missing.");
            var serialized = new SerializedObject(lab);
            SerializedProperty entries = serialized.FindProperty("animals");
            if (entries.arraySize != Animals.Length + CommonAnimals.Length)
                throw new InvalidOperationException("Animation lab animal count is wrong.");
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("animations").objectReferenceValue == null ||
                    entry.FindPropertyRelative("root").objectReferenceValue == null ||
                    entry.FindPropertyRelative("sprite").objectReferenceValue == null ||
                    entry.FindPropertyRelative("player").objectReferenceValue == null)
                    throw new InvalidOperationException("Animation lab reference is missing at index " + i);
            }
            GameObject cameraObject = labScene.GetRootGameObjects().FirstOrDefault(item => item.name == "Animation Lab Camera");
            Type cameraDataType = Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (cameraObject == null || cameraDataType != null && cameraObject.GetComponent(cameraDataType) == null)
                throw new InvalidOperationException("Animation lab camera data is missing.");
        }
        finally { if (openedHere) EditorSceneManager.CloseScene(labScene, true); }
        var fixture = new GameObject("Frame scheduler verification");
        try
        {
            var sprite = fixture.AddComponent<SpriteRenderer>();
            var scheduler = fixture.AddComponent<PopulationFrameAnimationController>();
            scheduler.Bind(sprite, AssetDatabase.LoadAssetAtPath<PopulationAnimationSet>(
                Output + "/鹿/鹿Animation.asset"), Color.white);
            scheduler.RequestStart();
            float cycle = scheduler.AnimationSet.WalkCycleSeconds;
            if (!scheduler.BeginWalk(4f, 2f)) throw new InvalidOperationException("Walk did not start.");
            float first = scheduler.Tick(cycle * 0.5f, 0f);
            scheduler.RequestStop();
            float second = scheduler.Tick(cycle * 0.5f, 0f);
            if (first <= 0f || second <= 0f || scheduler.Phase != PopulationFramePhase.Hold ||
                !Mathf.Approximately(first, second))
                throw new InvalidOperationException("Stop did not wait for the walk boundary.");
            scheduler.Bind(sprite, AssetDatabase.LoadAssetAtPath<PopulationAnimationSet>(
                Output + "/鹿游/鹿游Animation.asset"), Color.white);
            if (!scheduler.IsCommonLoop || scheduler.Phase != PopulationFramePhase.CommonAction ||
                scheduler.Tick(scheduler.AnimationSet.CommonCycleSeconds, 100f) != 0f ||
                scheduler.Phase != PopulationFramePhase.Hold)
                throw new InvalidOperationException("Common action did not stop without movement.");
            scheduler.RequestStop();
            scheduler.Tick(0.1f, 100.5f);
            if (scheduler.Phase != PopulationFramePhase.Hold)
                throw new InvalidOperationException("Common hold ended before one second.");
            scheduler.Tick(0.1f, 104f);
            if (scheduler.Phase != PopulationFramePhase.CommonAction)
                throw new InvalidOperationException("Common action did not resume after hold.");
            scheduler.Bind(sprite, AssetDatabase.LoadAssetAtPath<PopulationAnimationSet>(
                Output + "/雀狮/雀狮Animation.asset"), Color.white);
            if (scheduler.HasTransition || scheduler.AnimationSet.StopFrame != scheduler.AnimationSet.IdleFrames.Length)
                throw new InvalidOperationException("Sparrow lion should use the last idle frame without a transition.");
            scheduler.RequestStart();
            float walkCycle = scheduler.WalkCycleSeconds;
            if (!scheduler.BeginWalk(10f, 2f))
                throw new InvalidOperationException("Sparrow lion did not begin walking.");
            scheduler.Tick(walkCycle * 0.5f, 0f);
            scheduler.RequestStop();
            scheduler.Tick(walkCycle * 0.5f, 0f);
            if (scheduler.Phase != PopulationFramePhase.Hold ||
                sprite.sprite != scheduler.AnimationSet.IdleFrames[scheduler.AnimationSet.IdleEnd])
                throw new InvalidOperationException("Sparrow lion did not stop on the last idle frame.");
        }
        finally { UnityEngine.Object.DestroyImmediate(fixture); }
    }

    private static PopulationAnimationSet BuildAnimal(Entry animal)
    {
        string folder = Output + "/" + animal.name;
        EnsureFolder(Output, animal.name);
        Sprite[] idle = ImportFrames(Source + "/" + animal.idleFolder);
        Sprite[] walk = ImportFrames(Source + "/" + animal.walkFolder);
        if (idle.Length == 0 || walk.Length == 0)
            throw new InvalidOperationException("Missing paired frames: " + animal.name);

        string setPath = folder + "/" + animal.name + "Animation.asset";
        PopulationAnimationSet set = AssetDatabase.LoadAssetAtPath<PopulationAnimationSet>(setPath);
        bool created = set == null;
        if (created)
        {
            set = ScriptableObject.CreateInstance<PopulationAnimationSet>();
            AssetDatabase.CreateAsset(set, setPath);
        }
        var serialized = new SerializedObject(set);
        SetArray(serialized.FindProperty("idleFrames"), idle);
        SetArray(serialized.FindProperty("walkFrames"), walk);
        if (animal.transitionFolder != null)
        {
            Sprite[] transition = ImportFrames(Source + "/" + animal.transitionFolder);
            SetArray(serialized.FindProperty("transitionFrames"), transition);
            if (created)
            {
                serialized.FindProperty("transitionStartFrame").intValue = 1;
                serialized.FindProperty("transitionEndFrame").intValue = transition.Length;
                serialized.FindProperty("transitionFps").intValue = animal.walkFps;
            }
        }
        else
        {
            serialized.FindProperty("transitionFrames").arraySize = 0;
            string obsoleteTransition = folder + "/Transition.anim";
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(obsoleteTransition) != null)
                AssetDatabase.DeleteAsset(obsoleteTransition);
        }
        if (created)
        {
            serialized.FindProperty("idleStartFrame").intValue = 1;
            serialized.FindProperty("idleEndFrame").intValue = idle.Length;
            serialized.FindProperty("walkStartFrame").intValue = 1;
            serialized.FindProperty("walkEndFrame").intValue = walk.Length;
            serialized.FindProperty("stopFrame").intValue = animal.stopAtLastIdleFrame ? idle.Length : 1;
            serialized.FindProperty("idleFps").intValue = animal.idleFps;
            serialized.FindProperty("walkFps").intValue = animal.walkFps;
            serialized.FindProperty("referenceMoveSpeed").floatValue = 1.8f;
            serialized.FindProperty("artworkFacesLeft").boolValue = animal.artworkFacesLeft;
        }
        if (animal.stopAtLastIdleFrame)
            serialized.FindProperty("stopFrame").intValue = idle.Length;
        serialized.FindProperty("idleSprite").objectReferenceValue =
            animal.stopAtLastIdleFrame ? idle[idle.Length - 1] : idle[0];
        serialized.ApplyModifiedPropertiesWithoutUndo();

        AnimationClip hold = SpriteClip(folder + "/Hold.anim", new[] { set.StopSprite }, 1, false);
        AnimationClip idleClip = SpriteClip(folder + "/Idle.anim",
            idle.Skip(set.IdleStart).Take(set.IdleEnd - set.IdleStart + 1).ToArray(), set.IdleFps, false);
        AnimationClip walkClip = SpriteClip(folder + "/Walk.anim",
            walk.Skip(set.WalkStart).Take(set.WalkEnd - set.WalkStart + 1).ToArray(), set.WalkFps, true);
        AnimationClip transitionClip = null;
        if (animal.transitionFolder != null)
            transitionClip = SpriteClip(folder + "/Transition.anim",
                set.TransitionFrames.Skip(set.TransitionStart)
                    .Take(set.TransitionEnd - set.TransitionStart + 1).ToArray(), set.TransitionFps, false);
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(folder + "/Locomotion.controller");
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Locomotion.controller");
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState state in machine.states) machine.RemoveState(state.state);
        AnimatorState holdState = machine.AddState("Hold"); holdState.motion = hold;
        AnimatorState idleState = machine.AddState("IdleAction"); idleState.motion = idleClip;
        AnimatorState walkState = machine.AddState("Walk"); walkState.motion = walkClip;
        if (transitionClip != null)
        {
            AnimatorState transitionState = machine.AddState("Transition");
            transitionState.motion = transitionClip;
        }
        machine.defaultState = holdState;
        EditorUtility.SetDirty(controller);
        serialized = new SerializedObject(set);
        serialized.FindProperty("controller").objectReferenceValue = controller;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        PopulationAnimationSizing.Bake(set);
        if (!File.Exists(animal.speciesPath)) return set; // Standalone asset-build project.
        SpeciesData species = AssetDatabase.LoadAssetAtPath<SpeciesData>(animal.speciesPath);
        if (species == null) throw new InvalidOperationException("Missing species asset: " + animal.speciesPath);
        var speciesData = new SerializedObject(species);
        speciesData.FindProperty("populationAnimation").objectReferenceValue = set;
        if (animal.name == "鸽鸽") speciesData.FindProperty("speciesName").stringValue = "鸽鸽";
        speciesData.ApplyModifiedPropertiesWithoutUndo();
        return set;
    }

    private static PopulationAnimationSet BuildCommonAnimal(CommonEntry animal)
    {
        string folder = Output + "/" + animal.name;
        EnsureFolder(Output, animal.name);
        Sprite[] frames = ImportFrames(Source + "/" + animal.folder);
        if (frames.Length == 0) throw new InvalidOperationException("Missing common frames: " + animal.name);
        string setPath = folder + "/" + animal.name + "Animation.asset";
        PopulationAnimationSet set = AssetDatabase.LoadAssetAtPath<PopulationAnimationSet>(setPath);
        bool created = set == null;
        if (created)
        {
            set = ScriptableObject.CreateInstance<PopulationAnimationSet>();
            AssetDatabase.CreateAsset(set, setPath);
        }
        var serialized = new SerializedObject(set);
        serialized.FindProperty("mode").enumValueIndex = (int)PopulationAnimationMode.CommonLoop;
        SetArray(serialized.FindProperty("commonFrames"), frames);
        if (created)
        {
            serialized.FindProperty("commonStartFrame").intValue = 1;
            serialized.FindProperty("commonEndFrame").intValue = frames.Length;
            serialized.FindProperty("commonStopFrame").intValue = 1;
            serialized.FindProperty("commonFps").intValue = animal.fps;
            serialized.FindProperty("commonHoldMin").floatValue = 1f;
            serialized.FindProperty("commonHoldMax").floatValue = 3f;
        }
        serialized.FindProperty("idleSprite").objectReferenceValue = frames[0];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AnimationClip hold = SpriteClip(folder + "/Hold.anim", new[] { set.StopSprite }, 1, false);
        AnimationClip common = SpriteClip(folder + "/Common.anim", frames.Skip(set.CommonStart)
            .Take(set.CommonEnd - set.CommonStart + 1).ToArray(), set.CommonFps, false);
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(folder + "/Locomotion.controller");
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Locomotion.controller");
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState state in machine.states) machine.RemoveState(state.state);
        AnimatorState holdState = machine.AddState("Hold"); holdState.motion = hold;
        AnimatorState actionState = machine.AddState("CommonAction"); actionState.motion = common;
        machine.defaultState = holdState;
        EditorUtility.SetDirty(controller);
        serialized = new SerializedObject(set);
        serialized.FindProperty("controller").objectReferenceValue = controller;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        PopulationAnimationSizing.Bake(set);
        if (!File.Exists(animal.speciesPath)) return set;
        SpeciesData species = AssetDatabase.LoadAssetAtPath<SpeciesData>(animal.speciesPath);
        if (species == null) throw new InvalidOperationException("Missing species asset: " + animal.speciesPath);
        var speciesData = new SerializedObject(species);
        speciesData.FindProperty("populationAnimation").objectReferenceValue = set;
        speciesData.ApplyModifiedPropertiesWithoutUndo();
        return set;
    }

    private static Sprite[] ImportFrames(string folder)
    {
        string[] files = Directory.GetFiles(folder, "*.png")
            .Select(path => path.Replace('\\', '/'))
            .OrderBy(path => TrailingNumber(path)).ThenBy(path => path, StringComparer.Ordinal).ToArray();
        var sprites = new Sprite[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            TextureImporter importer = AssetImporter.GetAtPath(files[i]) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Not imported: " + files[i]);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(files[i]);
            float pixelsPerUnit = texture == null ? 100f : texture.height / 1.5f;
            if (importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != SpriteImportMode.Single ||
                !Mathf.Approximately(importer.spritePixelsPerUnit, pixelsPerUnit) || importer.mipmapEnabled)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = pixelsPerUnit;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(files[i]);
            if (sprites[i] == null) throw new InvalidOperationException("Sprite import failed: " + files[i]);
        }
        return sprites;
    }

    private static int TrailingNumber(string path)
    {
        Match match = Regex.Match(Path.GetFileNameWithoutExtension(path), @"(\d+)$");
        return match.Success && int.TryParse(match.Value, out int value) ? value : int.MaxValue;
    }

    private static void SetArray(SerializedProperty property, Sprite[] sprites)
    {
        property.arraySize = sprites.Length;
        for (int i = 0; i < sprites.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
    }

    private static AnimationClip SpriteClip(string path, Sprite[] frames, int fps, bool loop)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.frameRate = fps;
        clip.wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
        var keys = new ObjectReferenceKeyframe[frames.Length + 1];
        for (int i = 0; i < frames.Length; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i / (float)fps, value = frames[i] };
        keys[frames.Length] = new ObjectReferenceKeyframe
        { time = frames.Length / (float)fps, value = loop ? frames[0] : frames[frames.Length - 1] };
        AnimationUtility.SetObjectReferenceCurve(clip,
            EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        settings.startTime = 0f;
        settings.stopTime = frames.Length / (float)fps;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    public static void RefreshClips(PopulationAnimationSet set)
    {
        if (set == null || !set.HasFrameSequences) return;
        string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(set))?.Replace('\\', '/');
        if (string.IsNullOrEmpty(folder)) return;
        SpriteClip(folder + "/Hold.anim", new[] { set.StopSprite }, 1, false);
        if (set.Mode == PopulationAnimationMode.CommonLoop)
        {
            SpriteClip(folder + "/Common.anim", set.CommonFrames.Skip(set.CommonStart)
                .Take(set.CommonEnd - set.CommonStart + 1).ToArray(), set.CommonFps, false);
            AssetDatabase.SaveAssets();
            return;
        }
        SpriteClip(folder + "/Idle.anim", set.IdleFrames.Skip(set.IdleStart)
            .Take(set.IdleEnd - set.IdleStart + 1).ToArray(), set.IdleFps, false);
        SpriteClip(folder + "/Walk.anim", set.WalkFrames.Skip(set.WalkStart)
            .Take(set.WalkEnd - set.WalkStart + 1).ToArray(), set.WalkFps, true);
        if (set.HasTransition)
            SpriteClip(folder + "/Transition.anim", set.TransitionFrames.Skip(set.TransitionStart)
                .Take(set.TransitionEnd - set.TransitionStart + 1).ToArray(), set.TransitionFps, false);
        AssetDatabase.SaveAssets();
    }

    private static void BuildScene(PopulationAnimationSet[] sets)
    {
        if (File.Exists(ScenePath)) { RepairScene(sets); return; }
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        GameObject cameraObject = new GameObject("Animation Lab Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 3.6f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.13f, 0.16f, 0.2f);
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        GameObject root = new GameObject("Animation Lab");
        AnimationLabController lab = root.AddComponent<AnimationLabController>();
        var entries = new AnimationLabController.Animal[sets.Length];
        for (int i = 0; i < sets.Length; i++)
        {
            string name = i < Animals.Length ? Animals[i].name : CommonAnimals[i - Animals.Length].name;
            GameObject animal = new GameObject(name);
            animal.transform.SetParent(root.transform);
            SetPreviewPosition(animal.transform, name);
            SpriteRenderer sprite = animal.AddComponent<SpriteRenderer>();
            PopulationSpriteDisplay.Configure(sprite, sets[i].StopSprite, Color.white, sets[i].ReferenceVisualHeight);
            animal.AddComponent<Animator>().runtimeAnimatorController = sets[i].Controller;
            animal.GetComponent<Animator>().enabled = false; // Frame scheduler owns exact playback.
            PopulationFrameAnimationController player = animal.AddComponent<PopulationFrameAnimationController>();
            entries[i] = new AnimationLabController.Animal
            {
                displayName = name,
                animations = sets[i],
                root = animal,
                sprite = sprite,
                player = player
            };
        }
        lab.Configure(entries);
        EditorUtility.SetDirty(lab);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.CloseScene(scene, true);
        RepairScene(sets);
    }

    private static void RepairScene(PopulationAnimationSet[] sets)
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            GameObject labObject = scene.GetRootGameObjects().FirstOrDefault(item => item.name == "Animation Lab");
            if (labObject == null) throw new InvalidOperationException("Animation Lab root is missing.");
            AnimationLabController lab = labObject.GetComponent<AnimationLabController>();
            var entries = new AnimationLabController.Animal[sets.Length];
            for (int i = 0; i < sets.Length; i++)
            {
                string name = i < Animals.Length ? Animals[i].name : CommonAnimals[i - Animals.Length].name;
                Transform animal = labObject.transform.Find(name);
                if (animal == null)
                {
                    GameObject created = new GameObject(name);
                    created.transform.SetParent(labObject.transform, false);
                    SetPreviewPosition(created.transform, name);
                    SpriteRenderer sprite = created.AddComponent<SpriteRenderer>();
                    PopulationSpriteDisplay.Configure(sprite, sets[i].StopSprite, Color.white, sets[i].ReferenceVisualHeight);
                    Animator animator = created.AddComponent<Animator>();
                    animator.runtimeAnimatorController = sets[i].Controller;
                    animator.enabled = false;
                    created.AddComponent<PopulationFrameAnimationController>();
                    animal = created.transform;
                }
                entries[i] = new AnimationLabController.Animal
                {
                    displayName = name,
                    animations = sets[i],
                    root = animal.gameObject,
                    sprite = animal.GetComponent<SpriteRenderer>(),
                    player = animal.GetComponent<PopulationFrameAnimationController>()
                };
            }
            lab.Configure(entries);
            EditorUtility.SetDirty(lab);
            GameObject cameraObject = scene.GetRootGameObjects().FirstOrDefault(item => item.name == "Animation Lab Camera");
            Type cameraDataType = Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (cameraObject != null && cameraDataType != null && cameraObject.GetComponent(cameraDataType) == null)
                cameraObject.AddComponent(cameraDataType);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
    }

    private static void SetPreviewPosition(Transform animal, string name)
    {
        if (name == "鲨母") animal.localPosition = new Vector3(-2.3f, 0f, 0f);
        else if (name == "哈激麟") animal.localPosition = new Vector3(2.3f, 0f, 0f);
    }
}
