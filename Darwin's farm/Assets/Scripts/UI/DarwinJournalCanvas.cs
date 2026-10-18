using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// All views and graphics are authored beneath this scene Canvas. Runtime code
// only binds controls, changes their state, and updates the safe-area anchors.
[DefaultExecutionOrder(-900)]
public sealed class DarwinJournalCanvas : MonoBehaviour
{
    public event Action<bool> GameEntered;
    [Serializable] private sealed class SettingsData
    {
        public int schemaVersion = 1;
        public float master = 1, music = 1, effects = 1;
        public int quality = -1, width, height;
        public bool fullscreen = true;
    }
    private enum Page { Title, Game, Pause, Settings, Tutorial, Info, Saves, Confirm }

    [Header("Authored scene views")]
    [SerializeField] private RectTransform safeArea;
    [SerializeField] private GameObject titlePanel, pausePanel, settingsPanel, tutorialPanel;
    [SerializeField] private GameObject infoPanel, saveLoadPanel, confirmDialog, gameMenuButton;
    [SerializeField] private Button titleNew, titleLoad, titleExit, titleSettings, titleTutorial, titleInfo;
    [SerializeField] private Button pauseResume, pauseSettings, pauseSaves, pauseTutorial, pauseTitle;
    [SerializeField] private Button settingsClose, tutorialClose, infoClose, savesClose, confirmYes, confirmNo;
    [SerializeField] private TMP_Text confirmMessage, saveMessage;
    [Header("Settings")]
    [SerializeField] private Slider masterSlider, musicSlider, effectsSlider;
    [SerializeField] private TMP_Dropdown qualityDropdown, resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private GameObject resolutionRow, fullscreenRow;
    [Header("Tutorial")]
    [SerializeField] private TutorialDatabase tutorial;
    [SerializeField] private Button[] categoryButtons, pageButtons;
    [SerializeField] private TMP_Text[] categoryLabels, pageLabels;
    [SerializeField] private TMP_Text tutorialBody;
    [SerializeField] private ScrollRect tutorialScroll, infoScroll;
    [SerializeField] private Image tutorialIllustration;
    [SerializeField] private Button[] slotLoadButtons, slotSaveButtons, slotDeleteButtons;
    [SerializeField] private TMP_Text[] slotLabels;

    private SettingsData settings = new SettingsData();
    private Page page = Page.Title, parent = Page.Title;
    private SimulationTime clock;
    private Vector2Int[] resolutions;
    private Rect previousSafe;
    private Vector2Int previousScreen;
    private int currentCategory, currentPage;
    private bool inGame, busy;
    private Action confirmedAction;
    private Page confirmReturn=Page.Pause;
    private float worldScale = 1f;

    private void Awake()
    {
        clock = FindFirstObjectByType<SimulationTime>();
        if (clock == null) { Debug.LogError("Journal Canvas needs SimulationTime in this scene.", this); enabled=false; return; }
        worldScale = Time.timeScale > 0 ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        clock.AcquireModalPause();
        LoadSettings();
        Bind();
        UpdateSafeArea();
        Show(saveMessage.text.StartsWith("读档失败：",StringComparison.Ordinal)?Page.Saves:Page.Title);
    }

    private void OnDestroy()
    {
        if (clock != null) clock.ReleaseModalPause();
        if (Time.timeScale == 0f) Time.timeScale = worldScale;
    }

    private System.Collections.IEnumerator Start()
    {
        if(!GameSaveService.HasPending)yield break;
        yield return null; // Map and simulation bridge finish Start before restoration.
        yield return null; // WhiteboxBiowebTestSetup seeds its initial population one frame later.
        if(GameSaveService.TryRestorePending(out _))EnterGame(true);
        else ReloadForSave(); // Discard any world that was only partly restored.
    }

    private void Update()
    {
        if (previousScreen != new Vector2Int(Screen.width, Screen.height) || previousSafe != Screen.safeArea)
            UpdateSafeArea();
        bool back;
#if ENABLE_INPUT_SYSTEM
        back = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        back = Input.GetKeyDown(KeyCode.Escape);
#endif
        if (back)
        {
            var unlock = FindFirstObjectByType<SpeciesUnlockPresenter>();
            if (unlock == null || !unlock.IsPresenting) Back();
        }
    }

