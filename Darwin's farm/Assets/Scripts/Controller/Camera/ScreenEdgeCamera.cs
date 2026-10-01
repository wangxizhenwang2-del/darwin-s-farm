using UnityEngine;

[RequireComponent(typeof(Camera))]
public class ScreenEdgeCamera : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private Transform[] cameraPoints;

    [SerializeField, Range(0.01f, 0.45f)]
    private float edgeMargin = 0.15f;

    [SerializeField, Min(0.1f)]
    private float moveSpeed = 10f;

    // 新机位至少要近这么多，才允许切换，减少来回跳动。
    [SerializeField, Min(0f)]
    private float switchAdvantage = 1f;

    private Camera viewCamera;
    private Transform currentPoint;

    private Vector3 cameraOffset;
    private Vector3 targetPosition;
    private bool moving;

    private void Start()
    {
        viewCamera = GetComponent<Camera>();

        if (player == null)
        {
            enabled = false;
            return;
        }

        // 记录初始构图：镜头相对玩家的位置偏移。
        cameraOffset = transform.position - player.position;

        currentPoint = FindClosestPoint(transform.position);
    }

    private void LateUpdate()
    {
        if (player == null)
            return;

        if (moving)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPosition,
                moveSpeed * Time.deltaTime);

            if (transform.position == targetPosition)
                moving = false;

            return;
        }

        Vector3 viewport =
            viewCamera.WorldToViewportPoint(player.position);

        if (viewport.z <= 0f)
            return;

        bool nearEdge =
            viewport.x <= edgeMargin ||
            viewport.x >= 1f - edgeMargin ||
            viewport.y <= edgeMargin ||
            viewport.y >= 1f - edgeMargin;

        if (!nearEdge)
            return;

        // 如果保持初始构图，镜头此时理想的位置。
        Vector3 desiredPosition = player.position + cameraOffset;

        Transform nextPoint = FindClosestPoint(desiredPosition);

        if (nextPoint == null || nextPoint == currentPoint)
            return;

        if (currentPoint != null)
        {
            float currentDistance =
                HorizontalDistance(currentPoint.position, desiredPosition);

            float nextDistance =
                HorizontalDistance(nextPoint.position, desiredPosition);

            if (currentDistance - nextDistance <= switchAdvantage)
                return;
        }

        currentPoint = nextPoint;
        targetPosition = nextPoint.position;
        moving = true;
    }

    private Transform FindClosestPoint(Vector3 position)
    {
        if (cameraPoints == null)
            return null;

        Transform closest = null;
        float closestDistance = float.PositiveInfinity;

        foreach (Transform point in cameraPoints)
        {
            if (point == null)
                continue;

            float distance =
                HorizontalDistance(point.position, position);

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = point;
            }
        }

        return closest;
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        // 地图在 XZ 平面，只比较水平距离。
        return Vector2.Distance(
            new Vector2(a.x, a.z),
            new Vector2(b.x, b.z));
    }
}