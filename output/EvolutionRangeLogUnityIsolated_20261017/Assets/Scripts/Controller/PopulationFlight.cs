using UnityEngine;

// Local patrol owns no ecological state. Only a committed migration can change
// the owning tile. It does not use ground NavMesh or ground separation rules.
public sealed class PopulationFlight
{
    private readonly MapTileInstance tile;
    private readonly PopulationMovementSettings settings;
    private float phase;
    private readonly float direction;
    private float radius;
    private float ceilingOffset;
    private float refreshIn;
    public Vector3 Position { get; private set; }

    public PopulationFlight(MapTileInstance owner, PopulationMovementSettings movement)
    {
        tile = owner;
        settings = movement;
        phase = Random.Range(0f, Mathf.PI * 2f);
        direction = Random.value < 0.5f ? -1f : 1f;
        SetSprite(null);
        RefreshCeiling();
        UpdatePosition();
    }

    public void SetSprite(Sprite sprite)
    {
        float aspect = sprite == null ? 1f : sprite.bounds.size.x / Mathf.Max(0.01f, sprite.bounds.size.y);
        float imageRadius = settings.leaderSize * 1.5f * Mathf.Max(1f, aspect) * 0.5f;
        radius = Mathf.Max(0.1f, Mathf.Min(Mathf.Max(0.5f, settings.flightPatrolRadius),
            MapGridManager.TileSize * 0.5f - Mathf.Max(0.2f, settings.flightEdgeMargin) - imageRadius));
        UpdatePosition();
    }

    public static float CruiseHeight(MapTileInstance owner, PopulationMovementSettings settings)
    {
        float top = owner.transform.position.y;
        int groundLayer = LayerMask.NameToLayer("Ground");
        // Low flight is measured from the actual ground, never the tallest
        // cactus/tree/mountain decoration on the entire tile.
        foreach (Collider part in owner.GetComponentsInChildren<Collider>())
        {
            if (!part.enabled || !part.gameObject.activeInHierarchy ||
                part.isTrigger || part.gameObject.layer != groundLayer) continue;
            top = Mathf.Max(top, part.bounds.max.y);
        }
        return top + Mathf.Max(0.75f, settings.flightHeight);
    }

    public void RefreshCeiling()
    {
        ceilingOffset = CruiseHeight(tile, settings) - tile.transform.position.y;
        refreshIn = 0.5f;
    }

    public void Tick(float deltaTime, bool paused)
    {
        refreshIn -= deltaTime;
        if (refreshIn <= 0f) RefreshCeiling();
        if (!paused) phase = Mathf.Repeat(phase + direction *
            Mathf.Max(0.1f, settings.flightSpeed) / Mathf.Max(1f, radius) * deltaTime, Mathf.PI * 2f);
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        Vector3 local = new Vector3(Mathf.Sin(phase) * radius, 0f,
            Mathf.Cos(phase) * radius * 0.72f);
        Position = tile.transform.TransformPoint(local);
        Position = new Vector3(Position.x, tile.transform.position.y + ceilingOffset +
            Mathf.Sin(phase * 2f) * Mathf.Max(0f, settings.flightBob), Position.z);
    }

    public bool Contains(Vector3 position)
    {
        Vector3 local = tile.transform.InverseTransformPoint(position);
        float half = MapGridManager.TileSize * 0.5f - Mathf.Max(0.2f, settings.flightEdgeMargin);
        return Mathf.Abs(local.x) <= half && Mathf.Abs(local.z) <= half &&
            position.y >= tile.transform.position.y + Mathf.Max(0.75f, settings.flightHeight) -
                Mathf.Max(0f, settings.flightBob) - 0.001f;
    }
}
