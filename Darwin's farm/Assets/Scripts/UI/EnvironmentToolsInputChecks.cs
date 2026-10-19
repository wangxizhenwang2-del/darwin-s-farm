#if UNITY_EDITOR
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Play-mode integration checks use real Input System events through the touch/PC routers.
public sealed class EnvironmentToolsInputChecks : MonoBehaviour
{
    public static string Result { get; private set; } = "Not run";
    private Touchscreen screen;
    private MobileGameUI mobile;
    private MobileGameUI.DisplayMode originalMode;
    private GameInformationUI information;
    private EnvironmentToolSession session;
    private Vector2 point;
    public static void Run()
    {
        if (!Application.isPlaying) throw new System.InvalidOperationException("Run in Play mode.");
        new GameObject("EnvironmentToolsInputChecks").AddComponent<EnvironmentToolsInputChecks>();
    }
    private void Require(bool condition,string message)
    {
        if (condition) return;
        Result="FAILED: "+message; Debug.LogError(Result); throw new System.InvalidOperationException(Result);
    }
    private void Touch(int id,UnityEngine.InputSystem.TouchPhase phase,Vector2 position)
    { InputSystem.QueueStateEvent(screen,new TouchState { touchId=id,phase=phase,position=position }); }
    private IEnumerator Start()
    {
        Result="Running";
        mobile=FindFirstObjectByType<MobileGameUI>(); originalMode=mobile.Mode;
        information=FindFirstObjectByType<GameInformationUI>(); session=information.GetComponent<EnvironmentToolSession>();
        var clock=FindFirstObjectByType<SimulationTime>(); if(!clock.IsPaused)clock.TogglePause();
        mobile.Mode=MobileGameUI.DisplayMode.Mobile;
        information.CloseTile(); information.ClosePopulation(); session.Begin();
        yield return new WaitForSecondsRealtime(.7f);
        var tile=FindFirstObjectByType<MapTileInstance>();
        point=Camera.main.WorldToScreenPoint(tile.transform.position+new Vector3(-3,0,-2));
        Require(Camera.main.pixelRect.Contains(point),"Test point must be visible");
        screen=InputSystem.AddDevice<Touchscreen>("EnvironmentToolsTestTouch");
        Touch(61,UnityEngine.InputSystem.TouchPhase.Began,point);
        yield return new WaitForSecondsRealtime(.08f);
        Require(information.SelectedTile==null,"Touch selected before release");
        Touch(61,UnityEngine.InputSystem.TouchPhase.Ended,point);
        yield return new WaitForSecondsRealtime(.15f);
        Require(information.SelectedTile==tile,"Touch tap did not select the tool target");
        Require(information.GetComponent<SelectionOutlinePresenter>().HighlightedTileCount==1,"Selected tile outline missing");
        var env=FindFirstObjectByType<SimulationEnvironmentController>();
        env.TryRead(tile.Coordinate,out var before);
        var account=FindFirstObjectByType<BioenergyAccount>(); long money=account.Balance;
        session.SelectKind(4);
        Require(session.Choice.Kind == DarwinFarm.Environment.EnvironmentTechnologyKind.PlantStockChange,
            "Plant stock button maps to the wrong tool");
        session.SelectKind(3); session.SelectOption(1); session.Refresh();
        Require(session.Choice.Kind == DarwinFarm.Environment.EnvironmentTechnologyKind.PlantRecoveryChange,
            "Plant recovery button maps to the wrong tool");
        env.TryRead(tile.Coordinate,out var previewed);
        Require(before.PlantStock==previewed.PlantStock && account.Balance==money,"Preview changed ecology or balance");
        int cost=session.Preview.Cost; session.Deploy();
        env.TryRead(tile.Coordinate,out var after);
        Require(after.Environment.Recovery.Phase==DarwinFarm.Environment.EffectPhase.Entering &&
            after.Environment.Recovery.Value==before.Environment.Recovery.Value &&
            after.Environment.Recovery.Target==System.Math.Min(150000,before.Environment.Recovery.Value+25000),
            "Plant recovery did not begin its 25-day transition");
        Require(account.Balance==money-cost,"Wrong payment or duplicate touch commit");
        session.CancelEffect(); Require(session.Notice=="已撤销效果","Effect cancellation failed");
        information.CloseTile();
        yield return new WaitForSecondsRealtime(.2f);
        Touch(62,UnityEngine.InputSystem.TouchPhase.Began,point);
        yield return new WaitForSecondsRealtime(.06f);
        Touch(62,UnityEngine.InputSystem.TouchPhase.Moved,point+Vector2.right*60);
        yield return new WaitForSecondsRealtime(.06f);
        Touch(62,UnityEngine.InputSystem.TouchPhase.Ended,point+Vector2.right*60);
        yield return new WaitForSecondsRealtime(.15f);
        Require(information.SelectedTile==null,"Pan accidentally selected a tile");
        InputSystem.RemoveDevice(screen); screen=null;
        mobile.Mode=MobileGameUI.DisplayMode.Desktop;
        yield return new WaitForSecondsRealtime(.3f);
        point=Camera.main.WorldToScreenPoint(tile.transform.position+new Vector3(-3,0,0));
        InputSystem.QueueStateEvent(Mouse.current,new MouseState {position=point}.WithButton(MouseButton.Left));
        yield return new WaitForSecondsRealtime(.08f);
        InputSystem.QueueStateEvent(Mouse.current,new MouseState {position=point});
        yield return new WaitForSecondsRealtime(.15f);
        Require(information.SelectedTile==tile,"PC click did not select tool target");
        Require(!information.TileIsOpen,"PC tool targeting opened the information window");
        Require(session.Target==tile && !session.HasPendingOption && session.Preview==null,
            "A map click should clear an unapplied tier preview but retain the target");
        Require(information.GetComponent<SelectionOutlinePresenter>().HighlightedTileCount==1,
            "PC tool target lost its outline");
        session.End();
        Require(information.TrySelectTileAt(point) && information.TileIsOpen,
            "Normal information viewing did not open the window");
        Require(information.TrySelectTileAt(point) && information.TileIsOpen,
            "An open information window blocked a new map selection");
        var populationWindow = (InformationWindow)new SerializedObject(information)
            .FindProperty("populationWindow").objectReferenceValue;
        populationWindow.Open();
        Require(information.TileIsOpen && information.PopulationIsOpen,
            "Tile and population information could not stay open together");
        session.Begin();
        Require(information.TileIsOpen && information.PopulationIsOpen,
            "Starting the tool session closed an information window");
        information.CloseTile();
        Require(information.TrySelectSceneAt(point, 0f, true) && information.TileIsOpen &&
            information.PopulationIsOpen, "Inspection during tool mode did not open tile information");
        // Tool targeting updates the selected tile without closing either information window.
        information.TrySelectTileAt(point);
        Require(information.TileIsOpen && information.PopulationIsOpen,
            "Tool targeting closed an existing information window");
        session.End(); information.CloseTile(); information.ClosePopulation();
        yield return new WaitForSecondsRealtime(.2f);
        Require(information.GetComponent<SelectionOutlinePresenter>().HighlightedTileCount==0,"Closed tile left an outline");
        Result="PASS: mobile tap on release, input ownership, tile outline, read-only preview, one paid plant deployment, cancel, pan suppression, PC selection and close cleanup.";
        Debug.Log(Result);
        Destroy(gameObject);
    }
    private void OnDestroy()
    {
        if(screen!=null && screen.added)InputSystem.RemoveDevice(screen);
        if(mobile!=null)mobile.Mode=originalMode;
    }
}
#endif
