using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Run once in an Editor or batch-mode copy. The result is serialized scene UI;
// no visual GameObject is created by DarwinJournalCanvas at runtime.
public static class JournalSceneInstaller
{
    private const string ScenePath="Assets/Scenes/WhiteBox.unity";
    private const string ArtPath="Assets/Resources/DarwinJournal/";
    private static Sprite paper, button, pressed, card, title, news;
    private static Font legacy;
    private static TMP_FontAsset tmp;

    [MenuItem("Tools/Darwin's Farm/UI/Install journal scene UI")]
    public static void Install()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play Mode before installing UI.");
        var scene=EditorSceneManager.OpenScene(ScenePath);
        if(GameObject.Find("JournalCanvas")!=null)throw new InvalidOperationException("JournalCanvas already exists. Edit its saved hierarchy instead.");
        PrepareArt();
        legacy=GameUITheme.Font; tmp=GameUITheme.TMPFont;
        TutorialDatabase database=BuildTutorial();
        var root=new GameObject("JournalCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        root.layer=LayerMask.NameToLayer("UI");
        var canvas=root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=200;
        var scaler=root.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1280,720); scaler.matchWidthOrHeight=1;
        var safe=Rect("SafeArea",root.transform); Stretch(safe);
        var titleView=Rect("TitlePanel",safe); Stretch(titleView);
        var bg=Image("Landscape",titleView,title); Stretch((RectTransform)bg.transform); bg.raycastTarget=true;
        var wash=Image("PaperWash",titleView,null,new Color(.96f,.93f,.83f,.34f)); Stretch((RectTransform)wash.transform);
        Label("GameTitle",titleView,"达尔文农场",60,new Vector2(0,190),new Vector2(720,90));
        Label("Subtitle",titleView,"一册正在生长的生态观察手账",24,new Vector2(0,130),new Vector2(720,45));
        var titleNew=Button("NewGame",titleView,"新游戏",new Vector2(300,66),new Vector2(0,30));
        var titleLoad=Button("Load",titleView,"读档",new Vector2(300,66),new Vector2(0,-51));
        var titleExit=Button("Exit",titleView,"退出",new Vector2(300,66),new Vector2(0,-132));
        var titleSettings=Button("Settings",titleView,"设置",new Vector2(102,52),new Vector2(74,-40),new Vector2(0,1));
        var titleTutorial=Button("Tutorial",titleView,"教程",new Vector2(102,52),new Vector2(184,-40),new Vector2(0,1));
        var titleInfo=Button("Info",titleView,"信息",new Vector2(102,52),new Vector2(294,-40),new Vector2(0,1));

        var menu=Button("GameMenuButton",safe,"菜单",new Vector2(95,55),new Vector2(68,-96),new Vector2(0,1));
        var pauseView=Layer("PausePanel",safe);
        var pausePaper=Panel("Paper",pauseView,new Vector2(470,560));
        Label("Heading",pausePaper,"观察手账 · 暂停",31,new Vector2(0,221),new Vector2(380,52));
        var pauseResume=Button("Resume",pausePaper,"继续游戏",new Vector2(290,60),new Vector2(0,126));
        var pauseSettings=Button("Settings",pausePaper,"设置",new Vector2(290,60),new Vector2(0,49));
        var pauseSaves=Button("Saves",pausePaper,"存档 / 读档",new Vector2(290,60),new Vector2(0,-28));
        var pauseTutorial=Button("Tutorial",pausePaper,"教程",new Vector2(290,60),new Vector2(0,-105));
        var pauseTitle=Button("ReturnTitle",pausePaper,"回到标题",new Vector2(290,60),new Vector2(0,-182));

