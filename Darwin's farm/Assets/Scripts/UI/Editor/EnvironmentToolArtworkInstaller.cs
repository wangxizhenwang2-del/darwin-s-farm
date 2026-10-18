#if UNITY_EDITOR
using DarwinFarm.Environment;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Updates the existing controls in place; shared session callbacks stay intact.
public static class EnvironmentToolArtworkInstaller
{
    public const float DesktopToolbarWidth = 170f;
    [MenuItem("Tools/Darwin Farm/Apply Environment Tool Artwork")]
    public static void ApplyToScene()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play mode first.");
        Apply(Object.FindFirstObjectByType<GameInformationUI>(), Object.FindFirstObjectByType<MobileGameUI>());
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
    }

    public static void Apply(GameInformationUI information, MobileGameUI mobile)
    {
        var art = EnvironmentToolArtwork.Current;
        if (art == null || information == null || mobile == null) return;
        var desktop = information.GetComponentInChildren<DesktopEnvironmentTools>(true);
        if (desktop != null)
        {
            var saved = new SerializedObject(desktop);
            ConfigureDesktopToolbar(desktop,art);
            var body=((GameObject)saved.FindProperty("body").objectReferenceValue).GetComponent<RectTransform>();
            Badge((UnityEngine.UI.Button)saved.FindProperty("exit").objectReferenceValue,art);
            Text(body.Find("Title"),new Vector2(84,-22),new Vector2(402,36));
            Options(saved.FindProperty("options"),new Vector2(24,-92),new Vector2(462,98),art);
            Text(body.Find("Current"),new Vector2(24,-204),new Vector2(462,56));
            Text(body.Find("Preview"),new Vector2(24,-266),new Vector2(462,188));
            Position((RectTransform)body.Find("Apply"),Vector2.up,new Vector2(24,-470),new Vector2(144,54));
            Position((RectTransform)body.Find("CancelEffect"),Vector2.up,new Vector2(180,-470),new Vector2(144,54));
            Text(body.Find("Notice"),new Vector2(24,-530),new Vector2(462,26));
        }

        var mobileSaved=new SerializedObject(mobile);
        var safe=(RectTransform)mobileSaved.FindProperty("safeArea").objectReferenceValue;
        var hud=(RectTransform)safe.Find("MobileHUD");
        if(hud.TryGetComponent<UnityEngine.UI.HorizontalLayoutGroup>(out var hudLayout))hudLayout.enabled=false;
        var actions=safe.Find("MobileActions") as RectTransform;
        if(actions==null)
        {
            actions=new GameObject("MobileActions",typeof(RectTransform)).GetComponent<RectTransform>();
            actions.gameObject.layer=LayerMask.NameToLayer("UI");actions.SetParent(safe,false);
        }
        Position(actions,Vector2.up,new Vector2(8,-158),new Vector2(168,136));
        var legacyPause=(UnityEngine.UI.Button)mobileSaved.FindProperty("pauseButton").objectReferenceValue;
        if(legacyPause!=null)legacyPause.gameObject.SetActive(false);
        string[] fields={"environmentToolsButton","buildButton"};
        for(int i=0;i<fields.Length;i++)
        {
            var button=(UnityEngine.UI.Button)mobileSaved.FindProperty(fields[i]).objectReferenceValue;
            if(button==null)continue;
            button.transform.SetParent(actions,false);button.transform.SetSiblingIndex(i);
            Position((RectTransform)button.transform,Vector2.up,new Vector2(0,-i*72),new Vector2(168,64));
            Picture((RectTransform)button.transform,art.actionBackground,true);
            if(button.TryGetComponent<UnityEngine.UI.Outline>(out var outline))outline.enabled=false;
            var label=button.GetComponentInChildren<TextMeshProUGUI>(true);
            label.enableAutoSizing=true;label.fontSizeMin=18;label.fontSizeMax=24;
            label.alignment=TextAlignmentOptions.Center;
            Stretch(label.rectTransform,new Vector2(20,4),new Vector2(-20,-4));
        }
        var help=(RectTransform)safe.Find("GestureHelp");
        Picture(help,art.hintBackground,true);
        var hint=help.GetComponentInChildren<TextMeshProUGUI>(true);
        hint.enableAutoSizing=true;hint.fontSizeMin=16;hint.fontSizeMax=21;
        hint.alignment=TextAlignmentOptions.MidlineLeft;
        Stretch(hint.rectTransform,new Vector2(24,8),new Vector2(-24,-8));
        mobileSaved.FindProperty("statusBar").objectReferenceValue=hud;
        mobileSaved.FindProperty("actionColumn").objectReferenceValue=actions;
        mobileSaved.FindProperty("gestureHelp").objectReferenceValue=help;
        mobileSaved.ApplyModifiedPropertiesWithoutUndo();
        var speed=(UnityEngine.UI.Button)mobileSaved.FindProperty("speedButton").objectReferenceValue;
        speed.GetComponent<UnityEngine.UI.Image>().preserveAspect=false;
        var speedLabel=speed.GetComponentInChildren<TextMeshProUGUI>(true);
        speedLabel.enableAutoSizing=true;speedLabel.fontSizeMin=16;speedLabel.fontSizeMax=22;
        Stretch(speedLabel.rectTransform,new Vector2(12,5),new Vector2(-12,-5));

        var technology=mobile.GetComponentInChildren<MobileTechnologyPanel>(true);
        if(technology!=null)
        {
            var saved=new SerializedObject(technology);
            Badge((UnityEngine.UI.Button)saved.FindProperty("foldButton").objectReferenceValue,art);
            var close=(UnityEngine.UI.Button)saved.FindProperty("foldButton").objectReferenceValue;
            Text(close.transform.parent.Find("Title"),new Vector2(84,-22),new Vector2(390,36));
            var body=((GameObject)saved.FindProperty("body").objectReferenceValue).GetComponent<RectTransform>();
            body.sizeDelta=new Vector2(476,516);
            var kinds=saved.FindProperty("kindButtons");
            for(int i=0;i<kinds.arraySize;i++)
            {
                var button=(UnityEngine.UI.Button)kinds.GetArrayElementAtIndex(i).objectReferenceValue;
                var label=button.GetComponentInChildren<TextMeshProUGUI>(true);
                label.text=EnvironmentTechnologyCatalog.ShortName(EnvironmentTechnologyCatalog.All[i]);
                label.fontSizeMin=17;label.fontSizeMax=23;label.enableAutoSizing=true;
            }
            Options(saved.FindProperty("optionButtons"),new Vector2(0,-58),new Vector2(476,98),art);
            Text(body.Find("CurrentTechnology"),new Vector2(0,-166),new Vector2(476,58));
            Text(body.Find("EffectPreview"),new Vector2(0,-230),new Vector2(476,208));
            Position((RectTransform)body.Find("DeployAndCancel"),Vector2.up,new Vector2(0,-448),new Vector2(476,54));
            saved.FindProperty("expandedContentHeight").floatValue=520;
            saved.FindProperty("foldedContentHeight").floatValue=520;
            saved.ApplyModifiedPropertiesWithoutUndo();technology.ApplyFold();
        }
        var window=(RectTransform)mobileSaved.FindProperty("toolsDrawerRect").objectReferenceValue;
        if(window!=null)Stretch((RectTransform)window.Find("ToolScroll"),new Vector2(20,16),new Vector2(-20,-86));
        BuildAndPopulationUIInstaller.ConfigureMobile(information,mobile,art);
        mobile.RefreshLayout();
        foreach(var root in new[]{information.gameObject,mobile.gameObject})
            foreach(var component in root.GetComponentsInChildren<Component>(true))if(component!=null)EditorUtility.SetDirty(component);
        EditorSceneManager.MarkSceneDirty(mobile.gameObject.scene);
    }

    public static void ConfigureDesktopToolbar(DesktopEnvironmentTools desktop,EnvironmentToolArtwork art)
    {
        var saved=new SerializedObject(desktop);
        var root=(RectTransform)desktop.transform;
        var toolbar=((GameObject)saved.FindProperty("toolbar").objectReferenceValue).GetComponent<RectTransform>();
        Position(toolbar,Vector2.up,Vector2.zero,new Vector2(DesktopToolbarWidth,500));
        Picture(toolbar,ToolbarPaper(art),false);
        var image=toolbar.GetComponent<UnityEngine.UI.Image>();
        image.type=UnityEngine.UI.Image.Type.Sliced;image.pixelsPerUnitMultiplier=1;
        var buttons=saved.FindProperty("kinds");
        for(int i=0;i<buttons.arraySize;i++)
        {
            var button=(UnityEngine.UI.Button)buttons.GetArrayElementAtIndex(i).objectReferenceValue;
            button.name=EnvironmentTechnologyCatalog.ShortName(EnvironmentTechnologyCatalog.All[i]);
            Position((RectTransform)button.transform,Vector2.up,new Vector2((DesktopToolbarWidth-86)*.5f,-31.5f-i*87.6f),new Vector2(86,86));
            Picture((RectTransform)button.transform,art.exitBadge,true);
            button.targetGraphic.color=Color.clear;button.transition=UnityEngine.UI.Selectable.Transition.None;
            button.GetComponentInChildren<TextMeshProUGUI>(true).enabled=false;
            if(button.TryGetComponent<UnityEngine.UI.Outline>(out var outline))outline.enabled=false;
        }
        var body=((GameObject)saved.FindProperty("body").objectReferenceValue).GetComponent<RectTransform>();
        // Preserve the authored operation panel dimensions and interior layout.
        Position(body,Vector2.up,new Vector2(DesktopToolbarWidth+12,0),body.sizeDelta);
        root.sizeDelta=new Vector2(DesktopToolbarWidth+12+body.sizeDelta.x,Mathf.Max(500,body.sizeDelta.y));
        root.anchoredPosition=new Vector2(root.anchoredPosition.x,-142);
    }

    private static Sprite ToolbarPaper(EnvironmentToolArtwork art)
    {
        var original=art.toolbar;
        float scale=500f/original.rect.height;
        float margin=(DesktopToolbarWidth-scale*original.rect.width)/(2*(1-scale));
        if(art.wideToolbar!=null&&Mathf.Approximately(art.wideToolbar.border.x,margin))return art.wideToolbar;
        var sprite=Sprite.Create(original.texture,original.rect,new Vector2(.5f,.5f),original.pixelsPerUnit,0,
            SpriteMeshType.FullRect,new Vector4(margin,0,margin,0));
        sprite.name="ToolbarWide";
        if(art.wideToolbar==null)
        {
            art.wideToolbar=sprite;
            AssetDatabase.CreateAsset(sprite,"Assets/Resources/GameUI/ToolArt/ToolbarWide.asset");
        }
        else {EditorUtility.CopySerialized(sprite,art.wideToolbar);Object.DestroyImmediate(sprite);}
        EditorUtility.SetDirty(art);
        return art.wideToolbar;
    }

    private static void Options(SerializedProperty buttons,Vector2 position,Vector2 size,EnvironmentToolArtwork art)
    {
        if(buttons.arraySize==0)return;
        var first=(UnityEngine.UI.Button)buttons.GetArrayElementAtIndex(0).objectReferenceValue;
        var row=(RectTransform)first.transform.parent;
        Position(row,Vector2.up,position,size);
        var layout=row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing=18;layout.childAlignment=TextAnchor.MiddleCenter;
        layout.childControlWidth=layout.childControlHeight=false;
        layout.childForceExpandWidth=layout.childForceExpandHeight=false;
        for(int i=0;i<buttons.arraySize;i++)
        {
            var button=(UnityEngine.UI.Button)buttons.GetArrayElementAtIndex(i).objectReferenceValue;
            ((RectTransform)button.transform).sizeDelta=new Vector2(96,96);
            var element=button.GetComponent<UnityEngine.UI.LayoutElement>();
            if(element!=null){element.minWidth=element.preferredWidth=96;element.minHeight=element.preferredHeight=96;element.flexibleWidth=0;}
            Picture((RectTransform)button.transform,art.temperature[i],false);
            button.GetComponentInChildren<TextMeshProUGUI>(true).enabled=false;
            button.transition=UnityEngine.UI.Selectable.Transition.None;
            var outline=button.GetComponent<UnityEngine.UI.Outline>();
            if(outline==null)outline=button.gameObject.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor=new Color(.22f,.48f,.24f,1);outline.effectDistance=new Vector2(2,-2);outline.enabled=false;
            var marker=button.transform.Find("SelectedMarker") as RectTransform;
            if(marker==null)
            {
                marker=new GameObject("SelectedMarker",typeof(RectTransform),typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>();
                marker.gameObject.layer=LayerMask.NameToLayer("UI");marker.SetParent(button.transform,false);
            }
            marker.anchorMin=marker.anchorMax=new Vector2(.5f,0);marker.pivot=new Vector2(.5f,1);
            marker.anchoredPosition=new Vector2(0,-2);marker.sizeDelta=new Vector2(56,4);
            var markerImage=marker.GetComponent<UnityEngine.UI.Image>();markerImage.color=new Color(.25f,.48f,.27f);markerImage.raycastTarget=false;
            marker.gameObject.SetActive(i==1);
        }
    }
    private static void Badge(UnityEngine.UI.Button button,EnvironmentToolArtwork art)
    {
        Position((RectTransform)button.transform,Vector2.up,new Vector2(18,-14),new Vector2(56,56));
        Picture((RectTransform)button.transform,art.exitBadge,true);
        if(button.TryGetComponent<UnityEngine.UI.Outline>(out var outline))outline.enabled=false;
        var label=button.GetComponentInChildren<TextMeshProUGUI>(true);
        label.enabled=true;label.text="×";label.enableAutoSizing=false;label.fontSize=36;label.alignment=TextAlignmentOptions.Center;
        Stretch(label.rectTransform,Vector2.zero,Vector2.zero);
    }
    private static void Picture(RectTransform rect,Sprite sprite,bool mesh)
    {
        var image=rect.GetComponent<UnityEngine.UI.Image>();
        if(image==null)image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite=sprite;image.type=UnityEngine.UI.Image.Type.Simple;
        image.color=Color.white;image.useSpriteMesh=mesh;image.preserveAspect=false;
        image.raycastTarget=rect.GetComponent<UnityEngine.UI.Button>()!=null;
        var art=EnvironmentToolArtwork.Current;
        if(mesh && art.clippedSprites!=null)
        {
            foreach(var clip in art.clippedSprites)
            {
                if(clip.sprite!=sprite)continue;
                var silhouette=rect.GetComponent<ArtworkSilhouette>();
                if(silhouette==null)silhouette=rect.gameObject.AddComponent<ArtworkSilhouette>();
                silhouette.Configure(sprite,clip.outline);
                EditorUtility.SetDirty(silhouette);
                break;
            }
        }
    }
    private static void Text(Transform transform,Vector2 position,Vector2 size)
    { if(transform!=null)Position((RectTransform)transform,Vector2.up,position,size); }
    private static void Position(RectTransform rect,Vector2 anchor,Vector2 position,Vector2 size)
    {rect.anchorMin=rect.anchorMax=rect.pivot=anchor;rect.anchoredPosition=position;rect.sizeDelta=size;rect.localScale=Vector3.one;}
    private static void Stretch(RectTransform rect,Vector2 min,Vector2 max)
    {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=min;rect.offsetMax=max;}
}
#endif
