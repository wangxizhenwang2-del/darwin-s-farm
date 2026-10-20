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

// Main views and graphics are authored beneath this scene Canvas. The two
// extra test controls and export button clone its existing visual controls.
[DefaultExecutionOrder(-900)]
public sealed class DarwinJournalCanvas : MonoBehaviour
{
    public event Action<bool> GameEntered;
    public bool IsGameplayVisible => inGame && page == Page.Game;
    [Serializable] private sealed class SettingsData
    {
        public int schemaVersion = 3;
        public float master = 1, music = 1, effects = 1;
        public int quality = -1, width, height;
        public bool fullscreen = true;
        public bool testMode;
        public bool tutorialOnNewGame = true;
        public float testDifficulty;
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
    private Toggle testModeToggle, newGameTutorialToggle;
    private Slider testDifficultySlider;
    private LandRuleTuning testDifficultyBaseline;
    private Button exportTestButton;
    private TMP_Text exportTestStatus;
    private TestSessionRecorder testRecorder;
    public bool NewGameTutorialEnabled => settings.tutorialOnNewGame;
    [Header("Tutorial")]
    [SerializeField] private TutorialDatabase tutorial;
    [SerializeField] private Button[] categoryButtons, pageButtons;
    [SerializeField] private TMP_Text[] categoryLabels, pageLabels;
    [SerializeField] private TMP_Text tutorialBody;
    [SerializeField] private ScrollRect tutorialScroll, infoScroll;
    [SerializeField] private Image tutorialIllustration;
    [Header("Tutorial typography")]
    [SerializeField, Min(12f)] private float tutorialFontSize = 23f;
    [SerializeField, Min(1.2f)] private float tutorialLineHeight = 1.45f;
    [SerializeField, Min(0f)] private float tutorialParagraphSpacing = 18f;
    [SerializeField] private Button[] slotLoadButtons, slotSaveButtons, slotDeleteButtons;
    [SerializeField] private TMP_Text[] slotLabels;

    private SettingsData settings = new SettingsData();
    private Page page = Page.Title, parent = Page.Title;
    private SimulationTime clock;
    private SimulationController audioSimulation;
    private SaveAssetCatalog audioSpeciesCatalog;
    private Vector2Int[] resolutions;
    private Coroutine pendingDisplayChange;
    private static readonly string[] QualityLabels = { "高", "中", "低" };
    private static readonly int[] QualityLevels = { 2, 1, 0 };
    private Rect previousSafe;
    private Vector2Int previousScreen;
    private int currentCategory, currentPage;
    private int operationCategoryIndex = -1;
    private OperationGuideGallery operationGuide;
    private bool inGame, busy;
    private Action confirmedAction;
    private Page confirmReturn=Page.Pause;
    private float worldScale = 1f;

    private void Awake()
    {
        clock = FindFirstObjectByType<SimulationTime>();
        if (clock == null) { Debug.LogError("Journal Canvas needs SimulationTime in this scene.", this); enabled=false; return; }
        audioSimulation = FindFirstObjectByType<SimulationController>();
        if (audioSimulation != null)
            testDifficultyBaseline = audioSimulation.ReadLandRules();
        audioSpeciesCatalog = Resources.Load<SaveAssetCatalog>("DarwinJournal/SaveAssetCatalog");
        if (audioSimulation != null) audioSimulation.OnSpeciesUnlocked += OnAudioSpeciesUnlocked;
        worldScale = Time.timeScale > 0 ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        clock.AcquireModalPause();
        LoadSettings();
        ApplyTestDifficulty();
        testRecorder = GetComponent<TestSessionRecorder>();
        if (testRecorder == null) testRecorder = gameObject.AddComponent<TestSessionRecorder>();
        CreateExtraSettingsControls();
        CreateOperationGuide();
        Bind();
        UpdateSafeArea();
        Show(saveMessage.text.StartsWith("读档失败：",StringComparison.Ordinal)?Page.Saves:Page.Title);
    }

    private void OnDestroy()
    {
        if (audioSimulation != null) audioSimulation.OnSpeciesUnlocked -= OnAudioSpeciesUnlocked;
        DarwinAudioDispatch.SetGameplayActive(false);
        DarwinAudioDispatch.SetMenuOpen("Journal", false);
        if (clock != null) clock.ReleaseModalPause();
        if (Time.timeScale == 0f) Time.timeScale = worldScale;
    }

    private System.Collections.IEnumerator Start()
    {
        if(!GameSaveService.HasPending)yield break;
        yield return null; // Map and simulation bridge finish Start before restoration.
        yield return null; // WhiteboxBiowebTestSetup seeds its initial population one frame later.
        if(GameSaveService.TryRestorePending(out _))
        {
            ApplyTestDifficulty();
            EnterGame(true);
        }
        else ReloadForSave(); // Discard any world that was only partly restored.
    }

    private void Update()
    {
        if (previousScreen != new Vector2Int(Screen.width, Screen.height) || previousSafe != Screen.safeArea)
            UpdateSafeArea();
        if (page == Page.Tutorial && operationGuide != null && operationGuide.IsViewing &&
            AnyKeyboardKeyPressed())
        {
            operationGuide.CloseViewer();
            return;
        }
        bool back;
#if ENABLE_INPUT_SYSTEM
        back = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        back = Input.GetKeyDown(KeyCode.Escape);
#endif
        if (back)
        {
            var maps = FindFirstObjectByType<SpeciesMapsUI>();
            if (maps != null && maps.IsOpen) return;
            var unlock = FindFirstObjectByType<SpeciesUnlockPresenter>();
            if (unlock == null || !unlock.IsPresenting) Back();
        }
    }

    private static bool AnyKeyboardKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
#else
        // Legacy anyKeyDown also includes mouse input, so exclude pointer actions.
        return Input.anyKeyDown && Input.touchCount == 0 &&
            !Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1) &&
            !Input.GetMouseButtonDown(2);
#endif
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
        newGameTutorialToggle.SetIsOnWithoutNotify(settings.tutorialOnNewGame);
        newGameTutorialToggle.onValueChanged.AddListener(value => {
            settings.tutorialOnNewGame = value; SaveSettings();
        });
        testModeToggle.SetIsOnWithoutNotify(settings.testMode);
        testModeToggle.onValueChanged.AddListener(value => {
            settings.testMode = value;
            ApplyTestDifficulty();
            SaveSettings();
            if (!value) testRecorder.Stop();
            else if (inGame) StartTestRecording();
            RefreshTestExport();
        });
        testDifficultySlider.SetValueWithoutNotify(settings.testDifficulty);
        testDifficultySlider.interactable = settings.testMode;
        testDifficultySlider.onValueChanged.AddListener(value => {
            settings.testDifficulty = value;
            ApplyTestDifficulty();
            if (testRecorder != null && testRecorder.IsRecording)
                testRecorder.RecordTestDifficulty(value);
            SaveSettings();
        });
        exportTestButton.onClick.AddListener(ExportTestCode);
        RefreshTestExport();
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
        DarwinAudioDispatch.SetMenuOpen("Journal", target != Page.Game);
        titlePanel.SetActive(target == Page.Title);
        pausePanel.SetActive(target == Page.Pause);
        settingsPanel.SetActive(target == Page.Settings);
        if (target == Page.Settings)
        {
            RefreshDisplayOptions();
            RefreshTestExport();
        }
        tutorialPanel.SetActive(target == Page.Tutorial);
        if (target != Page.Tutorial) operationGuide?.CloseViewer();
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
        if (page == Page.Tutorial && operationGuide != null && operationGuide.IsViewing)
        {
            operationGuide.CloseViewer();
            return;
        }
        if (page == Page.Game) { Pause(); return; }
        if (page == Page.Pause) { Resume(); return; }
        if (page == Page.Confirm) { Show(confirmReturn); return; }
        Show(parent);
    }
    private void NewGame()
    {
        // Species assets survive scene reloads; begin each new run with fresh
        // discovery flags so the first deer unlock can open the water palette.
        var catalog = Resources.Load<SaveAssetCatalog>("DarwinJournal/SaveAssetCatalog");
        if (catalog != null)
        {
            foreach (var entry in catalog.species)
                if (entry.asset != null) entry.asset.RestoreProgress(false, false, 0);
            var simulation = FindFirstObjectByType<SimulationController>();
            if (simulation != null) simulation.RefreshSpeciesStatuses();
        }
        EnterGame(false);
    }
    private void EnterGame(bool loadedSave)
    {
        inGame = true;
        DarwinAudioDispatch.RefreshUnlockedSpecies(audioSpeciesCatalog);
        Time.timeScale = worldScale;
        clock.ReleaseModalPause();
        Show(Page.Game);
        DarwinAudioDispatch.SetGameplayActive(true);
        GameEntered?.Invoke(loadedSave);
        if (settings.testMode) StartTestRecording();
        else RefreshTestExport();
    }

    private void OnAudioSpeciesUnlocked(SpeciesData species)
    {
        if (inGame) DarwinAudioDispatch.RefreshUnlockedSpecies(audioSpeciesCatalog);
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
        if (area.width <= 0f || area.height <= 0f) return;
        previousSafe=area; previousScreen=new Vector2Int(Screen.width,Screen.height);
        safeArea.anchorMin=new Vector2(area.xMin/Screen.width,area.yMin/Screen.height);
        safeArea.anchorMax=new Vector2(area.xMax/Screen.width,area.yMax/Screen.height);
        safeArea.offsetMin=safeArea.offsetMax=Vector2.zero;
        // A landscape cutout makes Screen.safeArea exclude a strip for the entire
        // height. Let full-screen artwork and modal shades cover that strip;
        // the menu button and sizing calculations still use the safe rect.
        foreach (GameObject view in new[]{titlePanel,pausePanel,settingsPanel,tutorialPanel,
                     infoPanel,saveLoadPanel,confirmDialog})
        {
            if (view==null) continue;
            var rect=view.transform as RectTransform;
            if (rect==null) continue;
            rect.anchorMin=new Vector2(-area.xMin/area.width,-area.yMin/area.height);
            rect.anchorMax=new Vector2((Screen.width-area.xMin)/area.width,
                (Screen.height-area.yMin)/area.height);
            rect.offsetMin=rect.offsetMax=Vector2.zero;
        }
        Canvas.ForceUpdateCanvases();
        var landscape=titlePanel.transform.Find("Landscape") as RectTransform;
        if(landscape!=null)
        {
            float width=Mathf.Max(((RectTransform)titlePanel.transform).rect.width,
                ((RectTransform)titlePanel.transform).rect.height*16f/9f);
            float height=Mathf.Max(((RectTransform)titlePanel.transform).rect.height,
                ((RectTransform)titlePanel.transform).rect.width*9f/16f);
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
            if (currentCategory == operationCategoryIndex)
            {
                pageButtons[i].gameObject.SetActive(false);
                continue;
            }
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
                pageLabels[i].text=shortTitle;
                StyleTutorialButton(pageButtons[i],pageLabels[i],i==currentPage);
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
        operationGuide?.Layout(portrait);
    }

    private void BindSettings()
    {
        masterSlider.SetValueWithoutNotify(settings.master);
        musicSlider.SetValueWithoutNotify(settings.music);
        effectsSlider.SetValueWithoutNotify(settings.effects);
        masterSlider.onValueChanged.AddListener(v => { settings.master=v; AudioListener.volume=v; DarwinWwiseAmbience.SetMasterVolume(v); SaveSettings(); });
        musicSlider.onValueChanged.AddListener(v => { settings.music=v; ApplyCategorizedAudio(); SaveSettings(); });
        effectsSlider.onValueChanged.AddListener(v => { settings.effects=v; ApplyCategorizedAudio(); SaveSettings(); });
        qualityDropdown.ClearOptions();
        qualityDropdown.AddOptions(QualityLabels.ToList());
        qualityDropdown.SetValueWithoutNotify(Array.IndexOf(QualityLevels, settings.quality));
        qualityDropdown.onValueChanged.AddListener(i => {
            if (i < 0 || i >= QualityLevels.Length) return;
            settings.quality = QualityLevels[i];
            QualitySettings.SetQualityLevel(settings.quality, true);
            SaveSettings();
        });
        bool desktop=!Application.isMobilePlatform;
        resolutionRow.SetActive(desktop); fullscreenRow.SetActive(desktop);
        if (!desktop) return;
        RefreshDisplayOptions(false);
        resolutionDropdown.onValueChanged.AddListener(i => {
            if(i<0 || i>=resolutions.Length)return;
            settings.width=resolutions[i].x; settings.height=resolutions[i].y;
            ApplyDisplaySettings(); SaveSettings();
        });
        fullscreenToggle.SetIsOnWithoutNotify(settings.fullscreen);
        fullscreenToggle.onValueChanged.AddListener(v => { settings.fullscreen=v; ApplyDisplaySettings(); SaveSettings(); });
    }

    private void RefreshDisplayOptions(bool readActualSize = true)
    {
        if (Application.isMobilePlatform) return;
        // Read the actual window after Unity has applied any pending resolution change.
        if (readActualSize)
        {
            settings.width = Screen.width;
            settings.height = Screen.height;
#if !UNITY_EDITOR
            settings.fullscreen = Screen.fullScreen;
#endif
        }
        var available = Screen.resolutions.Select(r => new Vector2Int(r.width, r.height)).ToList();
        available.Add(new Vector2Int(settings.width, settings.height));
        available.Add(new Vector2Int(Screen.width, Screen.height));
        resolutions = available.Where(r => r.x > 0 && r.y > 0).Distinct()
            .OrderByDescending(r => (long)r.x * r.y).ThenByDescending(r => r.x).ToArray();
        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(resolutions.Select(r => r.x + " × " + r.y).ToList());
        int current = Array.FindIndex(resolutions, r => r.x == settings.width && r.y == settings.height);
        resolutionDropdown.SetValueWithoutNotify(Mathf.Max(0, current));
        fullscreenToggle.SetIsOnWithoutNotify(settings.fullscreen);
    }
    private void CreateExtraSettingsControls()
    {
        var paper = settingsPanel.transform.Find("Paper") as RectTransform;
        if (paper == null || fullscreenRow == null || pauseResume == null)
            throw new InvalidOperationException("设置面板缺少排版模板。");
        paper.sizeDelta = new Vector2(680, 700);
        foreach (Transform child in paper)
        {
            var rect = child as RectTransform;
            if (rect == null) continue;
            if (child.name == "Heading" || child.name == "Close")
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 310);
            else if (child.name == "AudioNote")
                rect.anchoredPosition = new Vector2(0, -320);
            else if (child.name == "主音量" || child.name == "主音量Track")
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 250);
            else if (child.name == "背景音乐" || child.name == "背景音乐Track")
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 200);
            else if (child.name == "音效" || child.name == "音效Track")
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 150);
            else if (child.name == "画质" || child.name == "画质Dropdown")
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 100);
            else if (child.name == "ResolutionRow")
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 48);
            else if (child.name == "FullscreenRow")
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -7);
        }
        newGameTutorialToggle = CloneSettingsToggle(paper, "NewGameTutorialRow",
            "新游戏进入教程", -64);
        testModeToggle = CloneSettingsToggle(paper, "TestModeRow", "测试模式", -121);
        var difficultyLabel = Instantiate(paper.Find("主音量"), paper);
        difficultyLabel.name = "TestDifficultyLabel";
        ((RectTransform)difficultyLabel).anchoredPosition = new Vector2(-185, -173);
        difficultyLabel.GetComponent<TMP_Text>().text = "进化/抗逆难度";
        difficultyLabel.GetComponent<TMP_Text>().fontSize = 20;
        testDifficultySlider = Instantiate(masterSlider, paper);
        testDifficultySlider.name = "TestDifficultySlider";
        ((RectTransform)testDifficultySlider.transform).anchoredPosition =
            new Vector2(105, -173);
        testDifficultySlider.onValueChanged.RemoveAllListeners();
        testDifficultySlider.minValue = -1f;
        testDifficultySlider.maxValue = 1f;
        testDifficultySlider.wholeNumbers = false;
        var difficultyNote = Instantiate(paper.Find("AudioNote"), paper);
        difficultyNote.name = "TestDifficultyNote";
        ((RectTransform)difficultyNote).anchoredPosition = new Vector2(0, -216);
        difficultyNote.GetComponent<TMP_Text>().fontSize = 16;
        difficultyNote.GetComponent<TMP_Text>().text =
            "左：易进化、更抗逆     中：当前     右：难进化、更脆弱";
        exportTestButton = Instantiate(pauseResume, paper);
        exportTestButton.name = "ExportTestCode";
        exportTestButton.onClick.RemoveAllListeners();
        var buttonRect = (RectTransform)exportTestButton.transform;
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(.5f, .5f);
        buttonRect.sizeDelta = new Vector2(255, 52);
        buttonRect.anchoredPosition = new Vector2(0, -265);
        exportTestButton.GetComponentInChildren<TMP_Text>(true).text = "复制测试分享码";
        exportTestStatus = paper.Find("AudioNote")?.GetComponent<TMP_Text>();
        if (exportTestStatus != null) exportTestStatus.text = "开启测试模式并进入游戏后，可复制本局记录。";
    }
    private Toggle CloneSettingsToggle(RectTransform paper, string name, string label, float y)
    {
        var row = Instantiate(fullscreenRow, paper);
        row.name = name;
        var rect = (RectTransform)row.transform;
        rect.anchoredPosition = new Vector2(0, y);
        var caption = row.transform.Find("Name")?.GetComponent<TMP_Text>();
        if (caption != null) caption.text = label;
        var toggle = row.GetComponentInChildren<Toggle>(true);
        if (toggle == null) throw new InvalidOperationException("设置面板缺少 Toggle 模板。");
        toggle.onValueChanged.RemoveAllListeners();
        return toggle;
    }
    private void ApplyTestDifficulty()
    {
        if (testDifficultySlider != null)
            testDifficultySlider.interactable = settings.testMode;
        if (audioSimulation == null || testDifficultyBaseline == null) return;
        float difficulty = settings.testMode ?
            Mathf.Clamp(settings.testDifficulty, -1f, 1f) : 0f;
        LandRuleTuning rules = testDifficultyBaseline.Copy();
        if (!Mathf.Approximately(difficulty, 0f))
        {
            // Formal evolution: all five traits must fit, then the food gates
            // must remain satisfied for consecutive simulation days.
            rules.evolutionTraitTolerance = difficulty < 0f
                ? Mathf.RoundToInt(Mathf.Lerp(
                    testDifficultyBaseline.evolutionTraitTolerance, 10f,
                    -difficulty))
                : Mathf.Max(0, Mathf.RoundToInt(Mathf.Lerp(
                    testDifficultyBaseline.evolutionTraitTolerance, 2f,
                    difficulty)));
            rules.siblingAttractionScale =
                testDifficultyBaseline.siblingAttractionScale *
                (1f - 0.5f * difficulty);
            float daysScale = 1f + 0.6f * difficulty;
            rules.evolutionRequiredDays = Mathf.Max(1, Mathf.RoundToInt(
                testDifficultyBaseline.evolutionRequiredDays * daysScale));
            rules.evolutionRequiredDaysAtFour = Mathf.Max(1, Mathf.RoundToInt(
                testDifficultyBaseline.evolutionRequiredDaysAtFour * daysScale));
            rules.evolutionTodaySatisfaction = Mathf.Clamp01(
                testDifficultyBaseline.evolutionTodaySatisfaction +
                0.08f * difficulty);
            rules.evolutionRenewalSatisfaction = Mathf.Clamp01(
                testDifficultyBaseline.evolutionRenewalSatisfaction +
                0.08f * difficulty);
            // These terms directly control food demand and daily mortality.
            float mortalityScale = 1f + 0.5f * difficulty;
            rules.adultMaintenanceScale =
                testDifficultyBaseline.adultMaintenanceScale *
                (1f + 0.25f * difficulty);
            rules.starvationDeathRate = Mathf.Clamp01(
                testDifficultyBaseline.starvationDeathRate * mortalityScale);
            rules.coldDeathRate =
                testDifficultyBaseline.coldDeathRate * mortalityScale;
            rules.climateMismatchDeathRate =
                testDifficultyBaseline.climateMismatchDeathRate * mortalityScale;
            rules.baselineAdultDeathRate =
                testDifficultyBaseline.baselineAdultDeathRate * mortalityScale;
            rules.smallBodyFrailtyRate =
                testDifficultyBaseline.smallBodyFrailtyRate * mortalityScale;
            rules.reproductionStressPerBirth =
                testDifficultyBaseline.reproductionStressPerBirth * mortalityScale;
        }
        audioSimulation.ApplyLandRules(rules);
    }
    private void StartTestRecording()
    {
        if (!inGame || testRecorder == null) return;
        if (!testRecorder.Begin(out string error))
        {
            if (exportTestStatus != null) exportTestStatus.text = "无法记录：" + error;
            Debug.LogWarning("Test recording failed: " + error);
        }
        else if (exportTestStatus != null) exportTestStatus.text = "正在记录本局；导出后会复制到剪贴板。";
        RefreshTestExport();
    }
    private void RefreshTestExport()
    {
        if (exportTestButton != null)
            exportTestButton.interactable = settings.testMode && inGame && testRecorder != null && testRecorder.IsRecording;
    }
    private async void ExportTestCode()
    {
        if (testRecorder == null || !testRecorder.IsRecording) return;
        exportTestButton.interactable = false;
        if (exportTestStatus != null) exportTestStatus.text = "正在生成分享码…";
        try
        {
            string code = await testRecorder.CreateCodeAsync();
            if (this == null) return;
            GUIUtility.systemCopyBuffer = code;
            if (exportTestStatus != null)
                exportTestStatus.text = "已复制分享码（" + code.Length + " 字符）" +
                    (testRecorder.IsTruncated ? "；记录达到容量上限。" : "。");
        }
        catch (Exception e)
        {
            if (this != null && exportTestStatus != null) exportTestStatus.text = "导出失败：" + e.Message;
        }
        finally { if (this != null) RefreshTestExport(); }
    }
    private void ApplyDisplaySettings()
    {
        if (Application.isMobilePlatform || settings.width <= 0 || settings.height <= 0) return;
        Screen.SetResolution(settings.width, settings.height,
            settings.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
#if UNITY_EDITOR
        // A fixed Game view ignores Screen.SetResolution in this Editor version.
        // Preview the selected size as well, so settings can be exercised in Play Mode.
        var viewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
        var preview = viewType?.GetMethod("SetCustomResolution",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        if (preview != null)
        {
            var view = UnityEditor.EditorWindow.GetWindow(viewType);
            preview.Invoke(view, new object[] { new Vector2(settings.width, settings.height), "游戏设置" });
            view.Repaint();
        }
#endif
        if (pendingDisplayChange != null) StopCoroutine(pendingDisplayChange);
        pendingDisplayChange = StartCoroutine(RefreshAppliedDisplay());
    }
    private System.Collections.IEnumerator RefreshAppliedDisplay()
    {
        // SetResolution is deferred until the end of the frame, including while paused.
        yield return null;
        yield return null;
        RefreshDisplayOptions();
        SaveSettings();
        pendingDisplayChange = null;
    }
    private string SettingsPath => Path.Combine(Application.persistentDataPath,"Config","settings.json");
    private void LoadSettings()
    {
        settings = new SettingsData {
            quality = QualitySettings.GetQualityLevel(),
            width = Screen.width,
            height = Screen.height,
            fullscreen = Screen.fullScreen
        };
        bool hasSavedSettings = false, migratedSettings = false;
        try
        {
            if(File.Exists(SettingsPath))
            {
                string json=File.ReadAllText(SettingsPath);
                if(json.Length<16384)
                {
                    settings=JsonUtility.FromJson<SettingsData>(json)??settings;
                    if (settings.schemaVersion < 2)
                    {
                        settings.tutorialOnNewGame = true;
                        settings.schemaVersion = 2;
                    }
                    if (settings.schemaVersion < 3)
                    {
                        // Old indices represented Mobile (0) and PC (1), not quality tiers.
                        settings.quality = settings.quality == 0 ? 0 : 2;
                        settings.schemaVersion = 3;
                        migratedSettings = true;
                    }
                    hasSavedSettings=true;
                }
            }
        }
        catch(Exception e){Debug.LogWarning("Journal settings load failed: "+e.Message);}
        settings.master=Mathf.Clamp01(settings.master);
        settings.music=Mathf.Clamp01(settings.music);
        settings.effects=Mathf.Clamp01(settings.effects);
        settings.testDifficulty=Mathf.Clamp(settings.testDifficulty,-1f,1f);
        AudioListener.volume=settings.master;
        DarwinWwiseAmbience.SetMasterVolume(settings.master);
        settings.quality = Mathf.Clamp(settings.quality, 0, QualityLevels.Length - 1);
        QualitySettings.SetQualityLevel(settings.quality, true);
        if (settings.width <= 0 || settings.height <= 0)
        {
            settings.width = Screen.width;
            settings.height = Screen.height;
        }
        if (hasSavedSettings) ApplyDisplaySettings();
        if (migratedSettings) SaveSettings();
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
        DarwinWwiseAmbience.SetEffectsVolume(settings.effects);
        DarwinAudioCategory.SetCategoryVolumes(settings.music, settings.effects);
        DarwinAudioDispatch.SetMusicVolume(settings.music);
    }
    private void CreateOperationGuide()
    {
        if (tutorial == null || categoryButtons == null || categoryButtons.Length == 0 ||
            tutorialPanel == null || tutorialBody == null) return;
        operationCategoryIndex = tutorial.categories.Length;
        var template = categoryButtons[0];
        var button = Instantiate(template, template.transform.parent);
        button.name = "CategoryOperationGuide";
        button.onClick.RemoveAllListeners();
        var label = button.GetComponentInChildren<TMP_Text>(true);
        Array.Resize(ref categoryButtons, operationCategoryIndex + 1);
        Array.Resize(ref categoryLabels, operationCategoryIndex + 1);
        categoryButtons[operationCategoryIndex] = button;
        categoryLabels[operationCategoryIndex] = label;
        operationGuide = tutorialPanel.AddComponent<OperationGuideGallery>();
        operationGuide.Build((RectTransform)tutorialPanel.transform.Find("Paper"),
            safeArea, tutorialBody);
    }

    private void SelectCategory(int index)
    {
        if(tutorial==null || index<0 || index>operationCategoryIndex)return;
        currentCategory=index;
        bool guide = index == operationCategoryIndex;
        for(int i=0;i<categoryButtons.Length;i++)
        {
            bool exists=i<tutorial.categories.Length || i==operationCategoryIndex;
            categoryButtons[i].gameObject.SetActive(exists);
            if(exists)
            {
                categoryLabels[i].text=i==operationCategoryIndex?
                    "操作说明":tutorial.categories[i].title;
                StyleTutorialButton(categoryButtons[i],categoryLabels[i],i==index);
            }
        }
        operationGuide?.SetVisible(guide);
        tutorialScroll.gameObject.SetActive(!guide);
        if (tutorialIllustration != null && guide)
            tutorialIllustration.gameObject.SetActive(false);
        if (guide)
        {
            foreach (var button in pageButtons) button.gameObject.SetActive(false);
            LayoutTutorial(safeArea.rect.height > safeArea.rect.width * 1.18f);
            return;
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
        if (currentCategory == operationCategoryIndex) return;
        var pages=tutorial.categories[currentCategory].pages;
        if(index<0 || index>=pages.Length)return;
        currentPage=index;
        for(int i=0;i<pageButtons.Length;i++)
            if(i<pages.Length)
            {
                pageLabels[i].text=pages[i].title;
                StyleTutorialButton(pageButtons[i],pageLabels[i],i==index);
            }
        string content=Application.isMobilePlatform&&!string.IsNullOrEmpty(pages[index].mobileContent)
            ?pages[index].mobileContent:pages[index].content;
        tutorialBody.enableAutoSizing=false;
        tutorialBody.fontSize=tutorialFontSize;
        tutorialBody.lineSpacing=0f;
        tutorialBody.paragraphSpacing=tutorialParagraphSpacing;
        string lineHeight=tutorialLineHeight.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
        tutorialBody.text="<line-height="+lineHeight+"em><size=115%><color=#2E4A3A><b>"+
            pages[index].title+"</b></color></size>\n"+content+"</line-height>";
        if(tutorialIllustration!=null)
        {
            tutorialIllustration.sprite=pages[index].illustration;
            tutorialIllustration.gameObject.SetActive(pages[index].illustration!=null);
            LayoutTutorial(safeArea.rect.height>safeArea.rect.width*1.18f);
        }
        if(tutorialScroll!=null)tutorialScroll.verticalNormalizedPosition=1;
    }

    private static void StyleTutorialButton(Button button,TMP_Text label,bool selected)
    {
        label.fontStyle=(label.fontStyle&~FontStyles.Bold)|(selected?FontStyles.Bold:FontStyles.Normal);
        label.color=selected?new Color32(40,74,58,255):new Color32(61,72,55,255);
        var colors=button.colors;
        Color normal=selected?new Color(.84f,.91f,.77f,1f):Color.white;
        colors.normalColor=normal;
        colors.selectedColor=normal;
        colors.highlightedColor=new Color(.91f,.95f,.84f,1f);
        colors.pressedColor=new Color(.75f,.84f,.66f,1f);
        button.transition=Selectable.Transition.ColorTint;
        button.colors=colors;
    }
}
