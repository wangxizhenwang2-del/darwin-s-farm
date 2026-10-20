using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


using UnityEngine.InputSystem;

// 先更新相机，再让拖拽预览计算鼠标对应的位置。
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Camera))]
public class ScreenEdgeCamera : MonoBehaviour
{
    [Header("Map panning")]
    [SerializeField, Min(0.1f)]
    private float buildMoveSpeed = 30f;

    [Header("地图缩放")]
    [Tooltip("每次滚轮输入的缩放幅度。")]
    [SerializeField, Range(0.01f, 0.5f)]
    private float zoomStep = 0.1f;

    [SerializeField, Min(0.1f)]
    private float minOrthographicSize = 10f;

    [SerializeField, Min(0.1f)]
    private float maxOrthographicSize = 70f;

    [SerializeField, Range(1f, 179f)]
    private float minFieldOfView = 25f;

    [SerializeField, Range(1f, 179f)]
    private float maxFieldOfView = 75f;

    private Camera viewCamera;
    private MapTouchInput touchInput;
    [Header("Presentation view (Caps Lock)")]
    [SerializeField] private bool enablePresentationView = true;
    [SerializeField] private Vector3 presentationAngles = new Vector3(35f, -38f, 0f);
    [SerializeField, Range(.1f, 2f)] private float presentationTransitionSeconds = .65f;
    private bool presentationView;
    private bool viewTransitionActive;
    private float viewTransitionElapsed;
    private ViewPose transitionFrom, transitionTo;
    private Vector3 transitionFromFocus, transitionToFocus;
    private float transitionFromDistance, transitionToDistance;
    private Vector3 savedPosition;
    private Quaternion savedRotation;
    private bool savedOrthographic;
    private float savedFieldOfView, savedOrthographicSize;
    private float savedNearClip, savedFarClip;
    private MapGridManager depthGrid;
    private float nextDepthScan, minimumMapHeight = -10f, maximumMapHeight = 30f;
    private readonly List<Renderer> depthRenderers = new List<Renderer>();
    public bool IsPresentationView => presentationView;

    private bool buildModeEnabled;

    private bool mapDragging;
    private bool tileDragging;
    private Vector2 previousMousePosition;

    public bool IsMapDragging => mapDragging;

    private readonly List<RaycastResult> uiResults =
        new List<RaycastResult>();

    private void Awake() => ResolveCameraReferences();

    private void OnEnable() => ResolveCameraReferences();

    private void ResolveCameraReferences()
    {
        viewCamera = GetComponent<Camera>();
        touchInput = GetComponent<MapTouchInput>();
    }

    private void LateUpdate()
    {
        AdvanceViewTransition(Time.unscaledDeltaTime);
        if (!viewTransitionActive) MaintainOrthographicDepth();
        if (!Application.isFocused)
        {
            mapDragging = false;
            return;
        }

        UpdatePresentationShortcut();

        UpdateMapBrowsing();
    }

    private void UpdatePresentationShortcut()
    {
        if (Keyboard.current != null && Keyboard.current.capsLockKey.wasPressedThisFrame &&
            enablePresentationView && !IsTextInputFocused())
            TogglePresentationView();
    }

    // Keep the map point at screen centre while adopting the biome showcase angle.
    // UI, picking, billboards and touch controls continue using the same camera.
    public void TogglePresentationView()
    {
        if (viewTransitionActive || !isActiveAndEnabled || tileDragging ||
            (touchInput != null && touchInput.HasWorldGesture)) return;
        if (viewCamera == null) ResolveCameraReferences();
        if (viewCamera == null) return;
        transitionFrom = ViewPose.Capture(viewCamera);
        SetPresentationView();
        transitionTo = ViewPose.Capture(viewCamera);
        transitionFromFocus = ViewFocus(transitionFrom, out transitionFromDistance);
        transitionToFocus = ViewFocus(transitionTo, out transitionToDistance);
        transitionFrom.Apply(viewCamera);
        viewTransitionElapsed = 0f;
        viewTransitionActive = true;
        AdvanceViewTransition(0f);
    }

