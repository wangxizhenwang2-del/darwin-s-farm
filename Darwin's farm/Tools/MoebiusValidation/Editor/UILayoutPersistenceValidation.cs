using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class UILayoutPersistenceValidation
{
    const string Phase = "UILayoutPersistenceValidation.Phase";
    static double deadline;
    static UILayoutPersistenceValidation() { EditorApplication.update += Tick; }
    public static void Run()
    {
        if (!Application.isBatchMode || !File.Exists("UILayoutFixture.marker")) throw new Exception("Isolated fixture required.");
        Directory.CreateDirectory("Assets/Resources/GameUI");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
        var text = CreateText(canvas.transform, "StaticText");
        var button = text.gameObject.AddComponent<Button>();
        var colors = button.colors; colors.highlightedColor = Color.magenta; button.colors = colors;
        var style = new UILayoutOverrides.Entry { values = UILayoutOverrides.Capture(button) };
        button.colors = ColorBlock.defaultColorBlock;
        UILayoutOverrides.Apply(button, style);
        Require(button.colors.highlightedColor == Color.magenta, "Button state colors were not restored.");
        text.text = "Runtime data is not saved";
        text.fontSize = 14;
        EditorSceneManager.SaveScene(scene, "Assets/LayoutProbe.unity");
        SessionState.SetInt(Phase, 1);
        deadline = 0;
        EditorApplication.EnterPlaymode();
    }
    static Text CreateText(Transform parent, string name)
    {
        var owner = new GameObject(name, typeof(RectTransform), typeof(Text));
        owner.transform.SetParent(parent, false);
        var text = owner.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = "Live data";
        return text;
    }
    static void Tick()
    {
        int phase = SessionState.GetInt(Phase, 0);
        if (phase == 0) return;
        try
        {
            if (phase == 1 && EditorApplication.isPlaying)
            {
                CreateText(GameObject.Find("Canvas").transform, "GeneratedText");
                SessionState.SetInt(Phase, 2); deadline = EditorApplication.timeSinceStartup + 1;
            }
            else if (phase == 2 && EditorApplication.isPlaying && EditorApplication.timeSinceStartup > deadline)
            {
                foreach (string name in new[] { "StaticText", "GeneratedText" })
                {
                    var text = GameObject.Find(name).GetComponent<Text>();
                    Undo.RecordObjects(new UnityEngine.Object[] { text, text.rectTransform }, "Inspector UI edit");
                    text.fontSize = 27;
                    text.color = Color.cyan;
                    text.rectTransform.anchoredPosition = new Vector2(37, -52);
                }
                Undo.FlushUndoRecordObjects();
                SessionState.SetInt(Phase, 3); deadline = EditorApplication.timeSinceStartup + .2;
            }
            else if (phase == 3 && EditorApplication.isPlaying && EditorApplication.timeSinceStartup > deadline)
            {
                UILayoutAuthoringWindow.SaveTracked();
                var settings = Resources.Load<UILayoutOverrides>(UILayoutOverrides.ResourcePath);
                Require(settings != null && settings.entries.Count >= 4, "Undo-driven Play edits were not saved.");
                foreach (var entry in settings.entries) foreach (var value in entry.values)
                    Require(value.property != "text", "Live text was captured.");
                SessionState.SetInt(Phase, 4);
                EditorApplication.ExitPlaymode(); deadline = EditorApplication.timeSinceStartup + .5;
            }
            else if (phase == 4 && !EditorApplication.isPlayingOrWillChangePlaymode && EditorApplication.timeSinceStartup > deadline)
            {
                UILayoutAuthoringWindow.ApplyToOpenScenes();
                var text = GameObject.Find("StaticText").GetComponent<Text>();
                Check(text);
                Require(text.text == "Runtime data is not saved", "Live data overwrote authored text.");
                EditorSceneManager.SaveScene(text.gameObject.scene);
                var settings = Resources.Load<UILayoutOverrides>(UILayoutOverrides.ResourcePath);
                Require(settings.entries.Count == 2, "Baked scene overrides were not retired.");
                SessionState.SetInt(Phase, 5);
                EditorApplication.EnterPlaymode();
            }
            else if (phase == 5 && EditorApplication.isPlaying)
            {
                Check(GameObject.Find("StaticText").GetComponent<Text>());
                CreateText(GameObject.Find("Canvas").transform, "GeneratedText");
                SessionState.SetInt(Phase, 6); deadline = EditorApplication.timeSinceStartup + 1;
            }
            else if (phase == 6 && EditorApplication.isPlaying && EditorApplication.timeSinceStartup > deadline)
            {
                Check(GameObject.Find("GeneratedText").GetComponent<Text>());
                SessionState.SetInt(Phase, 7); EditorApplication.ExitPlaymode();
            }
            else if (phase == 7 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetInt(Phase, 0);
                File.WriteAllText("Result.txt", "PASS: Inspector/Undo edits captured; static layout restored after Play and scene save; generated UI restored on a second Play; runtime text unchanged.");
                Debug.Log(File.ReadAllText("Result.txt")); EditorApplication.Exit(0);
            }
        }
        catch (Exception error)
        {
            SessionState.SetInt(Phase, 0); File.WriteAllText("Result.txt", "FAIL: " + error);
            Debug.LogException(error); EditorApplication.Exit(1);
        }
    }
    static void Check(Text text)
    {
        Require(text.fontSize == 27 && text.color == Color.cyan &&
            Vector2.Distance(text.rectTransform.anchoredPosition, new Vector2(37, -52)) < .001f,
            "Position/font/color did not survive: " + text.name);
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
