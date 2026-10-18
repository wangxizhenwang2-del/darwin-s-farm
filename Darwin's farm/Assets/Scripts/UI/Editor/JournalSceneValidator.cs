using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class JournalSceneValidator
{
    public static void ValidateBatch()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/WhiteBox.unity");
        var root = scene.GetRootGameObjects().SingleOrDefault(x => x.name == "JournalCanvas");
        Require(root != null, "JournalCanvas missing");
        Require(root.GetComponent<Canvas>() != null, "Canvas missing");
        Require(root.GetComponent<CanvasScaler>() != null, "CanvasScaler missing");
        Require(root.GetComponent<GraphicRaycaster>() != null, "GraphicRaycaster missing");
        Require(root.GetComponent<DarwinJournalCanvas>() != null, "UI controller missing");
        Require(root.GetComponent<EcologyNewsDetector>() != null, "News detector missing");
        Require(root.GetComponentsInChildren<EcologyNewsHUD>(true).Length == 1, "News view count invalid");
        Require(scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<EventSystem>(true)).Any(),
            "EventSystem missing");
        foreach (string name in new[] {"TitlePanel", "PausePanel", "SettingsPanel", "TutorialPanel",
                     "InfoPanel", "SaveLoadPanel", "ConfirmDialog", "EcologyNewsToast"})
            Require(root.GetComponentsInChildren<Transform>(true).Any(x => x.name == name), name + " missing");
        var serialized = new SerializedObject(root.GetComponent<DarwinJournalCanvas>());
        foreach (string name in new[] {"safeArea", "titlePanel", "pausePanel", "settingsPanel",
                     "tutorialPanel", "infoPanel", "saveLoadPanel", "confirmDialog", "gameMenuButton",
                     "titleNew", "titleLoad", "titleExit", "titleSettings", "titleTutorial", "titleInfo",
                     "pauseResume", "pauseSettings", "pauseSaves", "pauseTutorial", "pauseTitle",
                     "settingsClose", "tutorialClose", "infoClose", "savesClose", "confirmYes", "confirmNo",
                     "confirmMessage", "saveMessage", "masterSlider", "musicSlider", "effectsSlider",
                     "qualityDropdown", "resolutionDropdown", "fullscreenToggle", "resolutionRow", "fullscreenRow",
                     "tutorial", "tutorialBody", "tutorialScroll", "tutorialIllustration", "infoScroll"})
        {
            var property = serialized.FindProperty(name);
            Require(property != null && property.objectReferenceValue != null, name + " unassigned");
        }
        Require(root.GetComponentsInChildren<Button>(true).Length >= 20, "Expected authored buttons missing");
        var categories=serialized.FindProperty("categoryButtons");
        var pages=serialized.FindProperty("pageButtons");
        Require(categories.arraySize==7&&pages.arraySize==5,"Seven-chapter navigation is incomplete");
        foreach(string field in new[]{"slotLoadButtons","slotSaveButtons","slotDeleteButtons","slotLabels"})
            Require(serialized.FindProperty(field).arraySize==3,field+" has no three-slot binding");
        var database=serialized.FindProperty("tutorial").objectReferenceValue as TutorialDatabase;
        Require(database.categories.Length==7&&database.categories.Sum(x=>x.pages.Length)==26,
            "Tutorial content is incomplete");
        Require(database.categories.SelectMany(x=>x.pages).Count(x=>x.illustration!=null)>=8,
            "Tutorial illustrations are not connected");
        Require(root.GetComponentsInChildren<TextMeshProUGUI>(true).All(x => x.font != null),
            "One or more labels has no font");
        Require(root.GetComponentsInChildren<Image>(true).Any(x => x.sprite != null && x.sprite.name == "TitleLandscape"),
            "Title illustration missing");
        foreach (string name in new[] {"BodyScroll", "InfoScroll"})
        {
            var scroll = root.GetComponentsInChildren<ScrollRect>(true).Single(x => x.name == name);
            Require(scroll.GetComponent<Image>()?.raycastTarget == true, name + " cannot receive drag input");
        }
        Debug.Log("JournalSceneValidator: PASS — serialized Canvas views, references, font, sprites and EventSystem.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("JournalSceneValidator: " + message);
    }
}
