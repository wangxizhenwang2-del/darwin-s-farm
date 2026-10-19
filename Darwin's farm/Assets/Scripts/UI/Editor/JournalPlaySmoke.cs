using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class JournalPlaySmoke
{
    private const string Flag = "DarwinJournalPlaySmoke";
    private static int frames;
    static JournalPlaySmoke() => EditorApplication.update += Tick;

    public static void RunBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/WhiteBox.unity");
        SessionState.SetBool(Flag, true);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Flag, false) || !EditorApplication.isPlaying || ++frames < 4) return;
        SessionState.SetBool(Flag, false);
        try
        {
            var root = GameObject.Find("JournalCanvas");
            Require(root != null, "Canvas missing in Play Mode");
            var safe = root.transform.Find("SafeArea");
            var title = safe.Find("TitlePanel");
            var pause = safe.Find("PausePanel");
            var settings = safe.Find("SettingsPanel");
            var menu = safe.Find("GameMenuButton");
            Require(title.gameObject.activeSelf && Time.timeScale == 0f, "Title did not pause world");
            Click(title.Find("Settings"));
            Require(settings.Find("Paper/NewGameTutorialRow")?.GetComponentInChildren<Toggle>() != null,
                "New-game tutorial setting missing");
            Require(settings.Find("Paper/TestModeRow")?.GetComponentInChildren<Toggle>() != null,
                "Test mode setting missing");
            var export = settings.Find("Paper/ExportTestCode")?.GetComponent<Button>();
            Require(export != null && !export.interactable, "Export should be disabled before a game starts");
            Click(settings.Find("Paper/Close"));
            Click(title.Find("NewGame"));
            Require(menu.gameObject.activeSelf && Time.timeScale > 0f, "New game did not resume world");
            Click(menu);
            Require(pause.gameObject.activeSelf && Time.timeScale == 0f, "Pause did not stop world");
            Click(pause.Find("Paper/Settings"));
            Require(settings.gameObject.activeSelf && Time.timeScale == 0f, "Settings lost pause");
            Click(settings.Find("Paper/Close"));
            Require(pause.gameObject.activeSelf && Time.timeScale == 0f, "Settings did not return to pause");
            Click(pause.Find("Paper/Resume"));
            Require(menu.gameObject.activeSelf && Time.timeScale > 0f, "Resume did not restart world");
            Debug.Log("JournalPlaySmoke: PASS — title, new game, pause, settings/back and resume.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }
    private static void Click(Transform target)
    {
        Require(target != null, "Button missing");
        target.GetComponent<Button>().onClick.Invoke();
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("JournalPlaySmoke: " + message);
    }
}
