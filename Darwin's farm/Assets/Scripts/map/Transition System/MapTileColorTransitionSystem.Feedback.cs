using System.Collections.Generic;
using UnityEngine;

public partial class MapTileColorTransitionSystem
{
    [Header("Biome change feedback")]
    [SerializeField] private bool animateBiomeChanges = true;
    [SerializeField, Min(0.1f)] private float biomeTransitionSeconds = 1.1f;

    private bool CanAnimateBiomeChanges => Application.isPlaying && isActiveAndEnabled && animateBiomeChanges;
    private bool animateSurfaceRefresh;

    private sealed class SurfaceFeedback
    {
        public SurfaceData surface;
        public Color[] from;
        public Color[] to;
        public Color[] current;
        public float age;
        public float fromArtStrength;
        public float toArtStrength;
        public BiomeGroundData fromArtwork;
        public BiomeGroundData toArtwork;
    }

    private sealed class DecorationPose
    {
        public Transform target;
        public Vector3 position;
        public Vector3 scale;
        public Vector3 anchor;
        public float delay;
        public float amount = 1f;
        public float exitFrom = 1f;

        public void Apply(float value)
        {
            amount = value;
            if (target == null) return;
            // Root the mesh's growth at the ground, independently of asset pivots.
            target.localScale = scale * Mathf.Max(0.001f, value);
            target.localPosition = Vector3.LerpUnclamped(anchor, position, value);
        }
    }

    private readonly List<SurfaceFeedback> surfaceFeedback = new List<SurfaceFeedback>();
    private readonly List<BiomeVisualData> decorationFeedback = new List<BiomeVisualData>();
    public bool IsBiomeTransitioning => surfaceFeedback.Count > 0 || decorationFeedback.Count > 0;

    private void BeginSurfaceFeedback(SurfaceData surface, Vector3[] previousVertices,
        Color[] previousColors, List<Vector3> vertices, List<Color> colors)
    {
        if (!CanAnimateBiomeChanges || previousColors.Length == 0)
        { CommitGroundArtwork(surface); return; }
        for (int i = 0; i < surfaceFeedback.Count; i++)
        {
            SurfaceFeedback active = surfaceFeedback[i];
            if (active.surface != surface) continue;
            // An unrelated map rebuild must not restart an ongoing color blend.
            if (SameColors(active.to, colors) &&
                Mathf.Approximately(active.toArtStrength, surface.targetArtStrength) &&
                active.toArtwork == surface.targetArtwork)
            {
                surface.mesh.SetColors(active.current);
                return;
            }
            surfaceFeedback.RemoveAt(i);
            break;
        }

        if (!animateSurfaceRefresh)
        { CommitGroundArtwork(surface); return; }
        Color[] from = previousColors;
        if (from.Length != colors.Count)
        {
            // Height changes can add cliff faces. Wall and top vertices use
            // the same terrain color at a given horizontal position.
            var palette = new Dictionary<Vector2Int, Color>();
            for (int i = 0; i < previousColors.Length; i++)
                palette[ColorKey(previousVertices[i])] = previousColors[i];
            from = new Color[colors.Count];
            for (int i = 0; i < from.Length; i++)
                from[i] = palette.TryGetValue(ColorKey(vertices[i]), out Color old) ? old : colors[i];
        }
        if (SameColors(from, colors) && Mathf.Approximately(surface.artStrength, surface.targetArtStrength) &&
            surface.groundArtwork == surface.targetArtwork)
        { CommitGroundArtwork(surface); return; }
        var blend = new SurfaceFeedback
        {
            surface = surface, from = from, to = colors.ToArray(), current = (Color[])from.Clone(),
            fromArtStrength = surface.artStrength, toArtStrength = surface.targetArtStrength,
            fromArtwork = surface.groundArtwork, toArtwork = surface.targetArtwork
        };
        surfaceFeedback.Add(blend);
        surface.mesh.SetColors(blend.current);
        SetGroundArtStrength(surface, surface.artStrength);
    }

    private static Vector2Int ColorKey(Vector3 point) =>
        new Vector2Int(Mathf.RoundToInt(point.x * 1000f), Mathf.RoundToInt(point.z * 1000f));

    private static bool SameColors(Color[] first, IList<Color> second)
    {
        if (first.Length != second.Count) return false;
        for (int i = 0; i < first.Length; i++)
        {
            Color delta = first[i] - second[i];
            if (Mathf.Abs(delta.r) + Mathf.Abs(delta.g) + Mathf.Abs(delta.b) > 0.00001f) return false;
        }
        return true;
    }

    private static void CacheDecorationPoses(BiomeVisualData data, MeshCollider ground)
    {
        var included = new HashSet<Transform>();
        foreach (Renderer renderer in data.root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                IsGroundMesh(data.biome, renderer.name) ||
                !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;
            included.Add(renderer.transform);
        }
        foreach (Transform target in included)
        {
            bool nested = false;
            for (Transform parent = target.parent; parent != null && parent != data.root.transform; parent = parent.parent)
                if (included.Contains(parent)) { nested = true; break; }
            if (nested) continue;
            Renderer renderer = target.GetComponent<Renderer>();
            Vector3 anchor = renderer.bounds.center;
            anchor.y = renderer.bounds.min.y;
            if (ground.Raycast(new Ray(anchor + Vector3.up * 80f, Vector3.down), out RaycastHit hit, 160f))
                anchor = hit.point;
            Vector3 local = data.tile.transform.InverseTransformPoint(anchor);
            data.decorations.Add(new DecorationPose
            {
                target = target, position = target.localPosition, scale = target.localScale,
                anchor = target.parent.InverseTransformPoint(anchor),
                delay = Mathf.Clamp01(new Vector2(local.x, local.z).magnitude / 10f) * 0.22f
            });
        }
    }

