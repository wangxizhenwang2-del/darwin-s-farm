#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class EnvironmentToolsInstaller
{
    [MenuItem("Tools/Darwin Farm/Install Environment Tools and Selection Outlines")]
    public static void Install()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Install outside Play mode.");
        var information = Object.FindFirstObjectByType<GameInformationUI>();
        var mobile = Object.FindFirstObjectByType<MobileGameUI>();
        if (information == null || mobile == null) throw new System.InvalidOperationException("Information and mobile UI are required.");
        var session = information.GetComponent<EnvironmentToolSession>();
        if (session == null) session = information.gameObject.AddComponent<EnvironmentToolSession>();
        var outline = information.GetComponent<SelectionOutlinePresenter>();
        if (outline == null) outline = information.gameObject.AddComponent<SelectionOutlinePresenter>();
        var savedOutline = new SerializedObject(outline);
        Set(savedOutline,"lineMaterial",Material("SelectionBoundary", "Sprites/Default"));
        Set(savedOutline,"spriteMaterial",Material("SelectionSilhouette", "DarwinFarm/SelectionSpriteOutline"));
        savedOutline.ApplyModifiedPropertiesWithoutUndo();
        if (information.transform.Find("EnvironmentTools") == null)
        {
            var root=Rect("EnvironmentTools",(RectTransform)information.transform,new Vector2(620,530),new Vector2(20,-118));
            var panel=root.gameObject.AddComponent<DesktopEnvironmentTools>(); var saved=new SerializedObject(panel);
            Set(saved,"session",session); Set(saved,"mobile",mobile);
            var toolbar=Rect("Toolbar",root,new Vector2(94,320),Vector2.zero); Set(saved,"toolbar",toolbar.gameObject);
            var kinds=new UnityEngine.UI.Button[5]; string[] captions={"温度","海拔","湿度","植物增量","植物存量"};
            for(int i=0;i<5;i++) kinds[i]=Button(captions[i],toolbar,captions[i],new Vector2(94,56),new Vector2(0,-i*64));
            Array(saved,"kinds",kinds);
            var body=Rect("OperationPreview",root,new Vector2(510,480),new Vector2(104,0));
            var background=body.gameObject.AddComponent<UnityEngine.UI.Image>(); background.color=new Color(.93f,.97f,.94f,.98f);
            Set(saved,"body",body.gameObject);
            Label("Title",body,"环境工程 · 点选地块",24,new Vector2(14,-12),new Vector2(330,36));
            Set(saved,"exit",Button("Exit",body,"退出",new Vector2(88,40),new Vector2(408,-10)));
            var row=Rect("Options",body,new Vector2(482,50),new Vector2(14,-62));
            var layout=row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>(); layout.spacing=6; layout.childForceExpandWidth=true; layout.childControlWidth=true;
            var options=new UnityEngine.UI.Button[4];
            for(int i=0;i<4;i++) options[i]=Button("Option"+i,row,"档位",new Vector2(114,50),Vector2.zero);
            Array(saved,"options",options);
            Set(saved,"current",Label("Current",body,"当前科技",18,new Vector2(14,-124),new Vector2(482,60)));
            Set(saved,"preview",Label("Preview",body,"请选择地块",20,new Vector2(14,-190),new Vector2(482,180)));
            Set(saved,"apply",Button("Apply",body,"应用",new Vector2(144,54),new Vector2(14,-380)));
            Set(saved,"cancel",Button("CancelEffect",body,"撤销效果",new Vector2(144,54),new Vector2(170,-380)));
            Set(saved,"notice",Label("Notice",body,"",18,new Vector2(14,-442),new Vector2(482,30)));
            saved.ApplyModifiedPropertiesWithoutUndo();
        }
        var safe=(RectTransform)mobile.transform.Find("SafeArea");
        var existing=safe.Find("MobileActions/EnvironmentToolsButton") ?? safe.Find("EnvironmentToolsButton");
        var toggle=existing != null ? existing.GetComponent<UnityEngine.UI.Button>() :
            Button("EnvironmentToolsButton",safe,"环境工具",new Vector2(136,56),new Vector2(12,-144));
        ((RectTransform)toggle.transform).anchoredPosition = new Vector2(12,-144);
        var mobileSettings=new SerializedObject(mobile); Set(mobileSettings,"environmentToolsButton",toggle); mobileSettings.ApplyModifiedPropertiesWithoutUndo();
        // The mobile body needs space for a target, real prices and validation text at its existing touch scale.
        var technology=mobile.GetComponentInChildren<MobileTechnologyPanel>(true);
        var tech=new SerializedObject(technology); tech.FindProperty("expandedContentHeight").floatValue=540;
        var preview=(TextMeshProUGUI)tech.FindProperty("previewText").objectReferenceValue;
        preview.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,220);
        var actions=technology.transform.Find("TechnologyBody/DeployAndCancel") as RectTransform;
        actions.anchoredPosition=new Vector2(0,-398);
        var bodyRect=technology.transform.Find("TechnologyBody") as RectTransform;
        bodyRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,460);
        tech.ApplyModifiedPropertiesWithoutUndo();
        ConfigureToolPanels(information, mobile);
        foreach(var component in information.GetComponentsInChildren<Component>(true)) if(component!=null) EditorUtility.SetDirty(component);
        foreach(var component in mobile.GetComponentsInChildren<Component>(true)) if(component!=null) EditorUtility.SetDirty(component);
        EditorSceneManager.MarkSceneDirty(information.gameObject.scene);
        EditorSceneManager.SaveScene(information.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Environment tools and selection outlines installed.");
    }

    // Move the authored tool controls out of the information tabs without
    // replacing their buttons, callbacks or shared ecological session.
    public static void ConfigureToolPanels(GameInformationUI information, MobileGameUI mobile)
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Configure outside Play mode.");
        var saved = new SerializedObject(mobile);
        var safe = (RectTransform)saved.FindProperty("safeArea").objectReferenceValue;
        var technology = (MobileTechnologyPanel)saved.FindProperty("technologyPanel").objectReferenceValue;
        if (safe == null || technology == null) throw new System.InvalidOperationException("Missing authored mobile controls.");
        var windowRect = safe.Find("EnvironmentToolDrawer") as RectTransform;
        if (windowRect == null) windowRect = Rect("EnvironmentToolDrawer", safe, new Vector2(520,572), new Vector2(-8,-92));
        windowRect.anchorMin = windowRect.anchorMax = windowRect.pivot = Vector2.one;
        windowRect.anchoredPosition = new Vector2(-8,-92);
        if (windowRect.GetComponent<CanvasGroup>() == null) windowRect.gameObject.AddComponent<CanvasGroup>();
        var window = windowRect.GetComponent<InformationWindow>();
        if (window == null) window = windowRect.gameObject.AddComponent<InformationWindow>();
        var windowSettings = new SerializedObject(window);
        windowSettings.FindProperty("titleOnlyDragging").boolValue = true;
        windowSettings.ApplyModifiedPropertiesWithoutUndo();
        var title = windowRect.Find("Title")?.GetComponent<TextMeshProUGUI>();
        if (title == null) title = Label("Title",windowRect,"环境工具",24,new Vector2(24,-14),new Vector2(310,40));
        title.raycastTarget = true;
        var titleHandle = title.GetComponent<InformationTextDragHandle>();
        if (titleHandle == null) titleHandle = title.gameObject.AddComponent<InformationTextDragHandle>();
        titleHandle.Initialize(window);
        var titleSettings = new SerializedObject(titleHandle);
        titleSettings.FindProperty("titleHandle").boolValue = true;
        titleSettings.ApplyModifiedPropertiesWithoutUndo();

        var scrollRect = windowRect.Find("ToolScroll") as RectTransform;
        if (scrollRect == null) scrollRect = Rect("ToolScroll",windowRect,Vector2.zero,Vector2.zero);
        Stretch(scrollRect,new Vector2(20,16),new Vector2(-20,-76));
        var scroll = scrollRect.GetComponent<UnityEngine.UI.ScrollRect>();
        if (scroll == null) scroll = scrollRect.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 32;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        var viewport = scrollRect.Find("Viewport") as RectTransform;
        if (viewport == null) viewport = Rect("Viewport",scrollRect,Vector2.zero,Vector2.zero);
        Stretch(viewport,Vector2.zero,Vector2.zero);
        var viewportImage = viewport.GetComponent<UnityEngine.UI.Image>();
        if (viewportImage == null) viewportImage = viewport.gameObject.AddComponent<UnityEngine.UI.Image>();
        viewportImage.color = Color.clear; viewportImage.raycastTarget = true;
        if (viewport.GetComponent<UnityEngine.UI.RectMask2D>() == null) viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var content = (RectTransform)technology.transform;
        content.SetParent(viewport,false);
        content.anchorMin = content.anchorMax = content.pivot = Vector2.up;
        content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(500,480);
        content.localScale = Vector3.one;
        scroll.content = content; scroll.viewport = viewport;
        var tech = new SerializedObject(technology);
        Set(tech,"tileContent",content);
        tech.FindProperty("expandedContentHeight").floatValue = 480;
        tech.FindProperty("foldedContentHeight").floatValue = 480;
        var body = (RectTransform)((GameObject)tech.FindProperty("body").objectReferenceValue).transform;
        body.anchoredPosition = new Vector2(12,0);
        var close = (UnityEngine.UI.Button)tech.FindProperty("foldButton").objectReferenceValue;
        var closeRect = (RectTransform)close.transform;
        closeRect.SetParent(windowRect,false);
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = Vector2.one;
        closeRect.anchoredPosition = new Vector2(-20,-14); closeRect.sizeDelta = new Vector2(96,40);
        close.GetComponentInChildren<TextMeshProUGUI>(true).text = "关闭";
        foreach (var handle in content.GetComponentsInChildren<InformationTextDragHandle>(true)) handle.Initialize(window);
        tech.ApplyModifiedPropertiesWithoutUndo();
        Set(saved,"toolsDrawer",window); Set(saved,"toolsDrawerRect",windowRect);
        Set(saved,"toolsScroll",scroll); Set(saved,"toolsViewport",viewport); Set(saved,"toolsContent",content);
        saved.ApplyModifiedPropertiesWithoutUndo();

        Sprite paper = Resources.Load<Sprite>("GameUI/Editable/ToolPanelBackground");
        if (paper != null)
        {
            ApplyPaper(windowRect,paper);
            var desktop = information.GetComponentInChildren<DesktopEnvironmentTools>(true);
            if (desktop != null)
            {
                var desktopSettings = new SerializedObject(desktop);
                var bodyObject = (GameObject)desktopSettings.FindProperty("body").objectReferenceValue;
                ApplyPaper((RectTransform)bodyObject.transform,paper);
            }
        }
        EnvironmentToolArtworkInstaller.Apply(information, mobile);
        foreach(var component in windowRect.GetComponentsInChildren<Component>(true))
            if(component!=null) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(mobile);
        EditorSceneManager.MarkSceneDirty(mobile.gameObject.scene);
    }

    private static void ApplyPaper(RectTransform root, Sprite paper)
    {
        var image = root.GetComponent<UnityEngine.UI.Image>();
        if (image == null) image = root.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = paper; image.color = Color.white;
        image.type = UnityEngine.UI.Image.Type.Sliced; image.pixelsPerUnitMultiplier = 1.5f;
        image.raycastTarget = true; image.preserveAspect = false;
        EditorUtility.SetDirty(image);
    }
    private static void Stretch(RectTransform rect,Vector2 minimum,Vector2 maximum)
    { rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.pivot=Vector2.up;rect.offsetMin=minimum;rect.offsetMax=maximum; }
    private static Material Material(string name,string shaderName)
    {
        string path="Assets/Resources/GameUI/Editable/"+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material!=null) return material;
        var shader=Shader.Find(shaderName); if(shader==null) throw new System.InvalidOperationException(shaderName);
        material=new Material(shader){name=name}; AssetDatabase.CreateAsset(material,path); return material;
    }
    private static void Set(SerializedObject saved,string field,Object value) => saved.FindProperty(field).objectReferenceValue=value;
    private static void Array(SerializedObject saved,string field,UnityEngine.UI.Button[] values)
    { var prop=saved.FindProperty(field); prop.arraySize=values.Length; for(int i=0;i<values.Length;i++) prop.GetArrayElementAtIndex(i).objectReferenceValue=values[i]; }
    private static RectTransform Rect(string name,RectTransform parent,Vector2 size,Vector2 position)
    {
        var go=new GameObject(name,typeof(RectTransform)); go.layer=LayerMask.NameToLayer("UI");
        var rect=(RectTransform)go.transform; rect.SetParent(parent,false); rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.up;
        rect.sizeDelta=size; rect.anchoredPosition=position; return rect;
    }
    private static TextMeshProUGUI Label(string name,RectTransform parent,string text,float size,Vector2 position,Vector2 dimensions)
    {
        var rect=Rect(name,parent,dimensions,position); var label=rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font=GameUITheme.TMPFont; label.fontSize=GameUITheme.ReadableFontSize(size); label.text=text; label.color=new Color(.1f,.16f,.15f); label.raycastTarget=false;
        label.enableAutoSizing=true; label.fontSizeMin=20; label.fontSizeMax=GameUITheme.ReadableFontSize(size);
        return label;
    }
    private static UnityEngine.UI.Button Button(string name,RectTransform parent,string caption,Vector2 size,Vector2 position)
    {
        var rect=Rect(name,parent,size,position); var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color=Color.white;
        var border=rect.gameObject.AddComponent<UnityEngine.UI.Outline>(); border.effectColor=new Color(.15f,.25f,.2f); border.effectDistance=new Vector2(1,-1);
        var button=rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic=image;
        var navigation=button.navigation; navigation.mode=UnityEngine.UI.Navigation.Mode.None; button.navigation=navigation;
        var label=Label("Label",rect,caption,22,Vector2.zero,size); label.alignment=TextAlignmentOptions.Center;
        label.rectTransform.anchorMin=Vector2.zero; label.rectTransform.anchorMax=Vector2.one; label.rectTransform.offsetMin=new Vector2(4,2); label.rectTransform.offsetMax=new Vector2(-4,-2);
        return button;
    }
}
#endif