    private void SetPresentationView()
    {
        if (viewCamera == null) ResolveCameraReferences();
        // Mobile preview suppresses mouse input even when no gesture is active.
        // Keyboard shortcuts only need to wait for an actual world gesture.
        if (!isActiveAndEnabled || viewCamera == null || tileDragging ||
            (touchInput != null && touchInput.HasWorldGesture)) return;
        mapDragging = false;
        if (presentationView)
        {
            transform.SetPositionAndRotation(savedPosition, savedRotation);
            viewCamera.orthographic = savedOrthographic;
            viewCamera.fieldOfView = savedFieldOfView;
            viewCamera.orthographicSize = savedOrthographicSize;
            viewCamera.nearClipPlane = savedNearClip;
            viewCamera.farClipPlane = savedFarClip;
            presentationView = false;
            return;
        }

        savedPosition = transform.position;
        savedRotation = transform.rotation;
        savedOrthographic = viewCamera.orthographic;
        savedFieldOfView = viewCamera.fieldOfView;
        savedOrthographicSize = viewCamera.orthographicSize;
        savedNearClip = viewCamera.nearClipPlane;
        savedFarClip = viewCamera.farClipPlane;
        if (!TryGetMapPoint(viewCamera.pixelRect.center, out Vector3 focus))
            focus = transform.position + transform.forward * Mathf.Max(10f, Mathf.Abs(transform.position.y));
        float distance = Mathf.Max(1f, Vector3.Distance(transform.position, focus));
        float size = savedOrthographic ? savedOrthographicSize :
            distance * Mathf.Tan(savedFieldOfView * .5f * Mathf.Deg2Rad);
        viewCamera.orthographicSize = Mathf.Clamp(size, minOrthographicSize, maxOrthographicSize);
        Quaternion rotation = Quaternion.Euler(presentationAngles);
        transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
        viewCamera.orthographic = true;
        presentationView = true;
        nextDepthScan = 0f;
        MaintainOrthographicDepth();
    }