    private void Bind()
    {
        titleNew.onClick.AddListener(NewGame);
        titleLoad.onClick.AddListener(() => Open(Page.Saves));
        titleSettings.onClick.AddListener(() => Open(Page.Settings));
        titleTutorial.onClick.AddListener(() => Open(Page.Tutorial));
        titleInfo.onClick.AddListener(() => Open(Page.Info));
        titleExit.gameObject.SetActive(!Application.isMobilePlatform);
        if(Application.isMobilePlatform)
        {
            ((RectTransform)titleNew.transform).anchoredPosition=new Vector2(0,-5);
            ((RectTransform)titleLoad.transform).anchoredPosition=new Vector2(0,-86);
        }
        titleExit.onClick.AddListener(Application.Quit);
        gameMenuButton.GetComponent<Button>().onClick.AddListener(Pause);
        pauseResume.onClick.AddListener(Resume);
        pauseSettings.onClick.AddListener(() => Open(Page.Settings));
        pauseSaves.onClick.AddListener(() => Open(Page.Saves));
        pauseTutorial.onClick.AddListener(() => Open(Page.Tutorial));
        pauseTitle.onClick.AddListener(() => Confirm("尚未保存的进度将丢失。确定回到标题吗？",ReturnToTitle));
        settingsClose.onClick.AddListener(Back);
        tutorialClose.onClick.AddListener(Back);
        infoClose.onClick.AddListener(Back);
        savesClose.onClick.AddListener(Back);
        confirmYes.onClick.AddListener(() => { Action action=confirmedAction; confirmedAction=null; action?.Invoke(); });
        confirmNo.onClick.AddListener(Back);
        string loadError=GameSaveService.ConsumePendingError();
        saveMessage.text = string.IsNullOrEmpty(loadError)?"选择一个存档槽位。":"读档失败："+loadError;
        for(int i=0;i<3;i++)
        {
            int slot=i+1;
            slotLoadButtons[i].onClick.AddListener(() => RequestLoad(slot));
            slotSaveButtons[i].onClick.AddListener(() => RequestSave(slot));
            slotDeleteButtons[i].onClick.AddListener(() => RequestDelete(slot));
        }
        BindSettings();
        if (tutorial != null)
        {
            for (int i=0; i<categoryButtons.Length; i++)
            {
                int index=i;
                categoryButtons[i].onClick.AddListener(() => SelectCategory(index));
            }
            for (int i=0; i<pageButtons.Length; i++)
            {
                int index=i;
                pageButtons[i].onClick.AddListener(() => SelectTutorialPage(index));
            }
            SelectCategory(0);
        }
    }