        var settingsView=Layer("SettingsPanel",safe);
        var settingsPaper=Panel("Paper",settingsView,new Vector2(680,595));
        Header(settingsPaper,"设置");
        var settingsClose=Close(settingsPaper);
        Slider master=SliderRow(settingsPaper,"主音量",170);
        Slider music=SliderRow(settingsPaper,"背景音乐",110);
        Slider effects=SliderRow(settingsPaper,"音效",50);
        var quality=DropdownRow(settingsPaper,"画质",-10);
        var resolutionRow=Rect("ResolutionRow",settingsPaper);
        Center(resolutionRow,new Vector2(590,52),new Vector2(0,-70));
        Label("Name",resolutionRow,"分辨率",22,new Vector2(-196,0),new Vector2(170,44),TextAlignmentOptions.Left);
        var resolution=Dropdown("ResolutionDropdown",resolutionRow,new Vector2(238,46),new Vector2(112,0));
        var fullscreenRow=Rect("FullscreenRow",settingsPaper);
        Center(fullscreenRow,new Vector2(590,52),new Vector2(0,-130));
        Label("Name",fullscreenRow,"全屏",22,new Vector2(-196,0),new Vector2(170,44),TextAlignmentOptions.Left);
        var fullscreen=Toggle("FullscreenToggle",fullscreenRow,new Vector2(90,42),new Vector2(38,0));
        Label("AudioNote",settingsPaper,"分类音量仅作用于已标记的音频源；主音量控制全局。",16,
            new Vector2(0,-246),new Vector2(580,34));

        var tutorialView=Layer("TutorialPanel",safe);
        var tutorialPaper=Panel("Paper",tutorialView,new Vector2(960,620));
        Header(tutorialPaper,"野外观察指南"); var tutorialClose=Close(tutorialPaper);
        var categoryButtons=new Button[3]; var categoryLabels=new TMP_Text[3];
        var pageButtons=new Button[3]; var pageLabels=new TMP_Text[3];
        for(int i=0;i<3;i++)
        {
            categoryButtons[i]=Button("Category"+i,tutorialPaper,"目录",new Vector2(205,48),new Vector2(-337,206-i*55));
            categoryLabels[i]=categoryButtons[i].GetComponentInChildren<TMP_Text>();
            pageButtons[i]=Button("Page"+i,tutorialPaper,"章节",new Vector2(205,44),new Vector2(-337,-8-i*51));
            pageLabels[i]=pageButtons[i].GetComponentInChildren<TMP_Text>();
        }
        var tutorialScroll=ScrollArea("BodyScroll",tutorialPaper,new Vector2(225,-235),new Vector2(670,455),out var tutorialBody);
        tutorialBody.text="基础操作";

        var infoView=Layer("InfoPanel",safe);
        var infoPaper=Panel("Paper",infoView,new Vector2(780,580));
        Header(infoPaper,"关于达尔文农场"); var infoClose=Close(infoPaper);
        var infoScroll=ScrollArea("InfoScroll",infoPaper,new Vector2(0,-13),new Vector2(680,405),out var infoText);
        infoText.text="Staff 旷工作室\n\n" +
            "策划：叶青城\n" +
            "程序：王熙振,叶青城,余炜杰\n" +
            "技术美术：余炜杰\n" +
            "2D美术：刘炫邑,柚子溪皮耶罗,猫川,肖芷希\n" +
            "3D美术：余炜杰, [马来西亚]Ho Hui San, [马来西亚]Gan Yi Lin\n" +
            "音效：大鲵\n" +
            "联系方式：1779981587@qq.com\n\n" +
            "感谢 杭州乐米科技有限公司 的字体授权";

        var savesView=Layer("SaveLoadPanel",safe);
        var savesPaper=Panel("Paper",savesView,new Vector2(680,565));
        Header(savesPaper,"存档 / 读档"); var savesClose=Close(savesPaper);
        var saveMessage=Label("Status",savesPaper,"",18,new Vector2(0,174),new Vector2(590,55));
        for(int i=0;i<3;i++)
        {
            var slot=Image("Slot"+(i+1),savesPaper,card); slot.type=UnityEngine.UI.Image.Type.Sliced;
            Center((RectTransform)slot.transform,new Vector2(570,78),new Vector2(0,75-i*96));
            Label("SlotText",slot.transform,"Slot "+(i+1)+"     暂不可用",23,Vector2.zero,new Vector2(520,55));
        }

