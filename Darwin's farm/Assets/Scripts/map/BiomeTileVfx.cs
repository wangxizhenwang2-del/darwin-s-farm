using UnityEngine;

// Adapts the artist's native rigs to the actual placed tile. Baked emission
// meshes store height above the authored ground, not sample-scene coordinates.
public sealed class BiomeTileVfx : MonoBehaviour
{
    [SerializeField] private bool useAuthoredCollision;
    public bool UsesAuthoredCollision => useAuthoredCollision;
    private ParticleSystem[] systems;
    private Mesh[] sources;
    private Mesh[] projected;
    private BoxCollider boundary;
    private bool retiring;
    private Vector3[] authoredPositions;
    private Matrix4x4 lastPlacement;

    public void Initialize(MeshCollider ground)
    {
        if (systems == null)
        {
            systems = GetComponentsInChildren<ParticleSystem>(true);
            sources = new Mesh[systems.Length];
            projected = new Mesh[systems.Length];
            authoredPositions = new Vector3[systems.Length];
            for (int i = 0; i < systems.Length; i++)
            {
                sources[i] = systems[i].shape.mesh;
                authoredPositions[i] = systems[i].transform.localPosition;
            }
            var boundsObject = new GameObject("TileParticleBounds");
            boundsObject.layer = 2;
            boundsObject.transform.SetParent(transform, false);
            boundary = boundsObject.AddComponent<BoxCollider>();
            boundary.isTrigger = true;
            boundary.center = new Vector3(0, 4, 0);
            boundary.size = new Vector3(MapGridManager.TileSize, 20, MapGridManager.TileSize);
            if (useAuthoredCollision)
                foreach (var filter in transform.parent.GetComponentsInChildren<MeshFilter>())
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (!renderer || !renderer.enabled || !filter.sharedMesh ||
                        filter.name == "BiomeGround" || filter.name.Contains("Outline")) continue;
                    // Separate from navigation's solid obstacle boxes: the
                    // volcano's hollow crater must stay open to its own eruption.
                    var proxy = new GameObject("ParticleCollision_" + filter.name);
                    proxy.layer = 2;
                    proxy.transform.SetParent(filter.transform, false);
                    proxy.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                }
        }
        bool moved = lastPlacement != transform.localToWorldMatrix;
        lastPlacement = transform.localToWorldMatrix;
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem particles = systems[i];
            if (sources[i] == null)
            {
                // Native snowfall boxes and eruption/steam cones keep their
                // authored shape, burst cycle and offset above the local ground.
                Vector3 position = transform.TransformPoint(authoredPositions[i]);
                if (ground.Raycast(new Ray(position + Vector3.up * 80f, Vector3.down), out RaycastHit hit, 160f))
                    position.y += transform.InverseTransformPoint(hit.point).y;
                particles.transform.position = position;
            }
            else
            {
                Vector3[] vertices = sources[i].vertices;
                for (int v = 0; v < vertices.Length; v++)
                {
                    Vector3 world = particles.transform.TransformPoint(new Vector3(vertices[v].x, 0, vertices[v].z));
                    if (ground.Raycast(new Ray(world + Vector3.up * 80f, Vector3.down), out RaycastHit hit, 160f))
                        vertices[v].y += particles.transform.InverseTransformPoint(hit.point).y;
                }
                if (projected[i] == null)
                    projected[i] = new Mesh { name = sources[i].name + " (Placed)", hideFlags = HideFlags.DontSave };
                projected[i].vertices = vertices;
                projected[i].triangles = sources[i].triangles;
                projected[i].RecalculateNormals();
                projected[i].RecalculateBounds();
                var shape = particles.shape;
                shape.mesh = projected[i];
            }
            var collision = particles.collision;
            // The sample collision proxies are disabled. ColorSurface owns the ground.
            collision.collidesWith = useAuthoredCollision ? LayerMask.GetMask("Ground", "Ignore Raycast") :
                LayerMask.GetMask("Ground", "Obstacle");
            var trigger = particles.trigger;
            for (int c = trigger.colliderCount - 1; c >= 0; c--) trigger.RemoveCollider(c);
            trigger.AddCollider(boundary);
            trigger.enabled = true;
            trigger.outside = ParticleSystemOverlapAction.Kill;
            if (useAuthoredCollision) trigger.inside = ParticleSystemOverlapAction.Ignore;
            var main = particles.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        }
        if (retiring) return;
        bool starting = !gameObject.activeSelf;
        gameObject.SetActive(true);
        if (starting || moved)
        {
            // Apply the authored wind/visibility settings before warming up.
            var wood = GetComponent<Darwin.Rendering.WoodlandWindController>();
            if (wood != null) wood.RefreshSettings();
            var grass = GetComponent<Darwin.Rendering.GrasslandWindController>();
            if (grass != null) grass.RefreshSettings();
            var wind = GetComponent<Darwin.Rendering.MoebiusWindController>();
            if (wind != null) wind.RefreshSettings();
            foreach (ParticleSystem particles in systems)
            {
                particles.Simulate(3f, false, true, false);
                particles.Play(false);
            }
        }
    }

    public void Retire()
    {
        retiring = true;
        // The wind components update emission every frame, so stop those as well.
        foreach (Behaviour controller in GetComponents<Behaviour>())
            if (controller != this) controller.enabled = false;
        if (systems == null) return;
        foreach (ParticleSystem particles in systems)
            particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
    }

    private void OnDestroy()
    {
        if (projected == null) return;
        foreach (Mesh mesh in projected)
            if (mesh != null) Destroy(mesh);
    }
}
