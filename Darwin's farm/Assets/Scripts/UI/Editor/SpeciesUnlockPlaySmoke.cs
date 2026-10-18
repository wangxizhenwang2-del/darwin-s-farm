using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[InitializeOnLoad]
public static class SpeciesUnlockPlaySmoke
{
    private const string Flag = "SpeciesUnlockPlaySmokeActive";
    private static int stage, frames, startupClosed, manualClosed;
    private static double stageSince, started;
    private static SpeciesUnlockPresenter presenter;
    private static EcologyNewsHUD hud;
    private static Transform card;
    private static Sprite testSprite;
    private static int revealCallbacks;
    private static bool mysterySeen;

    static SpeciesUnlockPlaySmoke() => EditorApplication.update += Tick;

    public static void RunBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/WhiteBox.unity");
        SessionState.SetBool(Flag, true);
        stage = frames = startupClosed = manualClosed = revealCallbacks = 0;
        started = stageSince = EditorApplication.timeSinceStartup;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Flag, false) || !EditorApplication.isPlaying) return;
        if (++frames < 8) return;
        frames = 0;
        try
        {
            if (EditorApplication.timeSinceStartup - started > 95)
                throw new TimeoutException("unlock smoke timed out at stage " + stage);
            if (stage == 0)
            {
                presenter = UnityEngine.Object.FindFirstObjectByType<SpeciesUnlockPresenter>();
                hud = UnityEngine.Object.FindFirstObjectByType<EcologyNewsHUD>();
                Require(presenter != null && hud != null, "presenter or news HUD missing");
                card = GameObject.Find("JournalCanvas").transform.Find("SafeArea/SpeciesUnlockUI");
                var sample = AssetDatabase.LoadAssetAtPath<SpeciesData>(
                    "Assets/BiowebV46/Species/灵感菇狸-761468a5.asset");
                testSprite = sample.Portrait;
                Require(testSprite != null, "test creature portrait missing");
                Require(presenter.Phase == SpeciesUnlockPresenter.CardPhase.Hidden,
                    "card appeared before starting game");
                presenter.OnRevealStarted += OnReveal;
                Click(GameObject.Find("JournalCanvas").transform.Find("SafeArea/TitlePanel/NewGame"));
                stageSince = EditorApplication.timeSinceStartup;
                stage = 1;
                return;
            }
            if (stage == 1)
            {
                if (EditorApplication.timeSinceStartup - stageSince < 1.0)
                    Require(!presenter.IsPresenting, "startup card appeared without delay");
                if (presenter.Phase == SpeciesUnlockPresenter.CardPhase.Mystery)
                {
                    AssertMystery();
                    mysterySeen = true;
                    stage = 2;
                }
                return;
            }
            if (stage == 2)
            {
                if (presenter.Phase != SpeciesUnlockPresenter.CardPhase.Revealing) return;
                Require(mysterySeen && revealCallbacks == 1,
                    "startup reveal callback was not at reveal phase");
                Require(hud.CaptureSaveState().current?.type == EcologicalEventType.NewSpecies,
                    "startup news HUD did not begin at reveal");
                stage = 3;
                return;
            }
            if (stage == 3)
            {
                if (presenter.Phase != SpeciesUnlockPresenter.CardPhase.Ready) return;
                Require(card.Find("CardRoot/ContinueButton").GetComponent<Button>().interactable,
                    "continue not enabled after description");
                Continue();
                startupClosed++;
                stage = 4;
                return;
            }
            if (stage == 4)
            {
                if (presenter.Phase == SpeciesUnlockPresenter.CardPhase.Ready)
                {
                    Continue();
                    startupClosed++;
                }
                if (presenter.IsPresenting) return;
                Require(startupClosed > 0, "no starting species presented");
                presenter.Show(new SpeciesUnlockPresenter.SpeciesUnlockData {
                    SpeciesId = "test:inspiration", CreatureSprite = testSprite,
                    SpeciesName = "灵感菇狸",
                    Description = "一种长着蘑菇尾巴的奇妙生物，喜欢在温暖潮湿的环境中生活。"
                });
                presenter.Show(new SpeciesUnlockPresenter.SpeciesUnlockData {
                    SpeciesId = "test:second", CreatureSprite = testSprite,
                    SpeciesName = "第二种", Description = "队列测试。"
                });
                presenter.Show(new SpeciesUnlockPresenter.SpeciesUnlockData {
                    SpeciesId = "test:third", CreatureSprite = testSprite,
                    SpeciesName = "第三种", Description = "队列测试。"
                });
                Require(presenter.QueuedCount == 2, "three unlocks did not queue");
                stage = 5;
                return;
            }
            if (stage == 5)
            {
                if (presenter.Phase == SpeciesUnlockPresenter.CardPhase.Mystery)
                    AssertMystery();
                if (presenter.Phase == SpeciesUnlockPresenter.CardPhase.Revealing)
                {
                    Require(hud.CaptureSaveState().current?.speciesName == "灵感菇狸",
                        "manual news was not released at reveal");
                    stage = 6;
                }
                return;
            }
            if (stage == 6)
            {
                if (presenter.Phase != SpeciesUnlockPresenter.CardPhase.Ready) return;
                var description = card.Find("CardRoot/DescriptionGroup/DescriptionText")
                    .GetComponent<TMP_Text>();
                Require(description.text.Contains("蘑菇尾巴"), "description did not appear");
                Require(card.Find("CardRoot/DescriptionGroup").GetComponent<CanvasGroup>().alpha > .99f,
                    "description stayed transparent");
                Continue();
                manualClosed++;
                Require(presenter.QueuedCount == 1 && presenter.IsPresenting,
                    "second card did not begin after continue");
                stage = 7;
                return;
            }
            if (stage == 7)
            {
                if (presenter.Phase != SpeciesUnlockPresenter.CardPhase.Ready) return;
                Require(card.Find("CardRoot/NameText").GetComponent<TMP_Text>().text == "第二种",
                    "queue order or card reset wrong");
                Continue();
                manualClosed++;
                stage = 8;
                return;
            }
            if (stage == 8)
            {
                if (presenter.Phase != SpeciesUnlockPresenter.CardPhase.Ready) return;
                Require(card.Find("CardRoot/NameText").GetComponent<TMP_Text>().text == "第三种",
                    "third card missing");
                Continue();
                manualClosed++;
                Require(!presenter.IsPresenting && presenter.QueuedCount == 0,
                    "queue did not empty cleanly");
                Require(manualClosed == 3 && revealCallbacks >= startupClosed + 3,
                    "reveal callback count wrong");
                Debug.Log("SpeciesUnlockPlaySmoke: PASS startup delay, silhouette, reveal HUD, " +
                    "description, three-card queue and continue.");
                SessionState.SetBool(Flag, false);
                EditorApplication.Exit(0);
            }
        }
        catch (Exception error)
        {
            SessionState.SetBool(Flag, false);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void AssertMystery()
    {
        Require(card.Find("CardRoot/NameText").GetComponent<TMP_Text>().text == "我是谁？？？",
            "real name leaked in silhouette phase");
        Require(card.Find("CardRoot/CreatureArea/CreatureImage").GetComponent<Image>().color.a < .01f,
            "color portrait appeared before reveal");
    }
    private static void OnReveal(SpeciesUnlockPresenter.SpeciesUnlockData data)
    {
        Require(presenter.Phase == SpeciesUnlockPresenter.CardPhase.Revealing,
            "callback fired outside reveal");
        revealCallbacks++;
    }
    private static void Continue() =>
        card.Find("CardRoot/ContinueButton").GetComponent<Button>().onClick.Invoke();
    private static void Click(Transform target) => target.GetComponent<Button>().onClick.Invoke();
    private static void Require(bool result, string message)
    {
        if (!result) throw new InvalidOperationException("SpeciesUnlockPlaySmoke: " + message);
    }
}
