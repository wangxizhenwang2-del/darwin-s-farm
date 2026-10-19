using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PopulationFrameAnimationController))]
public sealed class PopulationFrameAnimationControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var player = (PopulationFrameAnimationController)target;
        PopulationAnimationSet set = player.AnimationSet;
        if (set == null)
        {
            AnimationLabController lab = player.GetComponentInParent<AnimationLabController>(true);
            if (lab != null) set = lab.AnimationFor(player);
        }

        serializedObject.Update();
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("动画默认配置", set, typeof(PopulationAnimationSet), false);
        if (set == null || !set.HasFrameSequences)
        {
            EditorGUILayout.HelpBox("请先在 Animation Lab 中绑定动画配置。", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("frameEvents"), true);
            serializedObject.ApplyModifiedProperties();
            return;
        }

        EditorGUILayout.HelpBox("帧号按图片自然排序后从 1 开始。这里修改的是当前动物；点击按钮可保存为该动画的默认值。", MessageType.None);
        bool changed = false;
        bool common = set.Mode == PopulationAnimationMode.CommonLoop;
        bool saveWalk = false;
        bool saveIdle = false;
        bool saveCommon = false;
        bool saveTransition = false;
        if (common)
            saveCommon = DrawTiming("通用动画", set.CommonFrames.Length, set.CommonStart + 1,
                set.CommonEnd + 1, set.CommonFps, "overrideCommonTiming", "commonStartFrame",
                "commonEndFrame", "commonFps", "commonStopFrame", set.CommonStopFrame, ref changed);
        else
        {
            saveWalk = DrawTiming("Walk 行走", set.WalkFrames.Length, set.WalkStart + 1,
                set.WalkEnd + 1, set.WalkFps, "overrideWalkTiming", "walkStartFrame",
                "walkEndFrame", "walkFps", null, 0, ref changed);
            saveIdle = DrawTiming("Idle 待机", set.IdleFrames.Length, set.IdleStart + 1,
                set.IdleEnd + 1, set.IdleFps, "overrideIdleTiming", "idleStartFrame",
                "idleEndFrame", "idleFps", "stopFrame", set.StopFrame, ref changed);
            if (set.HasTransition)
                saveTransition = DrawTiming("Transition 过渡", set.TransitionFrames.Length,
                    set.TransitionStart + 1, set.TransitionEnd + 1, set.TransitionFps,
                    "overrideTransitionTiming", "transitionStartFrame", "transitionEndFrame",
                    "transitionFps", null, 0, ref changed);
        }
        SerializedProperty useLocalFacing = serializedObject.FindProperty("overrideArtworkFacing");
        SerializedProperty facesLeft = serializedObject.FindProperty("artworkFacesLeft");
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("素材原始朝向", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        bool newFacesLeft = EditorGUILayout.Popup("图片面向", useLocalFacing.boolValue ?
            (facesLeft.boolValue ? 0 : 1) : (set.ArtworkFacesLeft ? 0 : 1),
            new[] { "左", "右" }) == 0;
        if (EditorGUI.EndChangeCheck())
        {
            facesLeft.boolValue = newFacesLeft;
            useLocalFacing.boolValue = true;
            changed = true;
        }
        EditorGUILayout.BeginHorizontal();
        bool saveFacing = GUILayout.Button("设为该动画默认值");
        using (new EditorGUI.DisabledScope(!useLocalFacing.boolValue))
            if (GUILayout.Button("恢复默认值"))
            {
                useLocalFacing.boolValue = false;
                changed = true;
            }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("frameEvents"), true);
        serializedObject.ApplyModifiedProperties();

        if (saveWalk) SaveDefaults(set, true);
        if (saveIdle) SaveDefaults(set, false);
        if (saveCommon) SaveCommonDefault(set);
        if (saveTransition) SaveTransitionDefault(set);
        if (saveFacing) SaveFacingDefault(set);
        if (changed || saveWalk || saveIdle || saveCommon || saveTransition || saveFacing)
        {
            foreach (PopulationFrameAnimationController item in Object.FindObjectsByType<PopulationFrameAnimationController>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (item == player || item.AnimationSet == set) item.RefreshTiming();
        }
    }

    private bool DrawTiming(string title, int count, int defaultStart, int defaultEnd,
        int defaultFps, string overrideName, string startName, string endName, string fpsName,
        string stopName, int defaultStop, ref bool changed)
    {
        SerializedProperty useLocal = serializedObject.FindProperty(overrideName);
        SerializedProperty start = serializedObject.FindProperty(startName);
        SerializedProperty end = serializedObject.FindProperty(endName);
        SerializedProperty fps = serializedObject.FindProperty(fpsName);
        SerializedProperty stop = stopName == null ? null : serializedObject.FindProperty(stopName);
        int currentStart = useLocal.boolValue ? start.intValue : defaultStart;
        int currentEnd = useLocal.boolValue ? end.intValue : defaultEnd;
        int currentFps = useLocal.boolValue ? fps.intValue : defaultFps;
        int currentStop = stop == null ? 0 : useLocal.boolValue ? stop.intValue : defaultStop;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(title + "  (共 " + count + " 帧)", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        int newStart = EditorGUILayout.IntField("起始帧", currentStart);
        int newEnd = EditorGUILayout.IntField("结束帧", currentEnd);
        int newFps = EditorGUILayout.IntField("帧率 FPS", currentFps);
        int newStop = stop == null ? 0 : EditorGUILayout.IntField("停止定格帧", currentStop);
        if (EditorGUI.EndChangeCheck())
        {
            start.intValue = Mathf.Clamp(newStart, 1, count);
            end.intValue = Mathf.Clamp(newEnd, start.intValue, count);
            fps.intValue = Mathf.Max(1, newFps);
            if (stop != null) stop.intValue = Mathf.Clamp(newStop, 1, count);
            useLocal.boolValue = true;
            changed = true;
        }
        EditorGUILayout.BeginHorizontal();
        bool save = GUILayout.Button("设为该动画默认值");
        using (new EditorGUI.DisabledScope(!useLocal.boolValue))
            if (GUILayout.Button("恢复默认值"))
            {
                useLocal.boolValue = false;
                changed = true;
            }
        EditorGUILayout.EndHorizontal();
        return save;
    }

    private void SaveDefaults(PopulationAnimationSet set, bool walk)
    {
        string prefix = walk ? "walk" : "idle";
        bool useLocal = serializedObject.FindProperty(walk ? "overrideWalkTiming" : "overrideIdleTiming").boolValue;
        if (!useLocal) return;
        var asset = new SerializedObject(set);
        asset.FindProperty(prefix + "StartFrame").intValue = serializedObject.FindProperty(prefix + "StartFrame").intValue;
        asset.FindProperty(prefix + "EndFrame").intValue = serializedObject.FindProperty(prefix + "EndFrame").intValue;
        asset.FindProperty(prefix + "Fps").intValue = serializedObject.FindProperty(prefix + "Fps").intValue;
        if (!walk)
            asset.FindProperty("stopFrame").intValue = serializedObject.FindProperty("stopFrame").intValue;
        asset.ApplyModifiedProperties();
        EditorUtility.SetDirty(set);
        EightAnimalAnimationLabBuilder.RefreshClips(set);
        serializedObject.FindProperty(walk ? "overrideWalkTiming" : "overrideIdleTiming").boolValue = false;
        serializedObject.ApplyModifiedProperties();
    }

    private void SaveFacingDefault(PopulationAnimationSet set)
    {
        SerializedProperty useLocal = serializedObject.FindProperty("overrideArtworkFacing");
        if (!useLocal.boolValue) return;
        var asset = new SerializedObject(set);
        asset.FindProperty("artworkFacesLeft").boolValue =
            serializedObject.FindProperty("artworkFacesLeft").boolValue;
        asset.ApplyModifiedProperties();
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        useLocal.boolValue = false;
        serializedObject.ApplyModifiedProperties();
    }

    private void SaveCommonDefault(PopulationAnimationSet set)
    {
        SerializedProperty useLocal = serializedObject.FindProperty("overrideCommonTiming");
        if (!useLocal.boolValue) return;
        var asset = new SerializedObject(set);
        foreach (string name in new[] { "commonStartFrame", "commonEndFrame", "commonFps", "commonStopFrame" })
            asset.FindProperty(name).intValue = serializedObject.FindProperty(name).intValue;
        asset.ApplyModifiedProperties();
        EditorUtility.SetDirty(set);
        EightAnimalAnimationLabBuilder.RefreshClips(set);
        useLocal.boolValue = false;
        serializedObject.ApplyModifiedProperties();
    }

    private void SaveTransitionDefault(PopulationAnimationSet set)
    {
        SerializedProperty useLocal = serializedObject.FindProperty("overrideTransitionTiming");
        if (!useLocal.boolValue) return;
        var asset = new SerializedObject(set);
        foreach (string name in new[] { "transitionStartFrame", "transitionEndFrame", "transitionFps" })
            asset.FindProperty(name).intValue = serializedObject.FindProperty(name).intValue;
        asset.ApplyModifiedProperties();
        EditorUtility.SetDirty(set);
        EightAnimalAnimationLabBuilder.RefreshClips(set);
        useLocal.boolValue = false;
        serializedObject.ApplyModifiedProperties();
    }
}