    private void Show(Page target)
    {
        page = target;
        titlePanel.SetActive(target == Page.Title);
        pausePanel.SetActive(target == Page.Pause);
        settingsPanel.SetActive(target == Page.Settings);
        tutorialPanel.SetActive(target == Page.Tutorial);
        infoPanel.SetActive(target == Page.Info);
        saveLoadPanel.SetActive(target == Page.Saves);
        if(target==Page.Saves)RefreshSlots();
        confirmDialog.SetActive(target == Page.Confirm);
        gameMenuButton.SetActive(target == Page.Game);
        if (target == Page.Tutorial && tutorialScroll != null) tutorialScroll.verticalNormalizedPosition=1;
        if (target == Page.Info && infoScroll != null) infoScroll.verticalNormalizedPosition=1;
    }
    private void Open(Page target)
    {
        if (busy) return;
        parent = page;
        Show(target);
    }
    private void Back()
    {
        if (busy) return;
        if (page == Page.Game) { Pause(); return; }
        if (page == Page.Pause) { Resume(); return; }
        if (page == Page.Confirm) { Show(confirmReturn); return; }
        Show(parent);
    }
    private void NewGame() => EnterGame(false);
    private void EnterGame(bool loadedSave)
    {
        inGame = true;
        Time.timeScale = worldScale;
        clock.ReleaseModalPause();
        Show(Page.Game);
        GameEntered?.Invoke(loadedSave);
    }
    private void Pause()
    {
        if (!inGame || page != Page.Game) return;
        clock.AcquireModalPause();
        Time.timeScale = 0f;
        Show(Page.Pause);
    }
    private void Resume()
    {
        clock.ReleaseModalPause();
        Time.timeScale = worldScale;
        Show(Page.Game);
    }
    private void ReturnToTitle()
    {
        if (busy) return;
        busy = true;
        // Scene reload clears the old world, subscriptions, and UI state.
        Scene scene = SceneManager.GetActiveScene();
        if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
#if UNITY_EDITOR
        else UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#endif
    }
    private void ReloadForSave()
    {
        busy=true;
        Scene scene=SceneManager.GetActiveScene();
        if(scene.buildIndex>=0)SceneManager.LoadScene(scene.buildIndex);
#if UNITY_EDITOR
        else UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path,new LoadSceneParameters(LoadSceneMode.Single));
#endif
    }
    private void Confirm(string message,Action action)
    {
        if(busy)return;
        confirmReturn=page;
        confirmedAction=action;
        confirmMessage.text=message;
        Show(Page.Confirm);
    }
    private void RefreshSlots()
    {
        if(slotLabels==null||slotLabels.Length!=3)return;
        for(int i=0;i<3;i++)
        {
            var info=GameSaveService.ReadSlot(i+1);
            string date=DateTime.TryParse(info.timestamp,null,
                System.Globalization.DateTimeStyles.RoundtripKind,out DateTime parsed)
                ?parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):info.timestamp;
            slotLabels[i].text=!info.exists ? $"槽位 {i+1} · 空白" :
                !info.valid ? $"槽位 {i+1} · 文件损坏" :
                $"槽位 {i+1} · 第 {info.day} 日{(info.backup?" · 备份":"")}\n{date}";
            slotLoadButtons[i].interactable=info.valid;
            slotSaveButtons[i].gameObject.SetActive(inGame);
            slotDeleteButtons[i].gameObject.SetActive(inGame && info.exists);
        }
    }
    private void RequestSave(int slot)
    {
        if(!inGame)return;
        var info=GameSaveService.ReadSlot(slot);
        Confirm(info.exists?$"覆盖槽位 {slot} 的已有存档？":$"保存到槽位 {slot}？",()=>
        {
            Show(Page.Saves);
            saveMessage.text=GameSaveService.TrySave(slot,out string error)?"存档完成。":"存档失败："+error;
            RefreshSlots();
        });
    }
    private void RequestLoad(int slot)
    {
        Confirm($"读取槽位 {slot}？当前未保存的进度会丢失。",()=>
        {
            if(GameSaveService.TryPrepareLoad(slot,out string error))ReloadForSave();
            else {Show(Page.Saves);saveMessage.text="读档失败："+error;RefreshSlots();}
        });
    }
    private void RequestDelete(int slot)
    {
        Confirm($"永久删除槽位 {slot} 的存档？",()=>
        {
            Show(Page.Saves);
            saveMessage.text=GameSaveService.TryDelete(slot,out string error)?"存档已删除。":"删除失败："+error;
            RefreshSlots();
        });
    }
    private void UpdateSafeArea()
    {
        if (safeArea == null || Screen.width <= 0 || Screen.height <= 0) return;
        Rect area=Screen.safeArea;
        previousSafe=area; previousScreen=new Vector2Int(Screen.width,Screen.height);
        safeArea.anchorMin=new Vector2(area.xMin/Screen.width,area.yMin/Screen.height);
        safeArea.anchorMax=new Vector2(area.xMax/Screen.width,area.yMax/Screen.height);
        safeArea.offsetMin=safeArea.offsetMax=Vector2.zero;
        Canvas.ForceUpdateCanvases();
        var landscape=titlePanel.transform.Find("Landscape") as RectTransform;
        if(landscape!=null)
        {
            float width=Mathf.Max(safeArea.rect.width,safeArea.rect.height*16f/9f);
            float height=Mathf.Max(safeArea.rect.height,safeArea.rect.width*9f/16f);
            landscape.anchorMin=landscape.anchorMax=new Vector2(.5f,.5f);
            landscape.sizeDelta=new Vector2(width,height);
            landscape.anchoredPosition=Vector2.zero;
        }
        float titleScale=Mathf.Min(1f,(safeArea.rect.width-24f)/720f);
        foreach(string name in new[]{"GameTitle","Subtitle"})
        {
            var titleText=titlePanel.transform.Find(name);
            if(titleText!=null)titleText.localScale=Vector3.one*Mathf.Max(.25f,titleScale);
        }
        foreach(GameObject view in new[]{pausePanel,settingsPanel,tutorialPanel,infoPanel,saveLoadPanel,confirmDialog})
        {
            if(view==null)continue;
            var paper=view.transform.Find("Paper") as RectTransform;
            if(paper==null)continue;
            if(view==tutorialPanel)LayoutTutorial(safeArea.rect.height>safeArea.rect.width*1.18f);
            float scale=Mathf.Min(1f,(safeArea.rect.width-24f)/paper.sizeDelta.x,
                (safeArea.rect.height-24f)/paper.sizeDelta.y);
            paper.localScale=Vector3.one*Mathf.Max(.25f,scale);
        }
        var toast=safeArea.Find("EcologyNewsToast") as RectTransform;
        if(toast!=null)toast.localScale=Vector3.one*Mathf.Min(1f,(safeArea.rect.width-24f)/toast.sizeDelta.x);
    }
    private void LayoutTutorial(bool portrait)
    {
        var paper=tutorialPanel.transform.Find("Paper") as RectTransform;
        if(paper==null)return;
        paper.sizeDelta=portrait?new Vector2(680,1040):new Vector2(960,620);
        var heading=paper.Find("Heading") as RectTransform;
        var close=paper.Find("Close") as RectTransform;
        if(heading!=null)
        {
            heading.anchoredPosition=new Vector2(0,portrait?468:259);
            heading.sizeDelta=new Vector2(portrait?530:800,52);
        }
        if(close!=null)close.anchoredPosition=new Vector2(portrait?295:435,portrait?475:265);
        for(int i=0;i<categoryButtons.Length;i++)
        {
            var item=(RectTransform)categoryButtons[i].transform;
            item.sizeDelta=portrait?new Vector2(145,46):new Vector2(205,44);
            item.anchoredPosition=portrait?
                new Vector2(-225+(i%4)*150,397-(i/4)*52):new Vector2(-337,201-i*49);
            categoryLabels[i].fontSize=portrait?18:19;
            ((RectTransform)categoryLabels[i].transform).sizeDelta=item.sizeDelta-new Vector2(8,4);
        }
        for(int i=0;i<pageButtons.Length;i++)
        {
            var item=(RectTransform)pageButtons[i].transform;
            item.sizeDelta=portrait?new Vector2(110,43):new Vector2(123,43);
            item.anchoredPosition=portrait?
                new Vector2(-230+i*116,286):new Vector2(-172+i*130,171);
            pageLabels[i].fontSize=portrait?16:17;
            ((RectTransform)pageLabels[i].transform).sizeDelta=item.sizeDelta-new Vector2(8,4);
            var pages=tutorial.categories[currentCategory].pages;
            if(i<pages.Length)
            {
                string shortTitle=pages[i].title;
                int limit=portrait?5:6;
                if(shortTitle.Length>limit)shortTitle=shortTitle.Substring(0,limit)+"…";
                pageLabels[i].text=(i==currentPage?"◆":"")+shortTitle;
            }
        }
        bool art=tutorialIllustration!=null&&tutorialIllustration.gameObject.activeSelf;
        var body=(RectTransform)tutorialScroll.transform;
        body.sizeDelta=portrait?new Vector2(590,art?600:745):new Vector2(642,art?260:380);
        body.anchoredPosition=portrait?new Vector2(0,art?-210:-129):
            new Vector2(119,art?-136:-60);
        if(tutorialIllustration!=null)
        {
            var illustration=(RectTransform)tutorialIllustration.transform;
            illustration.sizeDelta=portrait?new Vector2(550,145):new Vector2(550,145);
            illustration.anchoredPosition=portrait?new Vector2(0,177):new Vector2(119,68);
        }
    }

    private void BindSettings()
    {
        masterSlider.SetValueWithoutNotify(settings.master);
        musicSlider.SetValueWithoutNotify(settings.music);
        effectsSlider.SetValueWithoutNotify(settings.effects);
        masterSlider.onValueChanged.AddListener(v => { settings.master=v; AudioListener.volume=v; SaveSettings(); });
        musicSlider.onValueChanged.AddListener(v => { settings.music=v; ApplyCategorizedAudio(); SaveSettings(); });
        effectsSlider.onValueChanged.AddListener(v => { settings.effects=v; ApplyCategorizedAudio(); SaveSettings(); });
        qualityDropdown.ClearOptions();
        qualityDropdown.AddOptions(QualitySettings.names.ToList());
        qualityDropdown.SetValueWithoutNotify(Mathf.Clamp(QualitySettings.GetQualityLevel(),0,Mathf.Max(0,qualityDropdown.options.Count-1)));
        qualityDropdown.onValueChanged.AddListener(i => { QualitySettings.SetQualityLevel(i,true); settings.quality=i; SaveSettings(); });
        bool desktop=!Application.isMobilePlatform;
        resolutionRow.SetActive(desktop); fullscreenRow.SetActive(desktop);
        if (!desktop) return;
        resolutions=Screen.resolutions.Select(r=>new Vector2Int(r.width,r.height)).Distinct()
            .OrderBy(r=>r.x*r.y).ToArray();
        if (resolutions.Length==0) resolutions=new[] {new Vector2Int(Screen.width,Screen.height)};
        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(resolutions.Select(r=>r.x+" × "+r.y).ToList());
        int current=Array.FindIndex(resolutions,r=>r.x==Screen.width && r.y==Screen.height);
        resolutionDropdown.SetValueWithoutNotify(Mathf.Max(0,current));
        resolutionDropdown.onValueChanged.AddListener(i => {
            if(i<0 || i>=resolutions.Length)return;
            settings.width=resolutions[i].x; settings.height=resolutions[i].y;
            Screen.SetResolution(settings.width,settings.height,Screen.fullScreenMode); SaveSettings();
        });
        fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
        fullscreenToggle.onValueChanged.AddListener(v => { Screen.fullScreen=v; settings.fullscreen=v; SaveSettings(); });
    }
    private string SettingsPath => Path.Combine(Application.persistentDataPath,"Config","settings.json");
    private void LoadSettings()
    {
        try
        {
            if(File.Exists(SettingsPath))
            {
                string json=File.ReadAllText(SettingsPath);
                if(json.Length<16384)settings=JsonUtility.FromJson<SettingsData>(json)??new SettingsData();
            }
        }
        catch(Exception e){Debug.LogWarning("Journal settings load failed: "+e.Message);}
        settings.master=Mathf.Clamp01(settings.master);
        settings.music=Mathf.Clamp01(settings.music);
        settings.effects=Mathf.Clamp01(settings.effects);
        AudioListener.volume=settings.master;
        if(settings.quality>=0 && settings.quality<QualitySettings.names.Length)
            QualitySettings.SetQualityLevel(settings.quality,true);
        if(!Application.isMobilePlatform)
        {
            Screen.fullScreen=settings.fullscreen;
            if(settings.width>0 && Screen.resolutions.Any(r=>r.width==settings.width&&r.height==settings.height))
                Screen.SetResolution(settings.width,settings.height,Screen.fullScreenMode);
        }
        ApplyCategorizedAudio();
    }
    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            string temporary=SettingsPath+".tmp";
            File.WriteAllText(temporary,JsonUtility.ToJson(settings,true));
            if(File.Exists(SettingsPath))
            {
                try { File.Replace(temporary,SettingsPath,null); }
                catch(PlatformNotSupportedException)
                {
                    File.Copy(temporary,SettingsPath,true);
                    File.Delete(temporary);
                }
            }
            else File.Move(temporary,SettingsPath);
        }
        catch(Exception e){Debug.LogWarning("Journal settings save failed: "+e.Message);}
    }
    private void ApplyCategorizedAudio()
    {
        foreach(var source in FindObjectsByType<DarwinAudioCategory>(FindObjectsSortMode.None))
            source.Apply(settings.music,settings.effects);
    }
    private void SelectCategory(int index)
    {
        if(tutorial==null || index<0 || index>=tutorial.categories.Length)return;
        currentCategory=index;
        for(int i=0;i<categoryButtons.Length;i++)
        {
            bool exists=i<tutorial.categories.Length;
            categoryButtons[i].gameObject.SetActive(exists);
            if(exists)categoryLabels[i].text=(i==index?"◆ ":"▸ ")+tutorial.categories[i].title;
        }
        var pages=tutorial.categories[index].pages;
        for(int i=0;i<pageButtons.Length;i++)
        {
            bool exists=i<pages.Length;
            pageButtons[i].gameObject.SetActive(exists);
            if(exists)pageLabels[i].text=pages[i].title;
        }
        SelectTutorialPage(0);
    }
    private void SelectTutorialPage(int index)
    {
        var pages=tutorial.categories[currentCategory].pages;
        if(index<0 || index>=pages.Length)return;
        currentPage=index;
        for(int i=0;i<pageButtons.Length;i++)
            if(i<pages.Length)pageLabels[i].text=(i==index?"◆ ":"  ")+pages[i].title;
        string content=Application.isMobilePlatform&&!string.IsNullOrEmpty(pages[index].mobileContent)
            ?pages[index].mobileContent:pages[index].content;
        tutorialBody.text="<size=130%>"+pages[index].title+"</size>\n\n"+content;
        if(tutorialIllustration!=null)
        {
            tutorialIllustration.sprite=pages[index].illustration;
            tutorialIllustration.gameObject.SetActive(pages[index].illustration!=null);
            LayoutTutorial(safeArea.rect.height>safeArea.rect.width*1.18f);
        }
        if(tutorialScroll!=null)tutorialScroll.verticalNormalizedPosition=1;
    }
}
