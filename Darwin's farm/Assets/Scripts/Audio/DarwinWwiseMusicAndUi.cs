using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Connects the gameplay audio contract and Unity buttons to the authored Wwise banks.
[RequireComponent(typeof(AkGameObj))]
public sealed class DarwinWwiseMusicAndUi : MonoBehaviour
{
    private const string MusicBank = "SB_Music";
    private const string UiBank = "SB_UI";
    private readonly HashSet<Button> buttons = new HashSet<Button>();
    private readonly HashSet<string> loggedUiEvents = new HashSet<string>();
    private readonly HashSet<string> failedUiEvents = new HashSet<string>();
    private bool musicLoaded;
    private bool uiLoaded;
    private bool musicWarning;
    private bool uiWarning;
    private bool playing;
    private DarwinAudioDispatch.MusicPhase? appliedPhase;
    private bool? appliedMenu;
    private float appliedVolume = -1f;
    private float nextButtonScan;
    private SpeciesUnlockPresenter unlockPresenter;
    private EcologyNewsHUD newsHud;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindFirstObjectByType<DarwinWwiseMusicAndUi>() != null) return;
        var host = new GameObject("Darwin Wwise Music and UI");
        DontDestroyOnLoad(host);
        host.AddComponent<AkGameObj>();
        host.AddComponent<DarwinWwiseMusicAndUi>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        BindSceneEvents();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        nextButtonScan = 0f;
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextButtonScan)
        {
            RegisterButtons();
            BindSceneEvents();
            nextButtonScan = Time.unscaledTime + 1f;
        }

        if (!AkUnitySoundEngine.IsInitialized()) return;
        if (!musicLoaded) musicLoaded = LoadBank(MusicBank, ref musicWarning);
        if (!uiLoaded) uiLoaded = LoadBank(UiBank, ref uiWarning);

        if (musicLoaded)
        {
            var phase = DarwinAudioDispatch.CurrentMusicPhase;
            if (appliedPhase != phase)
            {
                AkUnitySoundEngine.SetState("BGM_Phase", phase == DarwinAudioDispatch.MusicPhase.Second ? "Second" : "First");
                appliedPhase = phase;
            }

            float volume = DarwinAudioDispatch.CurrentMusicVolume;
            if (!Mathf.Approximately(appliedVolume, volume))
            {
                AkUnitySoundEngine.SetRTPCValue("Music_Volume", volume);
                appliedVolume = volume;
            }

            bool shouldPlay = DarwinAudioDispatch.IsGameplayActive;
            if (shouldPlay && !playing)
            {
                playing = AkUnitySoundEngine.PostEvent("Play_MSC_BGM", gameObject) !=
                    AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
                if (playing) Debug.Log("Wwise music started: Play_MSC_BGM", this);
            }
            else if (!shouldPlay && playing)
            {
                AkUnitySoundEngine.PostEvent("Stop_MSC_BGM", gameObject);
                playing = false;
            }
        }

        if (uiLoaded)
        {
            bool menu = DarwinAudioDispatch.IsMenuDucked;
            if (appliedMenu != menu)
            {
                AkUnitySoundEngine.SetState("UI_Mix", menu ? "Menu" : "Gameplay");
                appliedMenu = menu;
            }
        }
    }

    private bool LoadBank(string name, ref bool warned)
    {
        uint bankId;
        AKRESULT result = AkUnitySoundEngine.LoadBank(name, out bankId);
        if (result == AKRESULT.AK_Success) return true;
        if (!warned)
        {
            Debug.LogError("Wwise: cannot load " + name + " (" + result + "). Check generated SoundBanks and WwiseSettings.xml.", this);
            warned = true;
        }
        return false;
    }

    private void RegisterButtons()
    {
        foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (button == null || !buttons.Add(button)) continue;
            button.onClick.AddListener(PlayClick);
        }
    }

    private void BindSceneEvents()
    {
        var presenter = FindFirstObjectByType<SpeciesUnlockPresenter>();
        if (presenter != unlockPresenter)
        {
            if (unlockPresenter != null) unlockPresenter.OnRevealStarted -= PlaySpeciesReveal;
            unlockPresenter = presenter;
            if (unlockPresenter != null) unlockPresenter.OnRevealStarted += PlaySpeciesReveal;
        }

        var hud = FindFirstObjectByType<EcologyNewsHUD>(FindObjectsInactive.Include);
        if (hud != newsHud)
        {
            if (newsHud != null) newsHud.NewsShown -= PlayNews;
            newsHud = hud;
            if (newsHud != null) newsHud.NewsShown += PlayNews;
        }
    }

    private void PlaySpeciesReveal(SpeciesUnlockPresenter.SpeciesUnlockData data)
    {
        PostUiEvent("Play_UI_Species_Reveal");
    }

    private void PlayNews(EcologicalEventType type)
    {
        string eventName;
        switch (type)
        {
            case EcologicalEventType.NewSpecies:
            case EcologicalEventType.TerrainUnlocked:
            case EcologicalEventType.WatersUnlocked:
                eventName = "Play_UI_News_good";
                break;
            case EcologicalEventType.SpeciesReappeared:
            case EcologicalEventType.SuccessfulMigration:
                eventName = "Play_UI_News_Mid";
                break;
            default:
                eventName = "Play_UI_News_Bad";
                break;
        }
        PostUiEvent(eventName);
    }

    private void PlayClick()
    {
        PostUiEvent("Play_UI_Click");
    }

    private void PostUiEvent(string eventName)
    {
        if (!uiLoaded || !AkUnitySoundEngine.IsInitialized()) return;
        uint playingId = AkUnitySoundEngine.PostEvent(eventName, gameObject);
        if (playingId == AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
        {
            if (failedUiEvents.Add(eventName))
                Debug.LogError("Wwise: " + eventName + " failed after loading SB_UI.", this);
        }
        else if (loggedUiEvents.Add(eventName))
        {
            Debug.Log("Wwise UI event started: " + eventName, this);
        }
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (unlockPresenter != null) unlockPresenter.OnRevealStarted -= PlaySpeciesReveal;
        if (newsHud != null) newsHud.NewsShown -= PlayNews;
        unlockPresenter = null;
        newsHud = null;
        foreach (Button button in buttons)
            if (button != null) button.onClick.RemoveListener(PlayClick);
        buttons.Clear();
        if (!AkUnitySoundEngine.IsInitialized()) return;
        if (playing) AkUnitySoundEngine.PostEvent("Stop_MSC_BGM", gameObject);
        playing = false;
        if (musicLoaded) AkUnitySoundEngine.UnloadBank(MusicBank, System.IntPtr.Zero);
        if (uiLoaded) AkUnitySoundEngine.UnloadBank(UiBank, System.IntPtr.Zero);
        musicLoaded = uiLoaded = false;
        appliedPhase = null;
        appliedMenu = null;
        appliedVolume = -1f;
    }
}
