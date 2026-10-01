using UnityEngine;
using UnityEngine.AI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(NavMeshAgent))]
public class ClickToMove : MonoBehaviour
{
    [SerializeField] private Camera viewCamera;
    [SerializeField] private LayerMask groundLayer;

    private NavMeshAgent agent;
    private NavMeshPath path;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        path = new NavMeshPath();

        if (viewCamera == null)
            viewCamera = Camera.main;
    }

    private void Update()
    {
        if (viewCamera == null || !agent.isOnNavMesh)
            return;

        Vector2 mousePosition;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null ||
            !Mouse.current.leftButton.wasPressedThisFrame)
            return;

        mousePosition = Mouse.current.position.ReadValue();
#else
        if (!Input.GetMouseButtonDown(0))
            return;

        mousePosition = Input.mousePosition;
#endif

        Ray ray = viewCamera.ScreenPointToRay(mousePosition);

        if (!Physics.Raycast(
                ray, out RaycastHit hit, 1000f,
                groundLayer, QueryTriggerInteraction.Ignore))
            return;

        // 在点击位置附近寻找可行走点。
        if (!NavMesh.SamplePosition(
                hit.point, out NavMeshHit navHit,
                0.5f, agent.areaMask))
            return;

        // 只有能完整到达的位置才接受移动指令。
        if (agent.CalculatePath(navHit.position, path) &&
            path.status == NavMeshPathStatus.PathComplete)
        {
            agent.SetPath(path);
        }
    }
}