    private struct ViewPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public bool orthographic;
        public float fieldOfView, size, near, far;
        public static ViewPose Capture(Camera camera) => new ViewPose
        {
            position = camera.transform.position, rotation = camera.transform.rotation,
            orthographic = camera.orthographic, fieldOfView = camera.fieldOfView,
            size = camera.orthographicSize, near = camera.nearClipPlane, far = camera.farClipPlane
        };
        public void Apply(Camera camera)
        {
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.orthographic = orthographic; camera.fieldOfView = fieldOfView;
            camera.orthographicSize = size; camera.nearClipPlane = near; camera.farClipPlane = far;
            camera.ResetProjectionMatrix();
        }
    }

    private static Vector3 ViewFocus(ViewPose pose, out float distance)
    {
        Vector3 forward = pose.rotation * Vector3.forward;
        if (!new Plane(Vector3.up, Vector3.zero).Raycast(new Ray(pose.position, forward), out distance))
            distance = Mathf.Max(10f, Mathf.Abs(pose.position.y));
        distance = Mathf.Max(1f, distance);
        return pose.position + forward * distance;
    }

    private void AdvanceViewTransition(float deltaTime)
    {
        if (!viewTransitionActive || viewCamera == null) return;
        viewTransitionElapsed += Mathf.Max(0f, deltaTime);
        float t = Mathf.Clamp01(viewTransitionElapsed / Mathf.Max(.1f, presentationTransitionSeconds));
        float eased = t * t * t * (t * (t * 6f - 15f) + 10f);
        float distance = Mathf.Lerp(transitionFromDistance, transitionToDistance, eased);
        Quaternion rotation = Quaternion.Slerp(transitionFrom.rotation, transitionTo.rotation, eased);
        Vector3 focus = Vector3.Lerp(transitionFromFocus, transitionToFocus, eased);
        transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
        float fromHeight = transitionFrom.orthographic ? transitionFrom.size :
            transitionFromDistance * Mathf.Tan(transitionFrom.fieldOfView * .5f * Mathf.Deg2Rad);
        float toHeight = transitionTo.orthographic ? transitionTo.size :
            transitionToDistance * Mathf.Tan(transitionTo.fieldOfView * .5f * Mathf.Deg2Rad);
        float height = Mathf.Lerp(fromHeight, toHeight, eased);
        float blend = Mathf.Lerp(transitionFrom.orthographic ? 1f : 0f, transitionTo.orthographic ? 1f : 0f, eased);
        viewCamera.nearClipPlane = Mathf.Min(transitionFrom.near, transitionTo.near);
        viewCamera.farClipPlane = Mathf.Max(transitionFrom.far, transitionTo.far);
        float fov = Mathf.Atan(height / distance) * 2f * Mathf.Rad2Deg;
        Matrix4x4 perspective = Matrix4x4.Perspective(fov, viewCamera.aspect, viewCamera.nearClipPlane, viewCamera.farClipPlane);
        Matrix4x4 ortho = Matrix4x4.Ortho(-height * viewCamera.aspect, height * viewCamera.aspect,
            -height, height, viewCamera.nearClipPlane, viewCamera.farClipPlane);
        Matrix4x4 projection = new Matrix4x4();
        // Normalize homogeneous scale at the focus plane before blending projections.
        // This avoids the zoom surge caused by directly lerping ordinary matrices.
        for (int i = 0; i < 16; i++) projection[i] = Mathf.Lerp(perspective[i] / distance, ortho[i], blend);
        viewCamera.projectionMatrix = projection;
        if (t >= 1f)
        {
            transitionTo.Apply(viewCamera);
            viewTransitionActive = false;
        }
    }
    // In orthographic projection, backing away changes depth only, not framing.
    // Keep the entire visible map-height slab in front of the near plane.
    private void MaintainOrthographicDepth()
    {
        if (viewCamera == null) ResolveCameraReferences();
        if (viewCamera == null || !viewCamera.orthographic) return;
        if (Time.unscaledTime >= nextDepthScan)
        {
            nextDepthScan = Time.unscaledTime + 1f;
            if (depthGrid == null) depthGrid = FindFirstObjectByType<MapGridManager>();
            float observedMaximum = 0f;
            bool hasMapGeometry = false;
            if (depthGrid != null)
                foreach (var tile in depthGrid.GetPlacedTiles())
                {
                    if (tile == null) continue;
                    tile.GetComponentsInChildren<Renderer>(false, depthRenderers);
                    foreach (var display in depthRenderers)
                    {
                        if (!display.enabled || display is ParticleSystemRenderer || display is TrailRenderer) continue;
                        minimumMapHeight = Mathf.Min(minimumMapHeight, display.bounds.min.y - 2f);
                        observedMaximum = Mathf.Max(observedMaximum, display.bounds.max.y + 2f);
                        hasMapGeometry = true;
                    }
                }
            if (hasMapGeometry) maximumMapHeight = observedMaximum;
        }
        Vector3 forward = transform.forward;
        float downward = -forward.y;
        if (downward < .05f) return;
        float verticalExtent = (Mathf.Abs(transform.up.y) + Mathf.Abs(transform.right.y) * viewCamera.aspect) *
            viewCamera.orthographicSize;
        viewCamera.nearClipPlane = Mathf.Min(viewCamera.nearClipPlane, .1f);
        float nearestDepth = (transform.position.y - verticalExtent - maximumMapHeight) / downward;
        float retreat = viewCamera.nearClipPlane + 2f - nearestDepth;
        if (retreat > 0f) transform.position -= forward * retreat;
        float farthestDepth = (transform.position.y + verticalExtent - minimumMapHeight) / downward;
        viewCamera.farClipPlane = Mathf.Max(viewCamera.farClipPlane, farthestDepth + 4f);
    }
    private void UpdateMapBrowsing()
    {
        if (viewTransitionActive) return;
        Mouse mouse = Mouse.current;
        if ((touchInput != null && touchInput.SuppressesMouseInput) ||
            mouse == null || tileDragging)
        {
            mapDragging = false;
            return;
        }

        Vector2 position = mouse.position.ReadValue();
        if (mapDragging && !mouse.middleButton.isPressed) mapDragging = false;
        if (mapDragging)
        {
            PanMap(previousMousePosition, position);
            previousMousePosition = position;
        }
        else if (mouse.middleButton.wasPressedThisFrame && IsOverMap(position))
        {
            mapDragging = true;
            previousMousePosition = position;
        }

        if (!IsOverMap(position))
            return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f))
            return;

        float factor = Mathf.Exp(-Mathf.Sign(scroll) * zoomStep);
        ZoomMap(factor);
    }

    // Both input devices use the same map plane, speed and zoom limits.
    public bool PanMap(Vector2 previousPosition, Vector2 position)
    {
        if (viewTransitionActive || !isActiveAndEnabled || tileDragging ||
            !TryGetMapPoint(previousPosition, out Vector3 previous) ||
            !TryGetMapPoint(position, out Vector3 current)) return false;
        Vector3 movement = previous - current;
        movement.y = 0f;
        movement *= buildMoveSpeed / 30f;
        if (movement.sqrMagnitude <= 0.000001f) return false;
        transform.position += movement;
        MaintainOrthographicDepth();
        return true;
    }

    public void ZoomMap(float factor, Vector2 screenPivot)
    {
        bool anchored = TryGetMapPoint(screenPivot, out Vector3 before);
        if (!ZoomMap(factor) || !anchored || !TryGetMapPoint(screenPivot, out Vector3 after)) return;
        Vector3 correction = before - after;
        correction.y = 0f;
        transform.position += correction;
        MaintainOrthographicDepth();
    }

    public bool ZoomMap(float factor)
    {
        if (viewTransitionActive || !isActiveAndEnabled || tileDragging || viewCamera == null || factor <= 0f ||
            float.IsNaN(factor) || float.IsInfinity(factor)) return false;
        if (viewCamera.orthographic)
        {
            float size = Mathf.Clamp(viewCamera.orthographicSize * factor,
                minOrthographicSize, maxOrthographicSize);
            if (!Mathf.Approximately(size, viewCamera.orthographicSize))
            {
                viewCamera.orthographicSize = size;
                MaintainOrthographicDepth();
                return true;
            }
        }
        else
        {
            float fieldOfView = Mathf.Clamp(viewCamera.fieldOfView * factor,
                minFieldOfView, maxFieldOfView);
            if (!Mathf.Approximately(fieldOfView, viewCamera.fieldOfView))
            {
                viewCamera.fieldOfView = fieldOfView;
                return true;
            }
        }
        return false;
    }

    private bool TryGetMapPoint(Vector2 position, out Vector3 point)
    {
        Plane mapPlane = new Plane(Vector3.up, Vector3.zero);
        Ray ray = viewCamera.ScreenPointToRay(position);
        if (mapPlane.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }

        point = Vector3.zero;
        return false;
    }

    private bool IsOverMap(Vector2 position)
    {
        return viewCamera.pixelRect.Contains(position) &&
               !IsPointerOverUI(position);
    }

    private bool IsPointerOverUI(Vector2 mousePosition)
    {
        if (EconomyOperationsView.IsPointerOverUi(mousePosition)) return true;
        if (EventSystem.current == null)
            return false;

        PointerEventData pointer =
            new PointerEventData(EventSystem.current);

        pointer.position = mousePosition;

        uiResults.Clear();
        EventSystem.current.RaycastAll(pointer, uiResults);

        foreach (RaycastResult result in uiResults)
        {
            if (result.module is GraphicRaycaster)
                return true;
        }

        return false;
    }

    public static bool IsTextInputFocused()
    {
        GameObject selected = EventSystem.current != null
            ? EventSystem.current.currentSelectedGameObject : null;
        return selected != null &&
               (selected.GetComponent<InputField>() != null ||
                selected.GetComponent("TMP_InputField") != null);
    }

    public void SetBuildMode(bool value)
    {
        if (buildModeEnabled == value)
            return;

        buildModeEnabled = value;
        mapDragging = false;
    }

    public void SetTileDragActive(bool value)
    {
        tileDragging = value;
        if (value)
        {
            mapDragging = false;
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            mapDragging = false;
    }

    private void OnDisable()
    {
        if (viewTransitionActive && viewCamera != null) transitionTo.Apply(viewCamera);
        viewTransitionActive = false;
        mapDragging = false;
        tileDragging = false;
    }
}
