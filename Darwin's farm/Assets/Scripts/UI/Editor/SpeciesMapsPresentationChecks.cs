using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class SpeciesMapsPresentationChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run in Play Mode after entering the game.");
        var maps = UnityEngine.Object.FindFirstObjectByType<SpeciesMapsUI>();
        var mobile = UnityEngine.Object.FindFirstObjectByType<MobileGameUI>();
        var clock = UnityEngine.Object.FindFirstObjectByType<SimulationTime>();
        maps.Close(); bool paused = clock.IsPaused;
        mobile.Mode = MobileGameUI.DisplayMode.Mobile;
        mobile.SendMessage("ApplyMode",SendMessageOptions.RequireReceiver);
        Canvas.ForceUpdateCanvases();
        var data = new SerializedObject(mobile);
        var button = (UnityEngine.UI.Button)data.FindProperty("speciesMapButton").objectReferenceValue;
        button.onClick.Invoke(); Require(maps.IsOpen,"Mobile button did not open map");
        Require(clock.IsPaused,"Map did not pause simulation");
        var root = maps.transform.Find("Species Maps");
        Require(root.GetComponent<Canvas>().overrideSorting && root.GetComponent<Canvas>().sortingOrder > 200,"Modal sorting");
        Require(button.GetComponentInChildren<SpeciesNetworkIcon>().canvasRenderer.GetMesh().vertexCount > 0,"Missing mobile icon mesh");
        var build = (UnityEngine.UI.Button)data.FindProperty("buildButton").objectReferenceValue;
        Require(((RectTransform)button.transform).anchoredPosition.y < ((RectTransform)build.transform).anchoredPosition.y,"Mobile button position");
        var flags = Private;
        var show = maps.GetType().GetMethod("ShowClue",flags);
        var eligible = maps.GetType().GetMethod("CanShowClue",flags);
        var text = root.GetComponentsInChildren<TMPro.TMP_Text>(true).First(t=>t.name=="BreedingClue");
        var popup = text.transform.parent;
        int count=0;
        foreach(var node in root.GetComponentsInChildren<SpeciesMapNodeView>(true))
        {
            show.Invoke(maps,new object[]{node}); Canvas.ForceUpdateCanvases(); text.ForceMeshUpdate();
            bool canShow=(bool)eligible.Invoke(maps,new object[]{node.Species});
            Require(popup.gameObject.activeSelf==canShow,"Clue eligibility: "+node.name);
            if(canShow)
            {
                Require(!text.isTextOverflowing,"Clue overflow: "+node.name);
                string narrative=text.text.Split(new[]{"\n\n"},StringSplitOptions.None)[0];
                int redKeywords=System.Text.RegularExpressions.Regex.Matches(narrative,"<color=#C3342D>").Count;
                Require(redKeywords<=2,"Too many highlighted clue keywords: "+node.name);
            }
            count++;
        }
        maps.GetType().GetMethod("HideClue",flags).Invoke(maps,null);
        var buttons=root.GetComponentsInChildren<UnityEngine.UI.Button>(true);
        buttons.First(b=>b.name=="Switch Map"&&b.gameObject.activeInHierarchy).onClick.Invoke();
        Require(root.Find("Food Web").gameObject.activeSelf,"Switch to food web");
        buttons.First(b=>b.name=="Close Map"&&b.gameObject.activeInHierarchy).onClick.Invoke();
        Require(!maps.IsOpen && clock.IsPaused==paused,"Close/pause restoration");
        maps.Open();
        return $"PASS: mobile entry, icon mesh, placement, modal sorting, {count} clues, page switch, close and pause restoration ({Screen.width}x{Screen.height}).";
    }

    public static string TouchGestures()
    {
        var maps=UnityEngine.Object.FindFirstObjectByType<SpeciesMapsUI>(); maps.Open();
        var pages=(Array)maps.GetType().GetField("pages",Private).GetValue(maps);
        var page=pages.GetValue((int)maps.GetType().GetField("activePage",Private).GetValue(maps));
        var type=page.GetType();
        var viewport=(RectTransform)type.GetField("viewport").GetValue(page);
        var content=(RectTransform)type.GetField("content").GetValue(page);
        var setZoom=maps.GetType().GetMethod("SetZoom",Private);
        var zoom=type.GetField("zoom");
        float minimum=(float)maps.GetType().GetMethod("MinimumZoom",Private).Invoke(maps,new[]{page});
        setZoom.Invoke(maps,new object[]{page,minimum*1.3f});
        Canvas.ForceUpdateCanvases();
        var corners=new Vector3[4];viewport.GetWorldCorners(corners);
        Vector2 p=RectTransformUtility.WorldToScreenPoint(null,corners[0])+new Vector2(40,100);
        var update=maps.GetType().GetMethod("UpdateTouches",Private);
        var oldMode=InputSystem.settings.updateMode;
        var screen=InputSystem.AddDevice<Touchscreen>("SpeciesMapGestureCheck");
        try
        {
            InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsManually;
            void Contact(int id,UnityEngine.InputSystem.TouchPhase phase,Vector2 position)
            {
                InputSystem.QueueStateEvent(screen,new TouchState{touchId=id,phase=phase,position=position});
                InputSystem.Update();
            }
            Vector2 before=content.anchoredPosition;
            Contact(1,UnityEngine.InputSystem.TouchPhase.Began,p);update.Invoke(maps,new[]{page});
            Contact(1,UnityEngine.InputSystem.TouchPhase.Moved,p+new Vector2(80,60));update.Invoke(maps,new[]{page});
            Require(Vector2.Distance(before,content.anchoredPosition)>1f,"One-finger pan");
            Contact(1,UnityEngine.InputSystem.TouchPhase.Ended,p);update.Invoke(maps,new[]{page});
            Contact(1,UnityEngine.InputSystem.TouchPhase.Began,p);Contact(2,UnityEngine.InputSystem.TouchPhase.Began,p+new Vector2(100,0));
            update.Invoke(maps,new[]{page});float initial=(float)zoom.GetValue(page);
            Contact(2,UnityEngine.InputSystem.TouchPhase.Moved,p+new Vector2(150,0));update.Invoke(maps,new[]{page});
            Require((float)zoom.GetValue(page)>initial,"Two-finger pinch");
            Require(maps.SuppressNodeClicks,"Pinch must suppress accidental species tap");
            Contact(1,UnityEngine.InputSystem.TouchPhase.Ended,p);Contact(2,UnityEngine.InputSystem.TouchPhase.Ended,p+new Vector2(150,0));
            update.Invoke(maps,new[]{page});
            return "PASS: injected Input System touchscreen events: one-finger pan, two-finger zoom, suppress pinch taps, release.";
        }
        finally {InputSystem.RemoveDevice(screen);InputSystem.settings.updateMode=oldMode;}
    }

    public static string DesktopKeys()
    {
        var maps=UnityEngine.Object.FindFirstObjectByType<SpeciesMapsUI>();maps.Close();
        var journal=UnityEngine.Object.FindFirstObjectByType<DarwinJournalCanvas>();
        var update=maps.GetType().GetMethod("Update",Private);
        var journalUpdate=journal.GetType().GetMethod("Update",Private);
        var mode=InputSystem.settings.updateMode;
        var keyboard=InputSystem.AddDevice<Keyboard>("SpeciesMapKeyboardCheck");
        try
        {
            InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsManually;
            void Step(params Key[] keys)
            {
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.Update();
                journalUpdate.Invoke(journal,null);update.Invoke(maps,null);
            }
            Step(Key.M);Require(maps.IsOpen,"M opens map");Step();
            Step(Key.Escape);Require(!maps.IsOpen&&journal.IsGameplayVisible,"Escape must close map without opening menu");Step();
            Step(Key.M);Require(maps.IsOpen,"M reopens map");Step();
            Step(Key.M);Require(!maps.IsOpen,"M closes map");Step();
            return "PASS: injected keyboard: M open/close, Escape closes only map, journal remains in game.";
        }
        finally {InputSystem.RemoveDevice(keyboard);InputSystem.settings.updateMode=mode;}
    }

    public static void SetResolution(int width,int height)
    {
        var asm=typeof(Editor).Assembly;var sizesType=asm.GetType("UnityEditor.GameViewSizes");
        var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var sizes=singleton.GetProperty("instance").GetValue(null);
        var group=sizesType.GetMethod("GetGroup").Invoke(sizes,new[]{Enum.Parse(asm.GetType("UnityEditor.GameViewSizeGroupType"),"Standalone")});
        var groupType=group.GetType();
        int index=(int)groupType.GetMethod("GetBuiltinCount").Invoke(group,null)+(int)groupType.GetMethod("GetCustomCount").Invoke(group,null);
        var size=Activator.CreateInstance(asm.GetType("UnityEditor.GameViewSize"),new object[]{Enum.Parse(asm.GetType("UnityEditor.GameViewSizeType"),"FixedResolution"),width,height,"Species maps verification"});
        groupType.GetMethod("AddCustomSize").Invoke(group,new[]{size});
        var view=EditorWindow.GetWindow(asm.GetType("UnityEditor.GameView"));
        view.GetType().GetProperty("selectedSizeIndex",Private|BindingFlags.Public).SetValue(view,index);
    }

    private static void Require(bool condition,string detail) {if(!condition)throw new InvalidOperationException(detail);}
}