        var confirmView=Layer("ConfirmDialog",safe);
        var confirmPaper=Panel("Paper",confirmView,new Vector2(480,320));
        Header(confirmPaper,"请确认");
        var confirmMessage=Label("Message",confirmPaper,"",22,new Vector2(0,13),new Vector2(410,80));
        var confirmYes=Button("Confirm",confirmPaper,"确认",new Vector2(150,54),new Vector2(-85,-83));
        var confirmNo=Button("Cancel",confirmPaper,"取消",new Vector2(150,54),new Vector2(85,-83));

        var toast=Image("EcologyNewsToast",safe,news);
        toast.type=UnityEngine.UI.Image.Type.Sliced;
        toast.raycastTarget=false;
        var toastRect=(RectTransform)toast.transform;
        Center(toastRect,new Vector2(650,112),new Vector2(0,92),new Vector2(.5f,1));
        var toastGroup=toast.gameObject.AddComponent<CanvasGroup>();toastGroup.alpha=0;
        var newsHeading=Label("EventType",toastRect,"生态新闻",21,new Vector2(-189,25),new Vector2(220,36),TextAlignmentOptions.Left);
        var newsBody=Label("Message",toastRect,"",20,new Vector2(0,-16),new Vector2(590,52));
        var newsDay=Label("GameDay",toastRect,"",16,new Vector2(215,25),new Vector2(150,32),TextAlignmentOptions.Right);
        root.AddComponent<EcologyNewsDetector>();
        var newsHud=toast.gameObject.AddComponent<EcologyNewsHUD>();
        var newsSerialized=new SerializedObject(newsHud);
        Set(newsSerialized,"card",toastRect);Set(newsSerialized,"group",toastGroup);
        Set(newsSerialized,"heading",newsHeading);Set(newsSerialized,"body",newsBody);
        Set(newsSerialized,"dayLabel",newsDay);newsSerialized.ApplyModifiedPropertiesWithoutUndo();

