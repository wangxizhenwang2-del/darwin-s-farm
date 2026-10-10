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
    [Header("跟随目标")]
    [SerializeField] private Transform player;

    [Header("玩家跟随")]
    [SerializeField, Range(0.01f, 0.45f)]
    private float edgeMargin = 0.25f;

    [Tooltip("玩家跟随时，相机每秒移动的距离。")]
    [SerializeField, Min(0.1f)]
    private float moveSpeed = 25f;

    [SerializeField, Min(0.01f)]
    private float stopDistance = 0.15f;

    [Header("地图平移")]
    [SerializeField, Min(0.1f)]
    private float buildMoveSpeed = 30f;

    [SerializeField, Min(1f)] private float altLeftDragThresholdPixels = 8f;

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

    [Header("退出建造模式返回")]
    [SerializeField, Min(0.1f)]
    private float returnResponsiveness = 6f;

    private bool returningToPlayer;

    private Camera viewCamera;

    private bool moving;
    private bool followEnabled = true;
    private bool buildModeEnabled;

    private bool mapDragging;
    private bool draggingWithLeft;
    private bool pendingAltLeftDrag;
    private Vector2 altLeftPressPosition;
    private bool tileDragging;
    private Vector2 previousMousePosition;

    public bool IsMapDragging => mapDragging;

    // ClickToMove runs in Update, before the camera's LateUpdate.
    public bool IsMapBrowsingInputActive
    {
        get
        {
            Mouse mouse = Mouse.current;
            if (!Application.isFocused || tileDragging || mouse == null ||
                !CanBrowseMap())
                return false;

            if (mapDragging && mouse.middleButton.isPressed)
                return true;

            Vector2 position = mouse.position.ReadValue();
            Keyboard keyboard = Keyboard.current;
            bool alt = keyboard != null &&
                (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
            return IsOverMap(position) &&
                   (mouse.middleButton.isPressed ||
                    (alt && mouse.leftButton.isPressed && !buildModeEnabled) ||
                    !Mathf.Approximately(mouse.scroll.ReadValue().y, 0f));
        }
    }
    private readonly List<RaycastResult> uiResults =
        new List<RaycastResult>();

    private void Awake()
    {
        viewCamera = GetComponent<Camera>();
    }

    private void LateUpdate()
    {
        if (!Application.isFocused)
        {
            mapDragging = false;
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.tabKey.wasPressedThisFrame &&
            !IsTextInputFocused())
            ReturnToPlayer();

        UpdateMapBrowsing();

        if (returningToPlayer || (!buildModeEnabled && followEnabled))
            UpdatePlayerFollow();
    }

    private bool CanBrowseMap()
    {
        if (buildModeEnabled)
            return true;

        Keyboard keyboard = Keyboard.current;
        return keyboard != null &&
               (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
    }

    private void UpdateMapBrowsing()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !CanBrowseMap() || tileDragging)
        {
            mapDragging = false;
            draggingWithLeft = false;
            pendingAltLeftDrag = false;
            return;
        }

        Vector2 position = mouse.position.ReadValue();
        if (pendingAltLeftDrag && !mouse.leftButton.isPressed)
            pendingAltLeftDrag = false;
        if (mapDragging && (draggingWithLeft
            ? !mouse.leftButton.isPressed : !mouse.middleButton.isPressed))
        {
            mapDragging = false;
            draggingWithLeft = false;
        }
        // Alt+left starts panning only beyond the click threshold. Starting
        // from the current position avoids a jump when the threshold is crossed.
        if (!buildModeEnabled && mouse.leftButton.wasPressedThisFrame && IsOverMap(position))
        {
            pendingAltLeftDrag = true;
            altLeftPressPosition = position;
        }
        if (pendingAltLeftDrag && mouse.leftButton.isPressed && !mapDragging &&
            (position - altLeftPressPosition).sqrMagnitude >
            altLeftDragThresholdPixels * altLeftDragThresholdPixels)
        {
            pendingAltLeftDrag = false;
            mapDragging = true;
            draggingWithLeft = true;
            previousMousePosition = position;
        }

        if (mapDragging)
        {
            if (TryGetMapPoint(previousMousePosition, out Vector3 previous) &&
                TryGetMapPoint(position, out Vector3 current))
            {
                Vector3 movement = previous - current;
                movement.y = 0f;
                movement *= buildMoveSpeed / 30f;
                if (movement.sqrMagnitude > 0.000001f)
                {
                    transform.position += movement;
                    PauseFollow();
                }
            }
            previousMousePosition = position;
        }
        else if (mouse.middleButton.wasPressedThisFrame && IsOverMap(position))
        {
            mapDragging = true;
            draggingWithLeft = false;
            previousMousePosition = position;
        }

        if (!IsOverMap(position))
            return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f))
            return;

        float factor = Mathf.Exp(-Mathf.Sign(scroll) * zoomStep);
        if (viewCamera.orthographic)
        {
            float size = Mathf.Clamp(viewCamera.orthographicSize * factor,
                minOrthographicSize, maxOrthographicSize);
            if (!Mathf.Approximately(size, viewCamera.orthographicSize))
            {
                viewCamera.orthographicSize = size;
                PauseFollow();
            }
        }
        else
        {
            float fieldOfView = Mathf.Clamp(viewCamera.fieldOfView * factor,
                minFieldOfView, maxFieldOfView);
            if (!Mathf.Approximately(fieldOfView, viewCamera.fieldOfView))
            {
                viewCamera.fieldOfView = fieldOfView;
                PauseFollow();
            }
        }
    }

    private bool TryGetMapPoint(Vector2 position, out Vector3 point)
    {
        float height = player != null ? player.position.y : 0f;
        Plane mapPlane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
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
        if (WhiteboxEcologyDebugView.IsPointerOverTestUi(mousePosition)) return true;
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

    private void UpdatePlayerFollow()
    {
        if ((!followEnabled && !returningToPlayer) || player == null)
            return;

        Vector3 viewport =
            viewCamera.WorldToViewportPoint(player.position);

        // 主动回到玩家时，即使玩家在画面外也继续移动。
        if (viewport.z <= 0f && !moving)
            return;

        bool nearEdge =
            viewport.x <= edgeMargin ||
            viewport.x >= 1f - edgeMargin ||
            viewport.y <= edgeMargin ||
            viewport.y >= 1f - edgeMargin;

        if (viewport.z > 0f && nearEdge)
            moving = true;

        if (!moving)
            return;

        Plane playerPlane =
            new Plane(Vector3.up, player.position);

        Ray centerRay = viewCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f));

        if (!playerPlane.Raycast(centerRay, out float distance))
        {
            moving = false;
            return;
        }

        Vector3 centerOnPlane = centerRay.GetPoint(distance);

        Vector3 correction = player.position - centerOnPlane;
        correction.y = 0f;

        if (correction.sqrMagnitude <= stopDistance * stopDistance)
        {
            // 返回结束时补齐最后一点距离。
            if (returningToPlayer)
                transform.position += correction;

            moving = false;
            returningToPlayer = false;
            return;
        }

        if (returningToPlayer)
        {
            // 距离远时快速返回，接近目标时逐渐减速。
            float blend =
                1f - Mathf.Exp(-returnResponsiveness * Time.deltaTime);

            transform.position += correction * blend;
        }
        else
        {
            Vector3 targetPosition = transform.position + correction;

            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPosition,
                moveSpeed * Time.deltaTime);
        }
    }

    public void SetBuildMode(bool value)
    {
        if (buildModeEnabled == value)
            return;

        buildModeEnabled = value;
        mapDragging = false;
        draggingWithLeft = false;
        pendingAltLeftDrag = false;
        PauseFollow();
    }

    public void SetTileDragActive(bool value)
    {
        tileDragging = value;
        if (value)
        {
            mapDragging = false;
            draggingWithLeft = false;
            pendingAltLeftDrag = false;
        }
    }

    private void PauseFollow()
    {
        followEnabled = false;
        moving = false;
        returningToPlayer = false;
    }

    private void ReturnToPlayer()
    {
        if (player == null)
            return;

        returningToPlayer = true;
        moving = true;
        followEnabled = !buildModeEnabled;
    }

    // 保留原接口，供其他系统控制自动跟随。
    public void SetFollowEnabled(bool value)
    {
        returningToPlayer = false;
        followEnabled = value;
        moving = false;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            mapDragging = false;
    }

    private void OnDisable()
    {
        mapDragging = false;
        draggingWithLeft = false;
        pendingAltLeftDrag = false;
        tileDragging = false;
        returningToPlayer = false;
    }
}
