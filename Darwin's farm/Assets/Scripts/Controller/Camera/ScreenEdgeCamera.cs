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

    private bool buildModeEnabled;

    private bool mapDragging;
    private bool tileDragging;
    private Vector2 previousMousePosition;

    public bool IsMapDragging => mapDragging;

    private readonly List<RaycastResult> uiResults =
        new List<RaycastResult>();

    private void Awake()
    {
        viewCamera = GetComponent<Camera>();
        touchInput = GetComponent<MapTouchInput>();
    }

    private void LateUpdate()
    {
        if (!Application.isFocused)
        {
            mapDragging = false;
            return;
        }

        UpdateMapBrowsing();
    }

    private void UpdateMapBrowsing()
    {
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
        if (!isActiveAndEnabled || tileDragging ||
            !TryGetMapPoint(previousPosition, out Vector3 previous) ||
            !TryGetMapPoint(position, out Vector3 current)) return false;
        Vector3 movement = previous - current;
        movement.y = 0f;
        movement *= buildMoveSpeed / 30f;
        if (movement.sqrMagnitude <= 0.000001f) return false;
        transform.position += movement;
        return true;
    }

    public void ZoomMap(float factor, Vector2 screenPivot)
    {
        bool anchored = TryGetMapPoint(screenPivot, out Vector3 before);
        if (!ZoomMap(factor) || !anchored || !TryGetMapPoint(screenPivot, out Vector3 after)) return;
        Vector3 correction = before - after;
        correction.y = 0f;
        transform.position += correction;
    }

    public bool ZoomMap(float factor)
    {
        if (!isActiveAndEnabled || tileDragging || viewCamera == null || factor <= 0f ||
            float.IsNaN(factor) || float.IsInfinity(factor)) return false;
        if (viewCamera.orthographic)
        {
            float size = Mathf.Clamp(viewCamera.orthographicSize * factor,
                minOrthographicSize, maxOrthographicSize);
            if (!Mathf.Approximately(size, viewCamera.orthographicSize))
            {
                viewCamera.orthographicSize = size;
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
        mapDragging = false;
        tileDragging = false;
    }
}
