using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

// Visual-only playback of ecological results. No code here writes PopulationData.
public sealed partial class PopulationVisualPresenter
{
    [Header("Ecological transition visuals")]
    [SerializeField, Min(0.1f)] private float transitionSpeed = 5f;
    [SerializeField, Min(0.1f)] private float minimumTransitionSeconds = 0.8f;
    [SerializeField, Min(0.1f)] private float maximumTransitionSeconds = 4.5f;
    [SerializeField, Min(0f)] private float fallbackArcHeight = 2.5f;

    private sealed class TransitionHold
    {
        public int CountBefore;
        public int Active;
        public WhiteboxPopulation StagedView;
    }

    private sealed class RunningTransition
    {
        public PopulationTransitionResult Result;
        public PopulationTransitionVisual View;
        public bool Held;
    }

    private sealed class PendingTransition
    {
        public PopulationTransitionResult Result;
        public Vector3 Start;
        public Color SourceColor;
        public Material Material;
        public bool Held;
    }

    private readonly Dictionary<string, TransitionHold> transitionHolds =
        new Dictionary<string, TransitionHold>(StringComparer.Ordinal);
    private readonly List<RunningTransition> activeTransitions = new List<RunningTransition>();
    private readonly List<PendingTransition> pendingTransitions = new List<PendingTransition>();
    public int ActiveTransitionCount => activeTransitions.Count + pendingTransitions.Count;

    private void SubscribeTransitions()
    {
        if (simulation == null) return;
        simulation.OnMigrationResolved += OnTransitionResolved;
        simulation.OnNicheConversionResolved += OnTransitionResolved;
        simulation.OnPresetEvolutionResolved += OnTransitionResolved;
    }

    private void UnsubscribeTransitions()
    {
        if (simulation == null) return;
        simulation.OnMigrationResolved -= OnTransitionResolved;
        simulation.OnNicheConversionResolved -= OnTransitionResolved;
        simulation.OnPresetEvolutionResolved -= OnTransitionResolved;
    }

    private void OnTransitionResolved(PopulationTransitionResult result)
    {
        if (!Application.isPlaying || grid == null || movement == null ||
            !TryFindTile(result.SourceBlock, out MapTileInstance sourceTile) ||
            !TryFindTile(result.TargetBlock, out MapTileInstance targetTile)) return;

        PopulationData targetData = FindPopulation(result.TargetBlock,
            result.ResultPopulationId);
        if (targetData == null) return;

        WhiteboxPopulation sourceView = null;
        if (visuals.TryGetValue(result.SourcePopulationId, out WhiteboxPopulation candidate) &&
            candidate != null && candidate.TileId == sourceTile.Coordinate)
            sourceView = candidate;
        Vector3 start = sourceView != null && sourceView.Leader != null ?
            sourceView.Leader.position : TileAnchor(sourceTile);
        PopulationData sourceData = sourceView != null ? sourceView.EcologicalPopulation :
            FindPopulation(result.SourceBlock, result.SourcePopulationId);
        Color targetColor = ColorFor(targetData);
        Color sourceColor = sourceData != null ? ColorFor(sourceData) : targetColor;
        Material material = sourceView != null && sourceView.Leader != null ?
            sourceView.Leader.GetComponent<Renderer>().sharedMaterial :
            targetTile.Core != null ? targetTile.Core.sharedMaterial : null;

        // Capture every departure before a model refresh can remove a whole
        // source population. Natural migration may publish several same-day events.
        pendingTransitions.Add(new PendingTransition
        {
            Result = result,
            Start = start,
            SourceColor = sourceColor,
            Material = material
        });
    }

    private void StartPendingTransitions()
    {
        if (pendingTransitions.Count == 0) return;
        var ready = new List<PendingTransition>(pendingTransitions.Count);
        foreach (PendingTransition pending in pendingTransitions)
        {
            if (!TryFindTile(pending.Result.TargetBlock, out _) ||
                FindPopulation(pending.Result.TargetBlock,
                    pending.Result.ResultPopulationId) == null) continue;
            PopulationData targetData = FindPopulation(pending.Result.TargetBlock,
                pending.Result.ResultPopulationId);
            if (targetData.ecologicalNiche == EcologicalNiche.Land)
            {
                if (!transitionHolds.TryGetValue(pending.Result.ResultPopulationId,
                        out TransitionHold hold))
                {
                    hold = new TransitionHold
                    {
                        CountBefore = pending.Result.TargetCountBefore
                    };
                    transitionHolds.Add(pending.Result.ResultPopulationId, hold);
                }
                hold.Active++;
                pending.Held = true;
            }
            ready.Add(pending);
        }
        pendingTransitions.Clear();
        if (ready.Count == 0) { SyncNow(); return; }
        // All holds are in place before one model-to-view reconciliation.
        SyncNow();

        foreach (PendingTransition pending in ready)
        {
            PopulationTransitionResult result = pending.Result;
            if (!TryFindTile(result.TargetBlock, out MapTileInstance targetTile))
            {
                ReleaseUnstartedHold(pending);
                continue;
            }
            PopulationData targetData = FindPopulation(result.TargetBlock,
                result.ResultPopulationId);
            if (targetData == null)
            {
                ReleaseUnstartedHold(pending);
                continue;
            }
            Color targetColor = ColorFor(targetData);
            Vector3 start = pending.Start;
            Vector3 end = TileAnchor(targetTile);
            if (visuals.TryGetValue(result.ResultPopulationId,
                    out WhiteboxPopulation targetView) && targetView != null &&
                targetView.TileId == targetTile.Coordinate && targetView.Leader != null)
                end = targetView.Leader.position;

            bool hasRoute = TryNavigationPath(start, end, out Vector3[] path);
            if (!hasRoute) path = new[] { start, end };
            if (result.SourceBlock == result.TargetBlock &&
                Vector3.Distance(start, end) < 0.8f)
            {
                path = new[] { start, start + Vector3.right * 1.5f, end };
                hasRoute = false;
            }
            float distance = PathLength(path);
            float duration = Mathf.Clamp(distance / Mathf.Max(0.1f, transitionSpeed),
                Mathf.Max(0.1f, minimumTransitionSeconds),
                Mathf.Max(minimumTransitionSeconds, maximumTransitionSeconds));
            GameObject objectRoot = new GameObject($"{result.Kind}_Transition");
            objectRoot.transform.SetParent(transform, true);
            PopulationTransitionVisual transition =
                objectRoot.AddComponent<PopulationTransitionVisual>();
            var running = new RunningTransition
            {
                Result = result,
                View = transition,
                Held = pending.Held
            };
            activeTransitions.Add(running);
            transition.Begin(path, !hasRoute ? fallbackArcHeight : 0f, duration,
                WhiteboxPopulation.VisibleMemberCount(result.Amount), movement.Settings,
                pending.Material, pending.SourceColor, targetColor,
                () => FinishTransition(running));
        }
    }

