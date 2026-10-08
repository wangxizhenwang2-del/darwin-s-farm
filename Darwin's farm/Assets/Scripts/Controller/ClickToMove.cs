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
    private NavMeshPath path;

    private bool movementInputEnabled = true;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (GetComponent<PopulationPlayerBarrier>() == null)
            gameObject.AddComponent<PopulationPlayerBarrier>();
        path = new NavMeshPath();

        if (viewCamera == null)
            viewCamera = Camera.main;

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

        if (mapNavigation != null && mapNavigation.IsUpdating)
            return;

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

        if (agent.CalculatePath(navHit.position, path) &&
            path.status == NavMeshPathStatus.PathComplete)
        {
            agent.SetPath(path);
        }
        else
        {
            Debug.Log("这个位置目前无法到达。", this);
        }
    }

    // 只控制是否接受新的点击指令。
    public void SetMovementInputEnabled(bool value)
    {
        movementInputEnabled = value;
    }
}