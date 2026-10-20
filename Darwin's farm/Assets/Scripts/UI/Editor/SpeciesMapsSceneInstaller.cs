using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SpeciesMapsSceneInstaller
{
    [MenuItem("Tools/Darwin's Farm/UI/Update species maps presentation")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        var maps = UnityEngine.Object.FindFirstObjectByType<SpeciesMapsUI>();
        if (maps == null) throw new InvalidOperationException("Open WhiteBox first.");
        var settings = new SerializedObject(maps);
        var root = (GameObject)settings.FindProperty("overlay").objectReferenceValue;
        Undo.RegisterFullObjectHierarchyUndo(root,"Update species maps");
        var canvas = root.GetComponent<Canvas>();
        if (canvas == null) canvas = root.AddComponent<Canvas>();
        canvas.overrideSorting = true; canvas.sortingOrder = 300;
        if (root.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null) root.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        Style(root.GetComponent<UnityEngine.UI.Image>(), "OuterFrame", false);
        foreach (Transform panel in root.transform)
        {
            if (panel.name != "Evolution Map" && panel.name != "Food Web") continue;
            Style(panel.GetComponent<UnityEngine.UI.Image>(), "InnerPanel", false);
            var viewport = panel.Find("Viewport").GetComponent<UnityEngine.UI.Image>();
            viewport.color = Color.clear;
            ConfigureWideGraph((RectTransform)panel.Find("Viewport/Grid Content"));
            foreach (var button in panel.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                Style(button.GetComponent<UnityEngine.UI.Image>(),button.name == "Close Map" ? "CloseButton" : "SwitchButton",true);
                var rect = (RectTransform)button.transform;
                rect.sizeDelta = new Vector2(button.name == "Close Map" ? 56f : 164f,56f);
                if (button.name == "Switch Map") rect.anchoredPosition = new Vector2(-167f,-36f);
            }
            foreach (var text in panel.GetComponentsInChildren<UnityEngine.UI.Text>(true)) text.color = new Color(.16f,.21f,.12f);
            foreach (var text in panel.GetComponentsInChildren<TMPro.TMP_Text>(true)) text.color = new Color(.16f,.21f,.12f);
        }
        var mobile = UnityEngine.Object.FindFirstObjectByType<MobileGameUI>();
        if (mobile != null)
        {
            var data = new SerializedObject(mobile);
            var build = (UnityEngine.UI.Button)data.FindProperty("buildButton").objectReferenceValue;
            var parent = (RectTransform)data.FindProperty("actionColumn").objectReferenceValue;
            var existing = parent.Find("SpeciesMapButton");
            var button = existing != null ? existing.GetComponent<UnityEngine.UI.Button>() : UnityEngine.Object.Instantiate(build,parent);
            button.name = "SpeciesMapButton";
            button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            var label = button.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            label.text = "进化网";
            label.rectTransform.offsetMin = new Vector2(48f,4f);
            label.rectTransform.offsetMax = new Vector2(-8f,-4f);
            label.fontSize = 23f; label.enableAutoSizing = true; label.fontSizeMin = 20f; label.fontSizeMax = 23f;
            var icon = button.transform.Find("NetworkIcon");
            if (icon == null)
            {
                icon = new GameObject("NetworkIcon",typeof(RectTransform),typeof(SpeciesNetworkIcon)).transform;
                icon.SetParent(button.transform,false);
            }
            var iconRect = (RectTransform)icon;
            if (icon.GetComponent<CanvasRenderer>() == null) icon.gameObject.AddComponent<CanvasRenderer>();
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0,.5f);
            iconRect.pivot = new Vector2(0,.5f); iconRect.anchoredPosition = new Vector2(12,0);
            iconRect.sizeDelta = new Vector2(34,34);
            var graphic = icon.GetComponent<SpeciesNetworkIcon>(); graphic.color = new Color(.18f,.24f,.14f); graphic.raycastTarget = false;
            data.FindProperty("speciesMapButton").objectReferenceValue = button;
            data.FindProperty("speciesMaps").objectReferenceValue = maps;
            data.ApplyModifiedProperties(); mobile.RefreshLayout(); EditorUtility.SetDirty(mobile);
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
    }

    private static void ConfigureWideGraph(RectTransform content)
    {
        // Keep the portraits at their authored size and set column spacing to 1.5 times row spacing.
        var background = content.Find("SpeciesMapBackground");
        if (background != null) background.gameObject.SetActive(false);
        content.sizeDelta = new Vector2(2080f,1060f);
        var nodes = content.GetComponentsInChildren<SpeciesMapNodeView>(true);
        float rowSpacing = float.MaxValue;
        float columnSpacing = float.MaxValue;
        foreach (var first in nodes)
            foreach (var second in nodes)
            {
                float rowDifference = Mathf.Abs(first.NodePosition.y - second.NodePosition.y);
                float columnDifference = Mathf.Abs(first.NodePosition.x - second.NodePosition.x);
                if (rowDifference > 1f) rowSpacing = Mathf.Min(rowSpacing, rowDifference);
                if (columnDifference > 1f) columnSpacing = Mathf.Min(columnSpacing, columnDifference);
            }
        if (rowSpacing == float.MaxValue || columnSpacing == float.MaxValue) return;
        float factor = rowSpacing * 1.5f / columnSpacing;
        foreach (RectTransform child in content)
        {
            if (child.GetComponent<SpeciesMapNodeView>() == null && !child.name.EndsWith(" Caption",StringComparison.Ordinal)) continue;
            Vector2 position = child.anchoredPosition;
            child.anchoredPosition = new Vector2(position.x*factor,position.y);
        }
    }

    private static void Style(UnityEngine.UI.Image image, string name, bool trimAlpha)
    {
        string path = "Assets/Resources/GameUI/SpeciesMaps/" + name + ".png";
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false; importer.alphaIsTransparency = trimAlpha;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 4096; importer.SaveAndReimport();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        Rect rect = new Rect(0,0,texture.width,texture.height);
        if (trimAlpha)
        {
            var read = new Texture2D(2,2); read.LoadImage(System.IO.File.ReadAllBytes(path));
            var pixels = read.GetPixels32(); int left=read.width,bottom=read.height,right=0,top=0;
            for(int y=0;y<read.height;y++) for(int x=0;x<read.width;x++)
                if(pixels[y*read.width+x].a>127) {left=Mathf.Min(left,x);right=Mathf.Max(right,x);bottom=Mathf.Min(bottom,y);top=Mathf.Max(top,y);}
            rect = new Rect(left,bottom,right-left+1,top-bottom+1);
            UnityEngine.Object.DestroyImmediate(read);
        }
        string spritePath = path.Replace(".png",".asset");
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        if (sprite == null)
        {
            sprite = Sprite.Create(texture,rect,new Vector2(.5f,.5f),100f,0,SpriteMeshType.FullRect);
            sprite.name = name; AssetDatabase.CreateAsset(sprite,spritePath);
        }
        image.sprite = sprite; image.type = UnityEngine.UI.Image.Type.Simple; image.color = Color.white;
        image.preserveAspect = false;
    }
}
