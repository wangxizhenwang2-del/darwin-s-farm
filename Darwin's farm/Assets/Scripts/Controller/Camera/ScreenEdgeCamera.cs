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

    [Header("建造模式平移")]
    [SerializeField, Min(0.1f)]
    private float buildMoveSpeed = 30f;

    [Header("建造模式缩放")]
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

    private float savedOrthographicSize;
    private float savedFieldOfView;

    private readonly List<RaycastResult> uiResults =
        new List<RaycastResult>();

    private void Awake()
    {
        viewCamera = GetComponent<Camera>();
    }

    private void LateUpdate()
    {
        if (!Application.isFocused)
            return;

        if (buildModeEnabled)
        {
            UpdateBuildCamera();
            return;
        }

        UpdatePlayerFollow();
    }

    private void UpdateBuildCamera()
    {
        Vector2 movement = Vector2.zero;
        Vector2 mousePosition = Vector2.zero;

        float scroll = 0f;
        bool hasMouse = false;


        Keyboard keyboard = Keyboard.current;

        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed ||
                keyboard.upArrowKey.isPressed)
            {
                movement.y += 1f;
            }

            if (keyboard.sKey.isPressed ||
                keyboard.downArrowKey.isPressed)
            {
                movement.y -= 1f;
            }

            if (keyboard.dKey.isPressed ||
                keyboard.rightArrowKey.isPressed)
            {
                movement.x += 1f;
            }

            if (keyboard.aKey.isPressed ||
                keyboard.leftArrowKey.isPressed)
            {
                movement.x -= 1f;
            }
        }

        if (Mouse.current != null)
        {
            hasMouse = true;
            mousePosition = Mouse.current.position.ReadValue();
            scroll = Mouse.current.scroll.ReadValue().y;
        }


        movement = Vector2.ClampMagnitude(movement, 1f);

        // 相机的右方向投影到地图水平面,用于找到相机朝向地面的右方向
        Vector3 right =
            Vector3.ProjectOnPlane(transform.right, Vector3.up);

        if (right.sqrMagnitude < 0.001f)
            right = Vector3.right;

        right.Normalize();

        // 与右方向垂直的地图前方向。
        Vector3 forward = Vector3.Cross(right, Vector3.up);

        Vector3 moveDirection =
            right * movement.x + forward * movement.y;

        transform.position +=
            moveDirection * buildMoveSpeed * Time.deltaTime;

        if (!hasMouse ||
            !viewCamera.pixelRect.Contains(mousePosition) ||
            IsPointerOverUI(mousePosition) ||
            Mathf.Approximately(scroll, 0f))
        {
            return;
        }

        // 按输入方向缩放，避免不同输入系统的滚轮数值差异。
        float factor = Mathf.Exp(-Mathf.Sign(scroll) * zoomStep);

        if (viewCamera.orthographic)
        {
            viewCamera.orthographicSize = Mathf.Clamp(
                viewCamera.orthographicSize * factor,
                minOrthographicSize,
                maxOrthographicSize);
        }
        else
        {
            viewCamera.fieldOfView = Mathf.Clamp(
                viewCamera.fieldOfView * factor,
                minFieldOfView,
                maxFieldOfView);
        }
    }

    private bool IsPointerOverUI(Vector2 mousePosition)
    {
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

    private void UpdatePlayerFollow()
    {
        if (!followEnabled || player == null)
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

        if (value)
        {
            returningToPlayer = false;
            savedOrthographicSize = viewCamera.orthographicSize;
            savedFieldOfView = viewCamera.fieldOfView;

            buildModeEnabled = true;
            followEnabled = false;
            moving = false;
        }
        else
        {
            buildModeEnabled = false;

            viewCamera.orthographicSize = savedOrthographicSize;
            viewCamera.fieldOfView = savedFieldOfView;

            followEnabled = true;

            // 关闭建造模式后，主动跟进到玩家附近。
            moving = player != null;
            returningToPlayer = player != null;
        }
    }

    // 保留原接口，供其他系统单独控制跟随。
    public void SetFollowEnabled(bool value)
    {
        returningToPlayer = false;
        followEnabled = value;
        moving = false;
    }
}