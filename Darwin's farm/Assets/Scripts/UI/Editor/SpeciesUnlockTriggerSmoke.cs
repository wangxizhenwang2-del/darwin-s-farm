using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class SpeciesUnlockTriggerSmoke
{
    private const string Flag = "SpeciesUnlockTriggerSmokeActive";
    private static int frames, stage;
    private static double started;
    private static SpeciesUnlockPresenter presenter;
    private static EcologyNewsHUD hud;

    static SpeciesUnlockTriggerSmoke() => EditorApplication.update += Tick;

    public static void RunBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/WhiteBox.unity");
        SessionState.SetBool(Flag, true);
        frames = stage = 0;
        started = EditorApplication.timeSinceStartup;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Flag, false) || !EditorApplication.isPlaying) return;
        if (++frames < 5) return;
        frames = 0;
        try
        {
            if (EditorApplication.timeSinceStartup - started > 25)
                throw new TimeoutException("trigger smoke timed out");
            if (stage == 0)
            {
                presenter = UnityEngine.Object.FindFirstObjectByType<SpeciesUnlockPresenter>();
                hud = UnityEngine.Object.FindFirstObjectByType<EcologyNewsHUD>();
                var model = UnityEngine.Object.FindFirstObjectByType<SimulationController>();
                var sample = AssetDatabase.LoadAssetAtPath<SpeciesData>(
                    "Assets/BiowebV46/Species/灵感菇狸-761468a5.asset");
                Require(presenter != null && hud != null && model != null && sample != null,
                    "required scene components missing");
                // A scene or UI transition may leave the authored card disabled.
                presenter.gameObject.SetActive(false);
                var start = GameObject.Find("JournalCanvas").transform
                    .Find("SafeArea/TitlePanel/NewGame").GetComponent<Button>();
                start.onClick.Invoke();
                Require(presenter.gameObject.activeInHierarchy && presenter.enabled,
                    "new game did not reactivate the species card presenter");
                var species = ScriptableObject.CreateInstance<SpeciesData>();
                Set(species, "speciesName", "即时解锁测试");
                Set(species, "speciesId", "test:direct-unlock");
                Set(species, "portrait", sample.Portrait);
                var block = model.ReadBlocks().First(b => b != null && b.community != null);
                block.community.Add(new PopulationData {
                    species = species, speciesId = species.SpeciesId,
                    lineageName = species.SpeciesName, speciesAmount = 2
                });
                model.RefreshSpeciesStatuses();
                Require(presenter.IsPresenting && presenter.Phase == SpeciesUnlockPresenter.CardPhase.Entering,
                    "real OnSpeciesUnlocked event did not start card immediately");
                Require(hud.CaptureSaveState().current?.speciesId != species.SpeciesId,
                    "HUD appeared before card reveal");
                stage = 1;
                return;
            }
            if (stage == 1)
            {
                if (presenter.Phase != SpeciesUnlockPresenter.CardPhase.Revealing) return;
                Require(hud.CaptureSaveState().current?.speciesId == "test:direct-unlock",
                    "HUD did not appear at the real unlock reveal");
                Debug.Log("SpeciesUnlockTriggerSmoke: PASS real unlock event and reveal HUD timing.");
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

    private static void Set(SpeciesData species, string field, object value)
    {
        typeof(SpeciesData).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(species, value);
    }
    private static void Require(bool result, string message)
    {
        if (!result) throw new InvalidOperationException("SpeciesUnlockTriggerSmoke: " + message);
    }
}
