using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Attached by ClickToMove at runtime. NavMesh still plans and drives the player;
// only its proposed position is constrained before populations advance/render.
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class PopulationPlayerBarrier : MonoBehaviour
{
    private static readonly List<PopulationPlayerBarrier> players = new List<PopulationPlayerBarrier>();
    internal static IReadOnlyList<PopulationPlayerBarrier> Players => players;
    private NavMeshAgent agent;
    private Collider[] colliders;
    private Vector3 lastPosition;
    private bool controlling;
    private bool previousUpdatePosition;
    public float Radius
    {
        get
        {
            float radius = agent != null ? agent.radius : 0.5f;
            foreach (Collider collider in colliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                // Full box diagonal is independent of its current turning angle.
                Vector3 scale = collider.transform.lossyScale;
                float largestScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                float extent = collider is BoxCollider box ? box.size.magnitude * largestScale * 0.5f :
                    collider.bounds.extents.magnitude;
                Vector3 center = collider is BoxCollider centeredBox ?
                    centeredBox.transform.TransformPoint(centeredBox.center) : collider.bounds.center;
                Vector3 offset = center - transform.position; offset.y = 0f;
                radius = Mathf.Max(radius, extent + offset.magnitude);
            }
            return radius + 0.03f;
        }
    }

    private void Awake() { agent = GetComponent<NavMeshAgent>(); colliders = GetComponentsInChildren<Collider>(); }
    private void OnEnable() { if (!players.Contains(this)) players.Add(this); }
    private void OnDisable() { ReleaseControl(); players.Remove(this); }
    private void OnDestroy() { ReleaseControl(); players.Remove(this); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPlayers() => players.Clear();

    internal static void ActivateAll()
    {
        foreach (PopulationPlayerBarrier player in players)
            if (player != null && player.isActiveAndEnabled) player.BeginControl();
    }

    private void BeginControl()
    {
        if (controlling || agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        previousUpdatePosition = agent.updatePosition;
        lastPosition = transform.position;
        agent.updatePosition = false;
        controlling = true;
    }

    private void ReleaseControl()
    {
        if (!controlling) return;
        if (agent != null) agent.updatePosition = previousUpdatePosition;
        controlling = false;
    }

    private void Update()
    {
        if (!PopulationMovementController.HasActivePopulations) { ReleaseControl(); return; }
        BeginControl();
        if (!controlling || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        Vector3 desired = agent.nextPosition;
        // Inspector/script teleports also pass through the same swept constraint.
        if ((transform.position - lastPosition).sqrMagnitude > 0.000001f) desired = transform.position;
        float radius = Radius;
        Vector3 resolved = desired;
        if (!PopulationMovementController.AllowsPlayerMove(lastPosition, desired, radius))
        {
            float lower = 0f, upper = 1f;
            for (int i = 0; i < 16; i++)
            {
                float middle = (lower + upper) * 0.5f;
                Vector3 candidate = Vector3.Lerp(lastPosition, desired, middle);
                if (PopulationMovementController.AllowsPlayerMove(lastPosition, candidate, radius)) lower = middle;
                else upper = middle;
            }
            resolved = Vector3.Lerp(lastPosition, desired, lower);
            if (!PopulationMovementController.AllowsPlayerMove(lastPosition, lastPosition, radius) &&
                !PopulationMovementController.AllowsPlayerMove(lastPosition, resolved, radius))
                resolved = TryRecover(radius);
            agent.velocity = Vector3.zero;
        }
        // Preserve destination and path: Warp/ResetPath would lose the click order.
        agent.nextPosition = resolved;
        transform.position = resolved;
        lastPosition = resolved;
    }

    private Vector3 TryRecover(float radius)
    {
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        for (int ring = 1; ring <= 6; ring++)
            for (int direction = 0; direction < 16; direction++)
            {
                float angle = direction * Mathf.PI * 0.125f;
                Vector3 candidate = lastPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (ring * 0.4f);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 0.2f, filter) ||
                    NavMesh.Raycast(lastPosition, hit.position, out _, filter) ||
                    !PopulationMovementController.AllowsPlayerMove(lastPosition, hit.position, radius) ||
                    !PopulationMovementController.AllowsPlayerMove(hit.position, hit.position, radius)) continue;
                return hit.position + Vector3.up * agent.baseOffset;
            }
        return lastPosition;
    }
}
