using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera), typeof(ScreenEdgeCamera))]
public sealed class MapTouchInput : MonoBehaviour
{
    [Header("Existing scene controllers")]
    [SerializeField] private ScreenEdgeCamera mapCamera;
    [SerializeField] private ClickToMove playerMovement;
    [SerializeField] private GameInformationUI information;
    [SerializeField] private MapBuildModeController buildMode;
    [SerializeField] private MapTilePlacementController placement;

    [Header("Touch thresholds (pixels at 720 screen height)")]
    [SerializeField, Min(1f)] private float dragThresholdPixels = 16f;
    [SerializeField, Min(0f)] private float populationPickPaddingPixels = 12f;
    [SerializeField, Range(0.2f, 1.5f)] private float longPressSeconds = 0.45f;
    [Header("Editor testing only; leave off for normal desktop controls")]
    [SerializeField] private bool simulateTouchWithMouse;

    private readonly MapTouchGesture gesture = new MapTouchGesture();
    private readonly List<MapTouchGesture.Contact> contacts = new List<MapTouchGesture.Contact>(10);
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private Camera viewCamera;
    private PointerEventData uiPointer;
    private EventSystem pointerEventSystem;
    private int suppressMouseThroughFrame = -1;
    private bool applicationPaused, previousBuildMode, previousPlacement;
    private MobileGameUI mobileUI;
#if UNITY_EDITOR
    private bool ownsTouchSimulation;
#endif

    public MapTouchGesture.State GestureState => gesture.CurrentState;
    public bool HasWorldGesture => isActiveAndEnabled && gesture.HasWorldGesture;
    public bool SuppressesMouseInput => isActiveAndEnabled &&
        (contacts.Count > 0 || Time.frameCount <= suppressMouseThroughFrame || MouseSimulationEnabled);
    public MapTouchGesture.Action LastAction { get; private set; }
    public bool IsEditorTouchRequested
    {
        get
        {
#if UNITY_EDITOR
            return simulateTouchWithMouse;
#else
            return false;
#endif
        }
    }
    public event System.Action<MapTouchGesture.Result> GestureRecognized;
    private float ResolutionScale => Mathf.Max(0.5f, Screen.height / 720f);
    private bool MouseSimulationEnabled
    {
        get
        {
#if UNITY_EDITOR
            return simulateTouchWithMouse || (mobileUI != null && mobileUI.IsMobileActive);
#else
            return false;
#endif
        }
    }

    private void Awake()
    {
        viewCamera = GetComponent<Camera>();
        if (mapCamera == null) mapCamera = GetComponent<ScreenEdgeCamera>();
        if (playerMovement == null) playerMovement = FindFirstObjectByType<ClickToMove>();
        if (information == null) information = FindFirstObjectByType<GameInformationUI>();
        if (buildMode == null) buildMode = FindFirstObjectByType<MapBuildModeController>();
        if (placement == null) placement = FindFirstObjectByType<MapTilePlacementController>();
        mobileUI = FindFirstObjectByType<MobileGameUI>();
    }

    private void OnEnable() => EnhancedTouchSupport.Enable();

    private void Update()
    {
        UpdateEditorSimulation();
        LastAction = MapTouchGesture.Action.None;
        if (!Application.isFocused || applicationPaused) { CancelGesture(); return; }
        bool building = buildMode != null && buildMode.IsBuildMode;
        bool persistent = placement != null && placement.IsTouchPlacement;
        bool placing = placement != null && placement.IsDragging && !persistent;
        if (building != previousBuildMode || placing != previousPlacement) gesture.Cancel();
        previousBuildMode = building; previousPlacement = placing;
        contacts.Clear();
        foreach (Touch touch in Touch.activeTouches)
        {
            Vector2 position = touch.screenPosition;
            bool blocked = touch.phase == UnityEngine.InputSystem.TouchPhase.Began &&
                (placing || !viewCamera.pixelRect.Contains(position) || IsPointerOverUI(position));
            long id = ((long)touch.screen.deviceId << 32) | (uint)touch.touchId;
            contacts.Add(new MapTouchGesture.Contact(id, position, touch.phase, blocked));
        }
        if (contacts.Count > 0) suppressMouseThroughFrame = Time.frameCount + 1;
        MapTouchGesture.Result action = gesture.Step(contacts, Time.unscaledTime,
            dragThresholdPixels * ResolutionScale, longPressSeconds);
        LastAction = action.Kind;
        if (action.Kind == MapTouchGesture.Action.None) return;
        switch (action.Kind)
        {
            case MapTouchGesture.Action.Tap:
                if (placing || !viewCamera.pixelRect.Contains(action.Position) || IsPointerOverUI(action.Position)) break;
                if (building)
                { if (persistent) placement.TrySetTouchPosition(action.Position); break; }
                if (information != null && information.ToolModeActive)
                { information.TrySelectTileAt(action.Position); break; }
                if (information != null && information.TrySelectPopulationAt(action.Position,
                    populationPickPaddingPixels * ResolutionScale)) break;
                if (playerMovement != null) playerMovement.TryMoveToScreenPosition(action.Position);
                break;
            case MapTouchGesture.Action.LongPress:
                if (!building && !placing && information != null)
                    information.TrySelectSceneAt(action.Position, populationPickPaddingPixels * ResolutionScale);
                break;
            case MapTouchGesture.Action.Pan:
                if (!placing) mapCamera.PanMap(action.Previous, action.Position);
                break;
            case MapTouchGesture.Action.Pinch:
                if (!placing)
                {
                    mapCamera.PanMap(action.Previous, action.Position);
                    mapCamera.ZoomMap(action.ZoomFactor, action.Position);
                }
                break;
        }
        GestureRecognized?.Invoke(action);
    }

    private bool IsPointerOverUI(Vector2 position)
    {
        if (WhiteboxEcologyDebugView.IsPointerOverTestUi(position)) return true;
        EventSystem events = EventSystem.current;
        if (events == null) return false;
        if (pointerEventSystem != events)
        {
            pointerEventSystem = events;
            uiPointer = new PointerEventData(events);
        }
        uiPointer.position = position;
        uiHits.Clear();
        events.RaycastAll(uiPointer, uiHits);
        foreach (RaycastResult hit in uiHits)
            if (hit.module is UnityEngine.UI.GraphicRaycaster) return true;
        return false;
    }

    public void CancelGesture()
    {
        gesture.Cancel();
        contacts.Clear();
        suppressMouseThroughFrame = Time.frameCount + 1;
        LastAction = MapTouchGesture.Action.None;
    }

    private void OnApplicationFocus(bool focused) { if (!focused) CancelGesture(); }
    private void OnApplicationPause(bool paused)
    { applicationPaused = paused; if (paused) CancelGesture(); }
    private void UpdateEditorSimulation()
    {
#if UNITY_EDITOR
        if (MouseSimulationEnabled && (TouchSimulation.instance == null || !TouchSimulation.instance.enabled))
        { TouchSimulation.Enable(); ownsTouchSimulation = true; gesture.Cancel(); }
        else if (!MouseSimulationEnabled && ownsTouchSimulation)
        { TouchSimulation.Disable(); ownsTouchSimulation = false; CancelGesture(); }
#endif
    }

    private void OnDisable()
    {
        CancelGesture();
        EnhancedTouchSupport.Disable();
#if UNITY_EDITOR
        if (ownsTouchSimulation) { TouchSimulation.Disable(); ownsTouchSimulation = false; }
#endif
    }
}