        var controller=root.AddComponent<DarwinJournalCanvas>();
        var serialized=new SerializedObject(controller);
        Set(serialized,"safeArea",safe);
        Set(serialized,"titlePanel",titleView.gameObject); Set(serialized,"pausePanel",pauseView.gameObject);
        Set(serialized,"settingsPanel",settingsView.gameObject); Set(serialized,"tutorialPanel",tutorialView.gameObject);
        Set(serialized,"infoPanel",infoView.gameObject); Set(serialized,"saveLoadPanel",savesView.gameObject);
        Set(serialized,"confirmDialog",confirmView.gameObject); Set(serialized,"gameMenuButton",menu.gameObject);
        Set(serialized,"titleNew",titleNew); Set(serialized,"titleLoad",titleLoad); Set(serialized,"titleExit",titleExit);
        Set(serialized,"titleSettings",titleSettings); Set(serialized,"titleTutorial",titleTutorial); Set(serialized,"titleInfo",titleInfo);
        Set(serialized,"pauseResume",pauseResume); Set(serialized,"pauseSettings",pauseSettings);
        Set(serialized,"pauseSaves",pauseSaves); Set(serialized,"pauseTutorial",pauseTutorial); Set(serialized,"pauseTitle",pauseTitle);
        Set(serialized,"settingsClose",settingsClose); Set(serialized,"tutorialClose",tutorialClose);
        Set(serialized,"infoClose",infoClose); Set(serialized,"savesClose",savesClose);
        Set(serialized,"confirmYes",confirmYes); Set(serialized,"confirmNo",confirmNo);
        Set(serialized,"confirmMessage",confirmMessage); Set(serialized,"saveMessage",saveMessage);
        Set(serialized,"masterSlider",master); Set(serialized,"musicSlider",music); Set(serialized,"effectsSlider",effects);
        Set(serialized,"qualityDropdown",quality); Set(serialized,"resolutionDropdown",resolution);
        Set(serialized,"fullscreenToggle",fullscreen); Set(serialized,"resolutionRow",resolutionRow.gameObject);
        Set(serialized,"fullscreenRow",fullscreenRow.gameObject); Set(serialized,"tutorial",database);
        SetArray(serialized,"categoryButtons",categoryButtons); SetArray(serialized,"pageButtons",pageButtons);
        SetArray(serialized,"categoryLabels",categoryLabels); SetArray(serialized,"pageLabels",pageLabels);
        Set(serialized,"tutorialBody",tutorialBody); Set(serialized,"tutorialScroll",tutorialScroll);
        Set(serialized,"infoScroll",infoScroll);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        ConfigureMenuLayout(controller);
        menu.gameObject.SetActive(false); pauseView.gameObject.SetActive(false);settingsView.gameObject.SetActive(false);
        tutorialView.gameObject.SetActive(false);infoView.gameObject.SetActive(false);
        savesView.gameObject.SetActive(false);confirmView.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Scene save failed.");
        // The playable scene is WhiteBox; retain SampleScene as a secondary scene.
        EditorBuildSettings.scenes=new[] {
            new EditorBuildSettingsScene(ScenePath,true),
            new EditorBuildSettingsScene("Assets/Scenes/SampleScene.unity",true)
        };
        AssetDatabase.SaveAssets();
        Debug.Log("JournalCanvas authored in WhiteBox and saved.");
    }
    public static void InstallBatch(){Install();}

    public static void ConfigureMenuLayout(DarwinJournalCanvas journal)
    {
        var saved = new SerializedObject(journal);
        var menu = (GameObject)saved.FindProperty("gameMenuButton").objectReferenceValue;
        var information = UnityEngine.Object.FindFirstObjectByType<GameInformationUI>();
        var mobile = UnityEngine.Object.FindFirstObjectByType<MobileGameUI>();
        if (menu == null || information == null || mobile == null)
            throw new InvalidOperationException("Menu and both HUDs must be authored before layout binding.");
        var desktopData = new SerializedObject(information);
        var mobileData = new SerializedObject(mobile);
        var desktopLabel = (UnityEngine.UI.Text)desktopData.FindProperty("speedText").objectReferenceValue;
        var mobileButton = (UnityEngine.UI.Button)mobileData.FindProperty("speedButton").objectReferenceValue;
        var layout = journal.GetComponent<HudMenuLayout>();
        if (layout == null) layout = journal.gameObject.AddComponent<HudMenuLayout>();
        var layoutData = new SerializedObject(layout);
        Set(layoutData, "menu", menu.transform);
        Set(layoutData, "desktopSpeed", desktopLabel.transform.parent);
        Set(layoutData, "mobileSpeed", mobileButton.transform);
        Set(layoutData, "mobileUI", mobile);
        layoutData.ApplyModifiedPropertiesWithoutUndo();
        var label = menu.GetComponentInChildren<TMP_Text>(true);
        var rect = label.rectTransform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(8, 4); rect.offsetMax = new Vector2(-8, -4);
        label.enableAutoSizing = true; label.fontSizeMin = 18; label.fontSizeMax = 26;
        label.alignment = TextAlignmentOptions.Center;
        layout.RefreshLayout();
        EditorUtility.SetDirty(layout);
        EditorUtility.SetDirty(label);
    }
    public static void PolishScrollBatch()
    {
        var scene=EditorSceneManager.OpenScene(ScenePath);
        var root=GameObject.Find("JournalCanvas");
        if(root==null)throw new InvalidOperationException("JournalCanvas missing.");
        foreach(var scroll in root.GetComponentsInChildren<ScrollRect>(true))
        {
            if(scroll.name!="BodyScroll" && scroll.name!="InfoScroll")continue;
            var image=scroll.GetComponent<Image>();
            if(image==null)image=scroll.gameObject.AddComponent<Image>();
            image.color=new Color(1,1,1,0);
            image.raycastTarget=true;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Scene save failed.");
        Debug.Log("Journal scroll raycast surfaces authored and saved.");
    }
    private static void PrepareArt()
    {
        paper=Import("PaperPanel",new Vector4(30,30,30,30));
        button=Import("PaperButton",new Vector4(24,24,24,24));
        pressed=Import("PaperButtonPressed",new Vector4(24,24,24,24));
        card=Import("SaveCard",new Vector4(24,24,24,24));
        news=Import("NewsToast",new Vector4(24,24,24,24));
        title=Import("TitleLandscape",Vector4.zero);
    }
    private static Sprite Import(string name,Vector4 border)
    {
        string path=ArtPath+name+".png";
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        if(importer==null)throw new IOException("Missing art: "+path);
        importer.textureType=TextureImporterType.Sprite;
        importer.spriteImportMode=SpriteImportMode.Single;
        importer.spriteBorder=border;
        importer.mipmapEnabled=false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    private static TutorialDatabase BuildTutorial()
    {
        const string path="Assets/Resources/DarwinJournal/TutorialDatabase.asset";
        var asset=AssetDatabase.LoadAssetAtPath<TutorialDatabase>(path);
        if(asset!=null)return asset;
        asset=ScriptableObject.CreateInstance<TutorialDatabase>();
        asset.categories=new[] {
            Category("basics","基础操作",Page("goal","游戏目标","建立地块，观察资源和种群如何随游戏日变化。当前版本主要模拟陆地生态。"),
                Page("observe","观察地块与生物","点击地块查看环境；点击种群查看数量及性状。手机上可用触摸手势操作地图。"),
                Page("time","暂停与倍速","空格键控制模拟暂停，2 和 4 可改变倍速。打开菜单时游戏日停止推进。")),
            Category("ecology","生态系统",Page("resources","资源与竞争","植物和猎物等资源会影响种群存活。实时数值请以信息窗为准。"),
                Page("predation","捕食关系","捕食由当前食性和生态规则决定；可观察每日结算后种群数量变化。"),
                Page("migration","迁徙","满足现有迁徙规则后，种群可能向相邻地块移动。")),
            Category("evolution","进化系统",Page("traits","五项性状","种群的温度适应、湿度适应、移动能力、体型与繁殖能力会改变。"),
                Page("speciation","种群演化","正式演化与分化需要持续的生态条件；一次数值波动不等于新物种形成。"))
        };
        AssetDatabase.CreateAsset(asset,path);
        return asset;
    }
    private static TutorialDatabase.Category Category(string id,string title,params TutorialDatabase.Page[] pages)=>
        new TutorialDatabase.Category{id=id,title=title,pages=pages};
    private static TutorialDatabase.Page Page(string id,string title,string content)=>
        new TutorialDatabase.Page{id=id,title=title,content=content};
    private static void Set(SerializedObject o,string name,UnityEngine.Object value)=>o.FindProperty(name).objectReferenceValue=value;
    private static void SetArray<T>(SerializedObject o,string name,T[] values) where T:UnityEngine.Object
    {
        var p=o.FindProperty(name);p.arraySize=values.Length;
        for(int i=0;i<values.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=values[i];
    }
    private static RectTransform Rect(string name,Transform parent)
    {var g=new GameObject(name,typeof(RectTransform));g.layer=LayerMask.NameToLayer("UI");g.transform.SetParent(parent,false);return (RectTransform)g.transform;}
    private static void Stretch(RectTransform r)
    {r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
    private static void Center(RectTransform r,Vector2 size,Vector2 position,Vector2? anchor=null)
    {r.anchorMin=r.anchorMax=anchor??new Vector2(.5f,.5f);r.sizeDelta=size;r.anchoredPosition=position;}
    private static Image Image(string name,Transform parent,Sprite sprite,Color? tint=null)
    {var r=Rect(name,parent);var i=r.gameObject.AddComponent<Image>();i.sprite=sprite;i.color=tint??Color.white;i.raycastTarget=false;return i;}
    private static TMP_Text Label(string name,Transform parent,string text,int size,Vector2 position,Vector2 dimensions,TextAlignmentOptions align=TextAlignmentOptions.Center)
    {var r=Rect(name,parent);Center(r,dimensions,position);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=tmp;t.fontSize=size;t.color=new Color(.20f,.27f,.22f);t.alignment=align;t.text=text;t.raycastTarget=false;return t;}
    private static Button Button(string name,Transform parent,string text,Vector2 size,Vector2 position,Vector2? anchor=null)
    {
        var image=Image(name,parent,button);image.type=UnityEngine.UI.Image.Type.Sliced;image.raycastTarget=true;
        Center((RectTransform)image.transform,size,position,anchor);
        var b=image.gameObject.AddComponent<Button>();b.targetGraphic=image;
        var c=b.colors;c.highlightedColor=new Color(.91f,.97f,.86f);c.pressedColor=new Color(.74f,.84f,.68f);c.disabledColor=new Color(.72f,.72f,.72f,.6f);b.colors=c;
        Label("Label",image.transform,text,size.y>=55?25:21,Vector2.zero,size-new Vector2(12,6));
        return b;
    }
    private static RectTransform Layer(string name,Transform parent)
    {var r=Rect(name,parent);Stretch(r);var shade=Image("Shade",r,null,new Color(.12f,.18f,.15f,.68f));Stretch((RectTransform)shade.transform);shade.raycastTarget=true;return r;}
    private static RectTransform Panel(string name,Transform parent,Vector2 size)
    {var i=Image(name,parent,paper);i.type=UnityEngine.UI.Image.Type.Sliced;i.raycastTarget=true;Center((RectTransform)i.transform,size,Vector2.zero);return (RectTransform)i.transform;}
    private static void Header(RectTransform p,string text)
    {Label("Heading",p,text,31,new Vector2(0,p.sizeDelta.y*.5f-51),new Vector2(p.sizeDelta.x-110,52));}
    private static Button Close(RectTransform p)=>Button("Close",p,"×",new Vector2(52,52),new Vector2(p.sizeDelta.x*.5f-45,p.sizeDelta.y*.5f-45));
    private static Slider SliderRow(RectTransform p,string title,float y)
    {
        Label(title,p,title,22,new Vector2(-185,y),new Vector2(170,44),TextAlignmentOptions.Left);
        var track=Image(title+"Track",p,null,new Color(.66f,.71f,.58f));Center((RectTransform)track.transform,new Vector2(245,14),new Vector2(105,y));
        var fill=Image("Fill",track.transform,null,new Color(.38f,.52f,.38f));Stretch((RectTransform)fill.transform);
        var knob=Image("Handle",track.transform,button);Center((RectTransform)knob.transform,new Vector2(36,33),Vector2.zero);
        var slider=track.gameObject.AddComponent<Slider>();slider.fillRect=(RectTransform)fill.transform;slider.handleRect=(RectTransform)knob.transform;
        slider.targetGraphic=knob;slider.minValue=0;slider.maxValue=1;slider.value=1;
        track.raycastTarget=true;return slider;
    }
    private static TMP_Dropdown DropdownRow(RectTransform p,string title,float y)
    {Label(title,p,title,22,new Vector2(-185,y),new Vector2(170,44),TextAlignmentOptions.Left);return Dropdown(title+"Dropdown",p,new Vector2(240,46),new Vector2(112,y));}
    private static TMP_Dropdown Dropdown(string name,Transform parent,Vector2 size,Vector2 position)
    {
        var image=Image(name,parent,button);image.type=UnityEngine.UI.Image.Type.Sliced;image.raycastTarget=true;
        Center((RectTransform)image.transform,size,position);
        var caption=Label("Caption",image.transform,"选项  ▾",21,Vector2.zero,size-new Vector2(20,4));
        var template=Rect("Template",image.transform);Center(template,new Vector2(size.x,215),new Vector2(0,-131));
        var templateImage=template.gameObject.AddComponent<Image>();templateImage.sprite=paper;templateImage.type=UnityEngine.UI.Image.Type.Sliced;
        var scroll=template.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;
        var viewport=Rect("Viewport",template);Stretch(viewport);viewport.gameObject.AddComponent<RectMask2D>();
        var content=Rect("Content",viewport);content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);
        content.offsetMin=content.offsetMax=Vector2.zero;
        var item=Rect("Item",content);item.anchorMin=new Vector2(0,1);item.anchorMax=new Vector2(1,1);item.pivot=new Vector2(.5f,1);
        item.sizeDelta=new Vector2(0,40);item.anchoredPosition=Vector2.zero;
        var itemBg=item.gameObject.AddComponent<Image>();itemBg.color=new Color(.95f,.93f,.83f);
        var toggle=item.gameObject.AddComponent<Toggle>();toggle.targetGraphic=itemBg;
        var check=Image("Checkmark",item,pressed);Center((RectTransform)check.transform,new Vector2(25,25),new Vector2(-size.x*.5f+24,-20));toggle.graphic=check;
        var itemText=Label("ItemLabel",item,"选项",19,new Vector2(10,-20),new Vector2(size.x-50,36),TextAlignmentOptions.Left);
        scroll.viewport=viewport;scroll.content=content;template.gameObject.SetActive(false);
        var dropdown=image.gameObject.AddComponent<TMP_Dropdown>();dropdown.targetGraphic=image;dropdown.template=template;
        dropdown.captionText=(TextMeshProUGUI)caption;dropdown.itemText=(TextMeshProUGUI)itemText;
        return dropdown;
    }
    private static Toggle Toggle(string name,Transform parent,Vector2 size,Vector2 position)
    {
        var bg=Image(name,parent,button);bg.type=UnityEngine.UI.Image.Type.Sliced;bg.raycastTarget=true;
        Center((RectTransform)bg.transform,size,position);
        var check=Image("Checkmark",bg.transform,pressed);Center((RectTransform)check.transform,new Vector2(40,29),Vector2.zero);
        var toggle=bg.gameObject.AddComponent<Toggle>();toggle.targetGraphic=bg;toggle.graphic=check;return toggle;
    }
    private static ScrollRect ScrollArea(string name,Transform parent,Vector2 position,Vector2 size,out TMP_Text body)
    {
        var viewport=Rect(name,parent);Center(viewport,size,position);
        var hit=viewport.gameObject.AddComponent<Image>();hit.color=new Color(1,1,1,0);hit.raycastTarget=true;
        viewport.gameObject.AddComponent<RectMask2D>();
        var content=Rect("Content",viewport);content.anchorMin=new Vector2(0,1);content.anchorMax=new Vector2(1,1);
        content.pivot=new Vector2(.5f,1);content.anchoredPosition=Vector2.zero;content.sizeDelta=new Vector2(0,0);
        var text=content.gameObject.AddComponent<TextMeshProUGUI>();text.font=tmp;text.fontSize=23;text.color=new Color(.20f,.27f,.22f);
        text.alignment=TextAlignmentOptions.TopLeft;text.textWrappingMode=TextWrappingModes.Normal;text.raycastTarget=false;
        var fitter=content.gameObject.AddComponent<ContentSizeFitter>();fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;
        scroll.scrollSensitivity=26;body=text;return scroll;
    }
}

