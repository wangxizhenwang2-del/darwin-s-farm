using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Edits the existing scene Canvas in the editor. Runtime only moves authored
// RectTransforms and swaps the content of existing labels and images.
public static class JournalSceneUpgrader
{
    public static void UpgradeBatch()
    {
        const string path="Assets/Scenes/WhiteBox.unity";
        var scene=EditorSceneManager.OpenScene(path);
        var canvas=GameObject.Find("JournalCanvas") ?? throw new InvalidOperationException("JournalCanvas missing");
        var controller=canvas.GetComponent<DarwinJournalCanvas>();
        var tutorial=canvas.transform.Find("SafeArea/TutorialPanel/Paper");
        var saves=canvas.transform.Find("SafeArea/SaveLoadPanel/Paper");
        if(controller==null||tutorial==null||saves==null)throw new InvalidOperationException("Journal scene structure missing");
        var categoryButtons=new Button[7]; var categoryLabels=new TMP_Text[7];
        var pageButtons=new Button[5]; var pageLabels=new TMP_Text[5];
        var categoryTemplate=tutorial.Find("Category0").GetComponent<Button>();
        var pageTemplate=tutorial.Find("Page0").GetComponent<Button>();
        for(int i=0;i<7;i++)
        {
            var button=i<3?tutorial.Find("Category"+i).GetComponent<Button>():
                UnityEngine.Object.Instantiate(categoryTemplate,tutorial);
            button.name="Category"+i;
            categoryButtons[i]=button;categoryLabels[i]=button.GetComponentInChildren<TMP_Text>(true);
        }
        for(int i=0;i<5;i++)
        {
            var button=i<3?tutorial.Find("Page"+i).GetComponent<Button>():
                UnityEngine.Object.Instantiate(pageTemplate,tutorial);
            button.name="Page"+i;
            pageButtons[i]=button;pageLabels[i]=button.GetComponentInChildren<TMP_Text>(true);
        }
        var artObject=new GameObject("Illustration",typeof(RectTransform),typeof(Image));
        artObject.layer=LayerMask.NameToLayer("UI");
        artObject.transform.SetParent(tutorial,false);
        var art=artObject.GetComponent<Image>();
        art.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(
            "Assets/Resources/DarwinJournal/Illustrations/FiveSpecies.png");
        art.preserveAspect=true;art.raycastTarget=false;
        var artRect=(RectTransform)art.transform;
        artRect.anchorMin=artRect.anchorMax=new Vector2(.5f,.5f);
        artRect.sizeDelta=new Vector2(550,145);artRect.anchoredPosition=new Vector2(119,68);
        artObject.SetActive(false);
        var load=new Button[3];var save=new Button[3];var delete=new Button[3];
        var labels=new TMP_Text[3];
        for(int i=0;i<3;i++)
        {
            var card=saves.Find("Slot"+(i+1));
            var rect=(RectTransform)card;
            rect.sizeDelta=new Vector2(570,112);
            rect.anchoredPosition=new Vector2(0,100-i*135);
            labels[i]=card.Find("SlotText").GetComponent<TMP_Text>();
            var labelRect=(RectTransform)labels[i].transform;
            labelRect.sizeDelta=new Vector2(310,76);labelRect.anchoredPosition=new Vector2(-112,0);
            labels[i].fontSize=19;labels[i].alignment=TextAlignmentOptions.MidlineLeft;
            load[i]=SlotButton(pageTemplate,card,"Load","读取",95);
            save[i]=SlotButton(pageTemplate,card,"Save","保存",171);
            delete[i]=SlotButton(pageTemplate,card,"Delete","删除",247);
        }
        var status=saves.Find("Status") as RectTransform;
        status.anchoredPosition=new Vector2(0,198);
        var serialized=new SerializedObject(controller);
        Set(serialized,"tutorialIllustration",art);
        SetArray(serialized,"categoryButtons",categoryButtons);
        SetArray(serialized,"categoryLabels",categoryLabels);
        SetArray(serialized,"pageButtons",pageButtons);
        SetArray(serialized,"pageLabels",pageLabels);
        SetArray(serialized,"slotLoadButtons",load);
        SetArray(serialized,"slotSaveButtons",save);
        SetArray(serialized,"slotDeleteButtons",delete);
        SetArray(serialized,"slotLabels",labels);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save journal scene");
        Debug.Log("JournalSceneUpgrader: 7 chapters, 5 pages, illustration, 3 save slots authored.");
    }
    private static Button SlotButton(Button template,Transform parent,string name,string text,float x)
    {
        var button=UnityEngine.Object.Instantiate(template,parent);
        button.name=name;
        var rect=(RectTransform)button.transform;
        rect.sizeDelta=new Vector2(69,40);rect.anchoredPosition=new Vector2(x,0);
        var label=button.GetComponentInChildren<TMP_Text>(true);
        label.text=text;label.fontSize=17;
        ((RectTransform)label.transform).sizeDelta=new Vector2(65,36);
        return button;
    }
    private static void Set(SerializedObject obj,string field,UnityEngine.Object value)
        =>obj.FindProperty(field).objectReferenceValue=value;
    private static void SetArray<T>(SerializedObject obj,string field,T[] values) where T:UnityEngine.Object
    {
        var array=obj.FindProperty(field);array.arraySize=values.Length;
        for(int i=0;i<values.Length;i++)array.GetArrayElementAtIndex(i).objectReferenceValue=values[i];
    }
}