    private void ReprojectDecorations(BiomeVisualData data, MeshCollider ground)
    {
        ProjectNativeGround(data);
        foreach (BiomeTileVfx vfx in data.root.GetComponentsInChildren<BiomeTileVfx>(true))
            vfx.Initialize(ground);
        foreach (DecorationPose pose in data.decorations)
        {
            if (pose.target == null) continue;
            Vector3 anchor = pose.target.parent.TransformPoint(pose.anchor);
            if (!ground.Raycast(new Ray(anchor + Vector3.up * 80f, Vector3.down), out RaycastHit hit, 160f)) continue;
            Vector3 offset = pose.target.parent.InverseTransformVector(Vector3.up * (hit.point.y - anchor.y));
            pose.anchor += offset;
            pose.position += offset;
            pose.Apply(pose.amount);
        }
        // Navigation proxies keep their complete size during visual growth.
        Transform obstacles = data.root.transform.Find("BiomeObstacles");
        if (obstacles == null) return;
        foreach (BoxCollider box in obstacles.GetComponentsInChildren<BoxCollider>())
        {
            Vector3 position = box.transform.position;
            if (ground.Raycast(new Ray(position + Vector3.up * 80f, Vector3.down), out RaycastHit hit, 160f))
                box.transform.position = new Vector3(position.x, hit.point.y + box.size.y * 0.5f, position.z);
        }
    }

    private void GrowDecorations(BiomeVisualData data)
    {
        data.age = 0f;
        foreach (DecorationPose pose in data.decorations) pose.Apply(0f);
        decorationFeedback.Add(data);
    }

    private void RetireDecorations(BiomeVisualData data)
    {
        if (data.root == null) return;
        data.retiring = true;
        data.age = 0f;
        data.root.name = "RetiringBiomeVisual_" + data.biome;
        SetNativeGroundVisible(data, false);
        Transform obstacles = data.root.transform.Find("BiomeObstacles");
        if (obstacles != null) obstacles.gameObject.SetActive(false);
        foreach (BiomeTileVfx vfx in data.root.GetComponentsInChildren<BiomeTileVfx>(true)) vfx.Retire();
        foreach (ParticleSystem particles in data.root.GetComponentsInChildren<ParticleSystem>())
            particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        foreach (DecorationPose pose in data.decorations) pose.exitFrom = pose.amount;
        if (!decorationFeedback.Contains(data)) decorationFeedback.Add(data);
    }

    private void Update()
    {
        float duration = Mathf.Max(0.1f, biomeTransitionSeconds);
        // A terrain bake or asset load can consume most of a frame. Preserve
        // the visible fade/growth phases instead of skipping them after a hitch.
        float step = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        for (int i = surfaceFeedback.Count - 1; i >= 0; i--)
        {
            SurfaceFeedback blend = surfaceFeedback[i];
            if (blend.surface.renderer == null || blend.surface.mesh == null)
            { surfaceFeedback.RemoveAt(i); continue; }
            blend.age += step;
            float t = animateBiomeChanges ? Mathf.Clamp01(blend.age / duration) : 1f;
            float eased = t * t * (3f - 2f * t);
            for (int vertex = 0; vertex < blend.current.Length; vertex++)
                blend.current[vertex] = Color.LerpUnclamped(blend.from[vertex], blend.to[vertex], eased);
            blend.surface.mesh.SetColors(blend.current);
            ApplyGroundArtworkTransition(blend, eased);
            if (t >= 1f) surfaceFeedback.RemoveAt(i);
        }
        for (int i = decorationFeedback.Count - 1; i >= 0; i--)
        {
            BiomeVisualData data = decorationFeedback[i];
            if (data.root == null || data.tile == null || !data.tile.gameObject.activeInHierarchy)
            {
                RemoveBiomeVisual(data);
                decorationFeedback.RemoveAt(i);
                continue;
            }
            data.age += step;
            float age = animateBiomeChanges ? data.age / duration : 2f;
            foreach (DecorationPose pose in data.decorations)
            {
                float t = data.retiring ? (age - pose.delay * 0.35f) / 0.32f :
                    (age - 0.15f - pose.delay) / 0.6f;
                t = Mathf.Clamp01(t);
                float eased = t * t * (3f - 2f * t);
                pose.Apply(data.retiring ? pose.exitFrom * (1f - eased) : eased);
            }
            if (age < (data.retiring ? 0.42f : 1f)) continue;
            if (data.retiring) RemoveBiomeVisual(data);
            else
            {
                foreach (DecorationPose pose in data.decorations) pose.Apply(1f);
                SetNativeGroundVisible(data, true);
            }
            decorationFeedback.RemoveAt(i);
        }
    }

    private void FinishBiomeFeedback()
    {
        foreach (SurfaceFeedback blend in surfaceFeedback)
            if (blend.surface.mesh != null)
            {
                blend.surface.mesh.SetColors(blend.to);
                ApplyGroundArtworkTransition(blend, 1f);
            }
        surfaceFeedback.Clear();
        foreach (BiomeVisualData data in decorationFeedback)
            if (data.retiring) RemoveBiomeVisual(data);
            else
            {
                foreach (DecorationPose pose in data.decorations) pose.Apply(1f);
                SetNativeGroundVisible(data, true);
            }
        decorationFeedback.Clear();
    }
}
