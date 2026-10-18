using UnityEngine;

// Adapts the artist's native rigs to the actual placed tile. Baked emission
// meshes store height above the authored ground, not sample-scene coordinates.
public sealed class BiomeTileVfx : MonoBehaviour
{
    private ParticleSystem[] systems;
    private Mesh[] sources;
    private Mesh[] projected;
    private BoxCollider boundary;
    private bool retiring;

    public void Initialize(MeshCollider ground)
    {
        if (systems == null)
        {
            systems = GetComponentsInChildren<ParticleSystem>(true);
            sources = new Mesh[systems.Length];
            projected = new Mesh[systems.Length];
            for (int i = 0; i < systems.Length; i++) sources[i] = systems[i].shape.mesh;
            var boundsObject = new GameObject("TileParticleBounds");
            boundsObject.layer = 2;
            boundsObject.transform.SetParent(transform, false);
            boundary = boundsObject.AddComponent<BoxCollider>();
            boundary.isTrigger = true;
            boundary.center = new Vector3(0, 4, 0);
            boundary.size = new Vector3(MapGridManager.TileSize, 20, MapGridManager.TileSize);
        }
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem particles = systems[i];
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
            var collision = particles.collision;
            // The sample collision proxies are disabled. ColorSurface owns the ground.
            collision.collidesWith = LayerMask.GetMask("Ground", "Obstacle");
            var trigger = particles.trigger;
            for (int c = trigger.colliderCount - 1; c >= 0; c--) trigger.RemoveCollider(c);
            trigger.AddCollider(boundary);
            trigger.enabled = true;
            trigger.outside = ParticleSystemOverlapAction.Kill;
            var main = particles.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        }
        if (retiring) return;
        bool starting = !gameObject.activeSelf;
        gameObject.SetActive(true);
        if (starting)
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
