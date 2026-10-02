using UnityEngine;

[RequireComponent(typeof(Camera))]
public class ScreenEdgeCamera : MonoBehaviour
{
    [Header("跟随目标")]
    [SerializeField] private Transform player;

    /// <summary>
    /// 表示玩家进入多少范围开始触发，数值越大的话，触发越及时
    /// </summary>
    [Header("触发区域")]
    
    [SerializeField, Range(0.01f, 0.45f)]
    private float edgeMargin = 0.25f;

    [Header("移动")]
    [Tooltip("表示鼠标的1移动速度")]
    [SerializeField, Min(0.1f)]
    private float moveSpeed = 25f;

    [Tooltip("玩家距画面中心足够近时停止移动")]
    [SerializeField, Min(0.01f)]
    private float stopDistance = 0.15f;

    private Camera viewCamera;
    private bool moving;
    private bool followEnabled = true;

    private void Awake()
    {
        viewCamera = GetComponent<Camera>();
    }

    private void LateUpdate()
    {
        if (!followEnabled || player == null)
            return;

        Vector3 viewport =
            viewCamera.WorldToViewportPoint(player.position);

        if (viewport.z <= 0f)
            return;

        bool nearEdge =
            viewport.x <= edgeMargin ||
            viewport.x >= 1f - edgeMargin ||
            viewport.y <= edgeMargin ||
            viewport.y >= 1f - edgeMargin;

        if (nearEdge)
            moving = true;

        if (!moving)
            return;

        // 使用玩家当前高度，建立一个水平面。
        Plane playerPlane = new Plane(Vector3.up, player.position);

        // 从画面中心发射射线。
        Ray centerRay = viewCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f));

        if (!playerPlane.Raycast(centerRay, out float distance))
        {
            moving = false;
            return;
        }

        Vector3 centerOnPlane = centerRay.GetPoint(distance);

        // 把玩家移到画面中心所需的相机水平位移。
        Vector3 correction = player.position - centerOnPlane;
        correction.y = 0f;

        if (correction.sqrMagnitude <= stopDistance * stopDistance)
        {
            moving = false;
            return;
        }

        Vector3 targetPosition = transform.position + correction;

        transform.position = Vector3.MoveTowards(
            transform.position,
            targetPosition,
            moveSpeed * Time.deltaTime);
    }

    // 后续建造模式会调用这个方法。
    public void SetFollowEnabled(bool value)
    {
        followEnabled = value;
        moving = false;
    }
}