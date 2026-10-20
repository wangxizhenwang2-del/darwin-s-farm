using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public sealed class UILayoutAuthoringWindow : EditorWindow
{
    private const string AssetPath = "Assets/Resources/GameUI/UILayoutOverrides.asset";
    private static readonly Dictionary<int, UILayoutOverrides.Entry> baseline = new Dictionary<int, UILayoutOverrides.Entry>();
    private static readonly HashSet<int> edited = new HashSet<int>();
    private static double nextScan;
    private static string status = "位置、尺寸、字体、颜色和布局参数可以保存；游戏数据与按钮事件保持动态。";
    private static string AutoKey => "Darwin.SavePlayUILayout." + Application.dataPath;

    static UILayoutAuthoringWindow()
    {
        Undo.postprocessModifications += OnModified;
        EditorApplication.update += Scan;
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorSceneManager.sceneSaved += OnSceneSaved;
    }

    [MenuItem("Tools/Darwin Farm/UI/Save Play UI Layout")]
    public static void Open() => GetWindow<UILayoutAuthoringWindow>("保存 Play UI");

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(status, MessageType.Info);
        bool auto = EditorPrefs.GetBool(AutoKey, true);
        bool wanted = EditorGUILayout.ToggleLeft("退出 Play 时自动保存 Inspector / Scene 中手动修改的 UI", auto);
        if (wanted != auto) EditorPrefs.SetBool(AutoKey, wanted);
        EditorGUILayout.LabelField("检测到手动修改的 UI 组件", edited.Count.ToString());
        using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
        {
            if (GUILayout.Button("保存检测到的 UI 调整")) SaveTracked();
            if (GUILayout.Button("保存当前选中 UI 的布局与样式")) SaveSelection();
        }
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("将已保存调整应用到当前编辑场景")) ApplyToOpenScenes();
        if (GUILayout.Button("选中布局保存资源"))
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UILayoutOverrides>(AssetPath);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("生成缺失的物种面板布局（编辑模式）")) PreparePopulationPanels();
        EditorGUILayout.HelpBox("适用于所有 Canvas 下的 Unity UI。运行时生成的节点在下次生成后恢复样式。" +
            "若某个第三方 Inspector 没有发送 Undo 修改事件，可选中对应 UI 后使用第二个保存按钮。" +
            "文本内容、物种头像、显隐、选择状态和游戏进度不会保存。", MessageType.None);
    }

    private static IEnumerable<Component> Components()
    {
        var seen = new HashSet<int>();
        foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
        {
            if (!canvas.gameObject.scene.IsValid() || EditorUtility.IsPersistent(canvas)) continue;
            foreach (var component in canvas.GetComponentsInChildren<Component>(true))
                if (component != null && seen.Add(component.GetInstanceID()) &&
                    UILayoutOverrides.Properties(component).Length > 0) yield return component;
        }
    }

    private static UILayoutOverrides.Entry Capture(Component target)
    {
        var siblings = target.GetComponents(target.GetType());
        return new UILayoutOverrides.Entry
        {
            scene = target.gameObject.scene.name == "DontDestroyOnLoad" ? UILayoutOverrides.PersistentScene : target.gameObject.scene.path,
            path = UILayoutOverrides.PathOf(target.transform),
            component = target.GetType().FullName + ", " + target.GetType().Assembly.GetName().Name,
            componentIndex = Array.IndexOf(siblings, target),
            values = UILayoutOverrides.Capture(target)
        };
    }

    private static void Scan()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < nextScan) return;
        nextScan = EditorApplication.timeSinceStartup + .5;
        foreach (var component in Components())
            if (!baseline.ContainsKey(component.GetInstanceID())) baseline[component.GetInstanceID()] = Capture(component);
    }

    private static UndoPropertyModification[] OnModified(UndoPropertyModification[] changes)
    {
        if (!EditorApplication.isPlaying) return changes;
        foreach (var change in changes)
            if (change.currentValue.target is Component target &&
                target.GetComponentInParent<Canvas>(true) != null && UILayoutOverrides.Properties(target).Length > 0)
                edited.Add(target.GetInstanceID());
        return changes;
    }

    private static void OnPlayMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        { baseline.Clear(); edited.Clear(); nextScan = 0; }
        else if (state == PlayModeStateChange.ExitingPlayMode && EditorPrefs.GetBool(AutoKey, true)) SaveTracked();
        else if (state == PlayModeStateChange.EnteredEditMode)
        { baseline.Clear(); edited.Clear(); EditorApplication.delayCall += ApplyToOpenScenes; }
    }

    public static void SaveTracked()
    {
        var pending = new List<UILayoutOverrides.Entry>();
        foreach (var component in Components())
        {
            int id = component.GetInstanceID();
            if (!edited.Contains(id) || !baseline.TryGetValue(id, out var before)) continue;
            var now = Capture(component);
            now.values.RemoveAll(value => before.values.Any(old => old.property == value.property && UILayoutOverrides.Same(old, value)));
            if (now.values.Count > 0) pending.Add(now);
        }
        Save(pending);
    }

    private static void SaveSelection()
    {
        var selected = new HashSet<GameObject>(Selection.gameObjects);
        Save(Components().Where(component => selected.Contains(component.gameObject)).Select(Capture).ToList());
    }

    private static void Save(List<UILayoutOverrides.Entry> pending)
    {
        pending.RemoveAll(entry => string.IsNullOrEmpty(entry.scene) || entry.values.Count == 0);
        if (pending.Count == 0) { status = "没有可保存的手动 UI 调整。未保存的临时场景请先保存一次。"; return; }
        var settings = AssetDatabase.LoadAssetAtPath<UILayoutOverrides>(AssetPath);
        if (settings == null)
        {
            settings = CreateInstance<UILayoutOverrides>();
            AssetDatabase.CreateAsset(settings, AssetPath);
        }
        Undo.RecordObject(settings, "Save Play UI layout");
        foreach (var entry in pending)
        {
            var existing = settings.entries.Find(item => item.scene == entry.scene && item.path == entry.path &&
                item.component == entry.component && item.componentIndex == entry.componentIndex);
            if (existing == null) settings.entries.Add(entry);
            else foreach (var value in entry.values)
            { existing.values.RemoveAll(old => old.property == value.property); existing.values.Add(value); }
        }
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
        status = "已保存 " + pending.Count + " 个 UI 组件的调整。退出 Play 后会应用到现有场景节点。";
        Debug.Log(status);
    }

    public static void ApplyToOpenScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var settings = AssetDatabase.LoadAssetAtPath<UILayoutOverrides>(AssetPath);
        if (settings == null) return;
        foreach (var entry in settings.entries)
        {
            Component target = UILayoutOverrides.Resolve(entry);
            if (target == null) continue;
            var current = UILayoutOverrides.Capture(target);
            if (entry.values.All(value => current.Any(now => now.property == value.property && UILayoutOverrides.Same(now, value)))) continue;
            // Leave unrelated scene edits intact and allow normal Ctrl+S / Undo.
            Undo.RecordObject(target, "Apply saved Play UI layout");
            UILayoutOverrides.Apply(target, entry);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            EditorUtility.SetDirty(target);
            EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
        }
    }

    public static void PreparePopulationPanels()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach (var panel in UnityEngine.Object.FindObjectsByType<PopulationInfoPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (EditorUtility.IsPersistent(panel)) continue;
            panel.Initialize(panel.GetComponentInParent<InformationWindow>(true));
            panel.ShowUnavailable("点击种群查看");
            EditorUtility.SetDirty(panel);
            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        }
    }

    private static void OnSceneSaved(UnityEngine.SceneManagement.Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var settings = AssetDatabase.LoadAssetAtPath<UILayoutOverrides>(AssetPath);
        if (settings == null) return;
        // Once baked into a scene, its normal Inspector edits take precedence again.
        int removed = settings.entries.RemoveAll(entry => entry.scene == scene.path && UILayoutOverrides.Resolve(entry) != null);
        if (removed > 0) { EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings); }
    }
}
