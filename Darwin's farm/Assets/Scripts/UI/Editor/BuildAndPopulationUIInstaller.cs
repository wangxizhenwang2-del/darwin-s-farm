#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildAndPopulationUIInstaller
{
    [MenuItem("Tools/Darwin Farm/Update Build UI and Population Attributes")]
    public static void ApplyToScene()
    {
        if(Application.isPlaying)throw new System.InvalidOperationException("Exit Play mode first.");
        var information=Object.FindFirstObjectByType<GameInformationUI>();
        var mobile=Object.FindFirstObjectByType<MobileGameUI>();
        var art=EnvironmentToolArtwork.Current;
        var desktop=information.GetComponentInChildren<DesktopEnvironmentTools>(true);
        if(desktop!=null)EnvironmentToolArtworkInstaller.ConfigureDesktopToolbar(desktop,art);
        ConfigureMobile(information,mobile,art);
        mobile.RefreshLayout();
        foreach(var root in new[]{information.gameObject,mobile.gameObject})
            foreach(var component in root.GetComponentsInChildren<Component>(true))if(component!=null)EditorUtility.SetDirty(component);
        EditorSceneManager.MarkSceneDirty(mobile.gameObject.scene);
        EditorSceneManager.SaveScene(mobile.gameObject.scene);
        AssetDatabase.SaveAssets();
    }

    public static void ConfigureMobile(GameInformationUI information,MobileGameUI mobile,EnvironmentToolArtwork art)
    {
        CopyRadarLabels(information,mobile);
        var saved=new SerializedObject(mobile);
        ConfigurePlacement(saved,art);
        ConfigurePalette(saved);
    }

    private static void CopyRadarLabels(GameInformationUI information,MobileGameUI mobile)
    {
        var source=information.GetComponentInChildren<PopulationInfoPanel>(true);
        var target=mobile.GetComponentInChildren<PopulationInfoPanel>(true);
        if(source==null||target==null)return;
        var original=new SerializedObject(source);
        var saved=new SerializedObject(target);
        var from=original.FindProperty("labels");var to=saved.FindProperty("labels");
        for(int i=0;i<Mathf.Min(from.arraySize,to.arraySize);i++)
        {
            var a=(TextMeshProUGUI)from.GetArrayElementAtIndex(i).objectReferenceValue;
            var b=(TextMeshProUGUI)to.GetArrayElementAtIndex(i).objectReferenceValue;
            CopyRect(a.rectTransform,b.rectTransform);
            b.alignment=a.alignment;b.margin=a.margin;b.fontSize=a.fontSize;
            b.enableAutoSizing=a.enableAutoSizing;b.fontSizeMin=a.fontSizeMin;b.fontSizeMax=a.fontSizeMax;
            b.textWrappingMode=TextWrappingModes.NoWrap;
            saved.FindProperty("radarLabelPositions").GetArrayElementAtIndex(i).vector2Value=a.rectTransform.anchoredPosition;
        }
        CopyRect(((PopulationRadarChart)original.FindProperty("radar").objectReferenceValue).rectTransform,
            ((PopulationRadarChart)saved.FindProperty("radar").objectReferenceValue).rectTransform);
        saved.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigurePlacement(SerializedObject saved,EnvironmentToolArtwork art)
    {
        if(art.placementBackground==null)return;
        var root=(RectTransform)((GameObject)saved.FindProperty("placementControls").objectReferenceValue).transform;
        root.anchorMin=root.anchorMax=root.pivot=Vector2.zero;
        root.anchoredPosition=new Vector2(8,144);root.sizeDelta=new Vector2(600,132);
        var image=root.GetComponent<UnityEngine.UI.Image>();
        image.sprite=art.placementBackground;image.type=UnityEngine.UI.Image.Type.Simple;image.color=Color.white;
        image.preserveAspect=false;image.raycastTarget=true;
        foreach(var clip in art.clippedSprites)
            if(clip.sprite==art.placementBackground)
            {
                var silhouette=root.GetComponent<ArtworkSilhouette>();
                if(silhouette==null)silhouette=root.gameObject.AddComponent<ArtworkSilhouette>();
                silhouette.Configure(clip.sprite,clip.outline);break;
            }
        var status=(TextMeshProUGUI)saved.FindProperty("placementLabel").objectReferenceValue;
        TopStretch(status.rectTransform,24,24,8,36);
        status.enableAutoSizing=true;status.fontSizeMin=16;status.fontSizeMax=21;
        status.alignment=TextAlignmentOptions.MidlineLeft;
        var actions=(RectTransform)root.Find("Actions");
        TopStretch(actions,12,12,56,60);
        var row=actions.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        row.spacing=12;row.padding=new RectOffset();row.childControlWidth=row.childControlHeight=true;
        row.childForceExpandWidth=row.childForceExpandHeight=true;
        foreach(string field in new[]{"rotateButton","confirmButton","cancelButton"})
        {
            var button=(UnityEngine.UI.Button)saved.FindProperty(field).objectReferenceValue;
            var background=(UnityEngine.UI.Image)button.targetGraphic;
            background.sprite=null;background.color=Color.white;
            var colors=button.colors;
            colors.normalColor=Color.clear;colors.highlightedColor=new Color(.3f,.5f,.2f,.12f);
            colors.pressedColor=new Color(.2f,.4f,.15f,.25f);colors.selectedColor=Color.clear;
            colors.disabledColor=new Color(.25f,.25f,.25f,.12f);colors.fadeDuration=.08f;
            button.transition=UnityEngine.UI.Selectable.Transition.ColorTint;button.colors=colors;
            var label=button.GetComponentInChildren<TextMeshProUGUI>(true);
            label.fontSizeMin=17;label.fontSizeMax=23;label.enableAutoSizing=true;
            label.alignment=TextAlignmentOptions.Center;
            var element=button.GetComponent<UnityEngine.UI.LayoutElement>();
            if(element!=null){element.minWidth=0;element.preferredWidth=180;element.flexibleWidth=1;}
        }
    }

    private static void ConfigurePalette(SerializedObject saved)
    {
        var desktop=Object.FindFirstObjectByType<TilePaletteUI>(FindObjectsInactive.Include);
        if(desktop==null)return;
        var paletteSettings=new SerializedObject(desktop);
        var prefab=(TilePaletteItemUI)paletteSettings.FindProperty("itemPrefab").objectReferenceValue;
        var itemSettings=new SerializedObject(prefab);
        var originalPreview=(UnityEngine.UI.Image)itemSettings.FindProperty("colorPreview").objectReferenceValue;
        var originalLabel=(TMP_Text)itemSettings.FindProperty("nameText").objectReferenceValue;
        var originalFrame=prefab.GetComponent<UnityEngine.UI.Image>();
        var palette=(GameObject)saved.FindProperty("buildPalette").objectReferenceValue;
        TopStretch((RectTransform)palette.transform.Find("Function"),12,12,8,32);
        var panel=(GameObject)saved.FindProperty("desktopBuildPanel").objectReferenceValue;
        CopyImage(panel.GetComponent<UnityEngine.UI.Image>(),palette.GetComponent<UnityEngine.UI.Image>());
        var scroll=palette.transform.Find("TileList");
        var scrollImage=scroll.GetComponent<UnityEngine.UI.Image>();
        if(scrollImage==null)scrollImage=scroll.gameObject.AddComponent<UnityEngine.UI.Image>();
        CopyImage(desktop.GetComponent<UnityEngine.UI.Image>(),scrollImage);
        var buttons=saved.FindProperty("tileButtons");var definitions=saved.FindProperty("tileDefinitions");
        for(int i=0;i<buttons.arraySize;i++)
        {
            var button=(UnityEngine.UI.Button)buttons.GetArrayElementAtIndex(i).objectReferenceValue;
            var definition=(MapTileDefinition)definitions.GetArrayElementAtIndex(i).objectReferenceValue;
            CopyImage(originalFrame,(UnityEngine.UI.Image)button.targetGraphic);
            var preview=button.transform.Find("ColorPreview")?.GetComponent<UnityEngine.UI.Image>();
            if(preview==null)
            {
                var go=new GameObject("ColorPreview",typeof(RectTransform),typeof(UnityEngine.UI.Image));
                go.layer=button.gameObject.layer;go.transform.SetParent(button.transform,false);
                preview=go.GetComponent<UnityEngine.UI.Image>();
            }
            CopyRect(originalPreview.rectTransform,preview.rectTransform);
            CopyImage(originalPreview,preview);preview.color=definition.mainColor;preview.raycastTarget=false;
            preview.transform.SetAsFirstSibling();
            var label=button.GetComponentInChildren<TextMeshProUGUI>(true);
            CopyRect(originalLabel.rectTransform,label.rectTransform);
            label.text=definition.displayName;
            label.color=originalFrame.color.grayscale<.35f ? new Color(.94f,.93f,.87f) : originalLabel.color;
            label.alignment=TextAlignmentOptions.Center;label.enableAutoSizing=true;
            label.fontSizeMin=17;label.fontSizeMax=23;
            var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(1.25f,1.25f,1.25f);
            colors.pressedColor=new Color(.75f,.75f,.75f);colors.selectedColor=Color.white;button.colors=colors;
        }
    }

    private static void CopyImage(UnityEngine.UI.Image from,UnityEngine.UI.Image to)
    {
        if(from==null||to==null)return;
        to.sprite=from.sprite;to.color=from.color;to.type=from.type;to.preserveAspect=from.preserveAspect;
    }
    private static void CopyRect(RectTransform from,RectTransform to)
    {
        to.anchorMin=from.anchorMin;to.anchorMax=from.anchorMax;to.pivot=from.pivot;
        to.anchoredPosition=from.anchoredPosition;to.sizeDelta=from.sizeDelta;
        to.localScale=from.localScale;to.localRotation=from.localRotation;
    }
    private static void TopStretch(RectTransform rect,float left,float right,float top,float height)
    {
        rect.anchorMin=Vector2.up;rect.anchorMax=Vector2.one;rect.pivot=Vector2.up;
        rect.offsetMin=new Vector2(left,-top-height);rect.offsetMax=new Vector2(-right,-top);
    }
}
#endif
