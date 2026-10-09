using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;


using UnityEngine.InputSystem;

[RequireComponent(typeof(NavMeshAgent))]
public class ClickToMove : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private Camera viewCamera;
    [SerializeField] private RuntimeMapNavigation mapNavigation;

    [Header("地面检测")]
    [SerializeField] private LayerMask groundLayer;

    [SerializeField, Min(0.01f)]
    private float sampleDistance = 0.5f;

    private NavMeshAgent agent;
    private PopulationPlayerBarrier populationBarrier;
    private ScreenEdgeCamera screenEdgeCamera;
    private NavMeshPath path;
    private Vector3 destination;
    private Vector3[] waypoints;
    private int waypointIndex;
    private float nextReplanAt;
    private bool hasDestination;

    private bool movementInputEnabled = true;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (GetComponent<PopulationPlayerBarrier>() == null)
            gameObject.AddComponent<PopulationPlayerBarrier>();
        populationBarrier = GetComponent<PopulationPlayerBarrier>();
        path = new NavMeshPath();

        if (viewCamera == null)
            viewCamera = Camera.main;

        if (viewCamera != null)
            screenEdgeCamera = viewCamera.GetComponent<ScreenEdgeCamera>();

        agent.autoRepath = true;
    }

    private void Update()
    {
        if (!movementInputEnabled ||
            viewCamera == null ||
            !agent.isActiveAndEnabled ||
            !agent.isOnNavMesh)
        {
            return;
        }

        if (!Application.isFocused ||
            (screenEdgeCamera != null && screenEdgeCamera.IsMapBrowsingInputActive))
            return;

        if (mapNavigation != null && mapNavigation.IsUpdating)
            return;

        UpdateRoute();

        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        Vector2 mousePosition;


        if (Mouse.current == null ||
            !Mouse.current.leftButton.wasPressedThisFrame)
        {
            return;
        }

        mousePosition = Mouse.current.position.ReadValue();
        if (WhiteboxEcologyDebugView.IsPointerOverTestUi(mousePosition)) return;


        Ray ray = viewCamera.ScreenPointToRay(mousePosition);

        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                1000f,
                groundLayer,
                QueryTriggerInteraction.Ignore))
        {
            return;
        }

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };

        if (!NavMesh.SamplePosition(
                hit.point,
                out NavMeshHit navHit,
                sampleDistance,
                filter))
        {
            return;
        }

        destination = navHit.position;
        hasDestination = true;
        nextReplanAt = 0f;
        if (!PlanRoute()) Debug.Log("The destination is currently blocked; waiting for a safe route.", this);
    }

    private void UpdateRoute()
    {
        if (!hasDestination) return;
        if (HorizontalDistance(transform.position, destination) < 0.25f)
        {
            hasDestination = false;
            waypoints = null;
            agent.ResetPath();
            return;
        }
        if (waypoints != null && waypointIndex < waypoints.Length &&
            HorizontalDistance(transform.position, waypoints[waypointIndex]) < 0.3f)
        {
            waypointIndex++;
            if (waypointIndex < waypoints.Length) agent.SetDestination(waypoints[waypointIndex]);
            else waypoints = null;
        }
        if (Time.time < nextReplanAt) return;
        if (populationBarrier.WasBlocked || (!agent.pathPending && (!agent.hasPath ||
            PathCrossesPopulation(agent.path.corners, waypoints != null ? waypoints[waypointIndex] : destination) ||
            RemainingRouteCrossesPopulation())))
            PlanRoute();
        nextReplanAt = Time.time + 0.35f;
    }

    private bool PlanRoute()
    {
        nextReplanAt = Time.time + 0.35f;
        Vector3 start = transform.position;
        if (!agent.CalculatePath(destination, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            agent.ResetPath();
            waypoints = null;
            return false;
        }
        if (!PopulationMovementController.HasActivePopulations || !PathCrossesPopulation(path.corners, destination))
        {
            waypoints = null;
            return agent.SetPath(path);
        }
        if (TryPopulationDetour(start, destination, out Vector3[] detour))
        {
            waypoints = detour;
            waypointIndex = 1;
            if (agent.SetDestination(waypoints[waypointIndex])) return true;
        }
        // An occupied destination or a temporarily sealed passage must not keep
        // driving the agent into another body. Retry as the populations move.
        waypoints = null;
        agent.ResetPath();
        return false;
    }

    private bool PathCrossesPopulation(Vector3[] corners, Vector3 end)
    {
        if (corners == null || corners.Length == 0) return true;
        float radius = populationBarrier.Radius;
        Vector3 previous = transform.position;
        foreach (Vector3 corner in corners)
        {
            if (!PopulationMovementController.AllowsPlayerMove(previous, corner, radius)) return true;
            previous = corner;
        }
        return !PopulationMovementController.AllowsPlayerMove(previous, end, radius);
    }

    private bool RemainingRouteCrossesPopulation()
    {
        if (waypoints == null) return false;
        float radius = populationBarrier.Radius;
        for (int i = waypointIndex + 1; i < waypoints.Length; i++)
            if (!PopulationMovementController.AllowsPlayerMove(waypoints[i - 1], waypoints[i], radius)) return true;
        return false;
    }
    private bool TryPopulationDetour(Vector3 start, Vector3 target, out Vector3[] corners)
    {
        corners = null;
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        float radius = populationBarrier.Radius;
        const float padding = 4f;
        Vector3 min = Vector3.Min(start, target) - new Vector3(padding, 1f, padding);
        Vector3 max = Vector3.Max(start, target) + new Vector3(padding, 1f, padding);
        float verticalSearch = Mathf.Abs(start.y - target.y) + 3f;
        var bounds = new Bounds((min + max) * 0.5f, max - min);
        bool TerrainMove(Vector3 from, Vector3 to)
        {
            if (!NavMesh.SamplePosition(from, out NavMeshHit a, verticalSearch, filter) ||
                !NavMesh.SamplePosition(to, out NavMeshHit b, verticalSearch, filter) ||
                HorizontalDistance(from, a.position) > 0.2f ||
                HorizontalDistance(to, b.position) > 0.2f) return false;
            return !NavMesh.Raycast(a.position, b.position, out _, filter);
        }
        var navigation = new PopulationLocalNavigation(bounds, 0.55f, TerrainMove);
        if (!navigation.TryPath(start, target,
            (a, b) => PopulationMovementController.AllowsPlayerMove(a, b, radius), out Vector3[] raw)) return false;
        for (int i = 1; i < raw.Length; i++)
        {
            if (!NavMesh.SamplePosition(raw[i], out NavMeshHit hit, verticalSearch, filter) ||
                HorizontalDistance(raw[i], hit.position) > 0.2f) return false;
            raw[i] = hit.position;
        }
        raw[raw.Length - 1] = target;
        for (int i = 1; i < raw.Length; i++)
            if (!TerrainMove(raw[i - 1], raw[i]) ||
                !PopulationMovementController.AllowsPlayerMove(raw[i - 1], raw[i], radius)) return false;
        corners = raw;
        return corners.Length > 1;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(PopulationCollisionMath.Flat(a), PopulationCollisionMath.Flat(b));
    // 只控制是否接受新的点击指令。
    public void SetMovementInputEnabled(bool value)
    {
        movementInputEnabled = value;
    }
}