    private void ReleaseUnstartedHold(PendingTransition pending)
    {
        if (pending.Held && transitionHolds.TryGetValue(
                pending.Result.ResultPopulationId, out TransitionHold hold) &&
            --hold.Active <= 0)
            transitionHolds.Remove(pending.Result.ResultPopulationId);
        SyncNow();
    }

    private void CheckTransitions()
    {
        if (activeTransitions.Count == 0) return;
        foreach (RunningTransition running in activeTransitions.ToArray())
            if (running.View == null ||
                !TryFindTile(running.Result.TargetBlock, out _) ||
                FindPopulation(running.Result.TargetBlock,
                    running.Result.ResultPopulationId) == null)
            {
                if (running.View != null) running.View.Cancel();
                else FinishTransition(running);
            }
    }

    private void FinishTransition(RunningTransition running)
    {
        if (!activeTransitions.Remove(running)) return;
        if (running.Held && transitionHolds.TryGetValue(running.Result.ResultPopulationId,
                out TransitionHold hold) && --hold.Active <= 0)
        {
            transitionHolds.Remove(running.Result.ResultPopulationId);
            if (hold.StagedView != null &&
                TryFindTile(running.Result.TargetBlock, out _) &&
                FindPopulation(running.Result.TargetBlock,
                    running.Result.ResultPopulationId) != null)
            {
                hold.StagedView.gameObject.SetActive(true);
                hold.StagedView.PauseWandering(false);
            }
        }
        SyncNow();
    }

    private void ClearTransitions()
    {
        foreach (RunningTransition running in activeTransitions)
            if (running.View != null) running.View.CancelSilently();
        activeTransitions.Clear();
        pendingTransitions.Clear();
        transitionHolds.Clear();
    }

    private bool TryFindTile(BlockInfo block, out MapTileInstance tile)
    {
        if (grid != null && block != null)
            foreach (MapTileInstance candidate in grid.GetPlacedTiles())
                if (candidate != null && candidate.Block == block)
                {
                    tile = candidate;
                    return true;
                }
        tile = null;
        return false;
    }

    private static PopulationData FindPopulation(BlockInfo block, string id)
    {
        if (block == null || block.community == null || string.IsNullOrEmpty(id)) return null;
        foreach (PopulationData population in block.community)
            if (population != null && population.speciesAmount > 0 &&
                population.PopulationId == id) return population;
        return null;
    }

    private static Vector3 TileAnchor(MapTileInstance tile) =>
        tile.transform.position + Vector3.up * 0.6f;

    private bool TryNavigationPath(Vector3 start, Vector3 end, out Vector3[] points)
    {
        points = null;
        if (grid == null || grid.Navigation == null || grid.Navigation.IsUpdating) return false;
        NavMeshSurface surface = grid.Navigation.GetComponent<NavMeshSurface>();
        if (surface == null || surface.navMeshData == null) return false;
        var filter = new NavMeshQueryFilter
        {
            agentTypeID = surface.agentTypeID,
            areaMask = NavMesh.AllAreas
        };
        if (!NavMesh.SamplePosition(start, out NavMeshHit from, 2f, filter) ||
            !NavMesh.SamplePosition(end, out NavMeshHit to, 2f, filter)) return false;
        var route = new NavMeshPath();
        if (!NavMesh.CalculatePath(from.position, to.position, filter, route) ||
            route.status != NavMeshPathStatus.PathComplete || route.corners.Length < 2)
            return false;
        points = route.corners;
        for (int i = 0; i < points.Length; i++) points[i] += Vector3.up * 0.5f;
        points[0] = start;
        points[points.Length - 1] = end;
        return true;
    }

    private static float PathLength(Vector3[] path)
    {
        float length = 0f;
        for (int i = 1; i < path.Length; i++)
            length += Vector3.Distance(path[i - 1], path[i]);
        return length;
    }
}
