using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class JournalUILayoutMigration
{
    static JournalUILayoutMigration() => EditorApplication.delayCall += Run;
    public static void Run()
    {
        const string marker = "Temp/UILayoutAuthoring/migrate.request";
        if (!File.Exists(marker) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(marker);
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/WhiteBox.unity");
        bool opened = !scene.IsValid() || !scene.isLoaded;
        try
        {
            if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/WhiteBox.unity", OpenSceneMode.Additive);
            bool wasDirty = scene.isDirty;
            UILayoutAuthoringWindow.PreparePopulationPanels();
            var panels = UnityEngine.Object.FindObjectsByType<PopulationInfoPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int checkedPanels = 0;
            foreach (var panel in panels)
            {
                if (panel.gameObject.scene != scene) continue;
                if (panel.LayoutVersion != 2 || panel.transform.Find("JournalContent") == null ||
                    panel.transform.Find("CompactJournal") == null) throw new Exception("Layout not authored: " + panel.name);
                var clone = UnityEngine.Object.Instantiate(panel.gameObject);
                clone.SetActive(false);
                try
                {
                    var copy = clone.GetComponent<PopulationInfoPanel>();
                    var title = clone.transform.Find("JournalContent/NameSticker/Caption").GetComponent<TextMeshProUGUI>();
                    title.fontSize = 31;
                    title.enableAutoSizing = false;
                    title.rectTransform.anchoredPosition += new Vector2(7, 11);
                    var position = title.rectTransform.anchoredPosition;
                    int children = clone.transform.childCount;
                    copy.Initialize(null);
                    copy.ShowUnavailable("Validation");
                    copy.Initialize(null);
                    if (title.fontSize != 31 || title.rectTransform.anchoredPosition != position ||
                        clone.transform.childCount != children) throw new Exception("Initialize overwrote authored layout.");
                    checkedPanels++;
                }
                finally { UnityEngine.Object.DestroyImmediate(clone); }
            }
            if (checkedPanels < 2) throw new Exception("Desktop/mobile panel missing.");
            if (!wasDirty) EditorSceneManager.SaveScene(scene);
            const string settingsPath = "Assets/Resources/GameUI/UILayoutOverrides.asset";
            if (AssetDatabase.LoadAssetAtPath<UILayoutOverrides>(settingsPath) == null)
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<UILayoutOverrides>(), settingsPath);
            File.WriteAllText("Temp/UILayoutAuthoring/Migration.txt", "PASS: " + checkedPanels +
                " authored desktop/mobile panels; font/position survive repeated Initialize and data refresh; " +
                (wasDirty ? "scene marked dirty to preserve existing unsaved edits." : "scene saved."));
        }
        catch (Exception error) { File.WriteAllText("Temp/UILayoutAuthoring/Migration.txt", "FAIL: " + error); Debug.LogException(error); }
        finally
        {
            if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }
}
