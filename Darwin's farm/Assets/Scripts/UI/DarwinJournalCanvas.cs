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

    private SettingsData settings = new SettingsData();
    private Page page = Page.Title, parent = Page.Title;
    private SimulationTime clock;
    private Vector2Int[] resolutions;
    private Rect previousSafe;
    private Vector2Int previousScreen;
    private int currentCategory, currentPage;
    private bool inGame, busy;
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
        Show(Page.Title);
    }

    private void OnDestroy()
    {
        if (clock != null) clock.ReleaseModalPause();
        if (Time.timeScale == 0f) Time.timeScale = worldScale;
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
        if (back) Back();
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
        pauseTitle.onClick.AddListener(() => Open(Page.Confirm));
        settingsClose.onClick.AddListener(Back);
        tutorialClose.onClick.AddListener(Back);
        infoClose.onClick.AddListener(Back);
        savesClose.onClick.AddListener(Back);
        confirmYes.onClick.AddListener(ReturnToTitle);
        confirmNo.onClick.AddListener(Back);
        confirmMessage.text = "尚未保存的进度将丢失。确定回到标题吗？";
        saveMessage.text = "完整生态快照尚未接入。本版禁止写入或读取不完整进度。";
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
        if (page == Page.Confirm) { Show(Page.Pause); return; }
        Show(parent);
    }
    private void NewGame()
    {
        inGame = true;
        Time.timeScale = worldScale;
        clock.ReleaseModalPause();
        Show(Page.Game);
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
            float scale=Mathf.Min(1f,(safeArea.rect.width-24f)/paper.sizeDelta.x,
                (safeArea.rect.height-24f)/paper.sizeDelta.y);
            paper.localScale=Vector3.one*Mathf.Max(.25f,scale);
        }
        var toast=safeArea.Find("EcologyNewsToast") as RectTransform;
        if(toast!=null)toast.localScale=Vector3.one*Mathf.Min(1f,(safeArea.rect.width-24f)/toast.sizeDelta.x);
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
        tutorialBody.text="<size=130%>"+pages[index].title+"</size>\n\n"+pages[index].content;
        if(tutorialScroll!=null)tutorialScroll.verticalNormalizedPosition=1;
    }
}
