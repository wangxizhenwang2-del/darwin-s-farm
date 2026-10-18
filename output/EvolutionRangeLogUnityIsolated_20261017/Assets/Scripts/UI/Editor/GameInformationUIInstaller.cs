using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Materializes the existing builders once; runtime only binds the saved scene objects.
public static class GameInformationUIInstaller
{
    private const string AssetFolder = "Assets/Resources/GameUI/Editable";

    [MenuItem("Tools/Darwin Farm/UI/Install Editable Information UI", false, 300)]
    public static void Install()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("Please exit Play Mode before installing editable UI.");
            return;
        }
        GameInformationUI[] managers = Object.FindObjectsByType<GameInformationUI>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (managers.Length == 0)
        {
            Debug.LogWarning("The open scene has no GameInformationUI component.");
            return;
        }
        if (!AssetDatabase.IsValidFolder(AssetFolder))
            AssetDatabase.CreateFolder("Assets/Resources/GameUI", "Editable");
        PersistFont();
        foreach (GameInformationUI manager in managers)
        {
            Undo.RegisterFullObjectHierarchyUndo(manager.gameObject, "Install editable information UI");
            Canvas.ForceUpdateCanvases();
            manager.EnsureLayout();
            foreach (Image image in manager.GetComponentsInChildren<Image>(true))
            {
                Sprite sprite = image.sprite;
                if (sprite == null || AssetDatabase.Contains(sprite)) continue;
                string path = AssetFolder + "/" + image.name + "Background.asset";
                Sprite saved = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (saved == null)
                {
                    sprite.name = image.name + "Background";
                    sprite.hideFlags = HideFlags.None;
                    AssetDatabase.CreateAsset(sprite, path);
                    saved = sprite;
                }
                image.sprite = saved;
                EditorUtility.SetDirty(image);
            }
            foreach (InformationWindow window in manager.GetComponentsInChildren<InformationWindow>(true))
            {
                // Visible in Edit Mode for layout work; runtime initialization closes both windows.
                window.gameObject.SetActive(true);
                CanvasGroup group = window.GetComponent<CanvasGroup>();
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;
                EditorUtility.SetDirty(group);
            }
            foreach (Component component in manager.GetComponentsInChildren<Component>(true))
                if (component != null) EditorUtility.SetDirty(component);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Information UI installed. Adjust Canvas/TileInformation, PopulationInformation and InformationHUD, then save the scene.");
    }

    private static void PersistFont()
    {
        const string path = AssetFolder + "/GameUIFont.asset";
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null)
        {
            font = GameUITheme.TMPFont;
            font.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(font, path);
            if (font.material != null && !AssetDatabase.Contains(font.material))
            {
                font.material.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(font.material, font);
            }
            foreach (Texture2D atlas in font.atlasTextures)
                if (atlas != null && !AssetDatabase.Contains(atlas))
                {
                    atlas.hideFlags = HideFlags.None;
                    AssetDatabase.AddObjectToAsset(atlas, font);
                }
        }
        GameUITheme theme = Resources.Load<GameUITheme>("GameUI/Theme");
        if (theme != null)
        {
            SerializedObject settings = new SerializedObject(theme);
            settings.FindProperty("textMeshProFont").objectReferenceValue = font;
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(theme);
        }
    }
}
