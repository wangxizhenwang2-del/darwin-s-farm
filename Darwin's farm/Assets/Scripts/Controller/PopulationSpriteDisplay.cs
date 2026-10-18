using System.Collections.Generic;
using UnityEngine;

// The cube transform remains the movement and clearance proxy. Its child is
// only a camera-facing illustration and never supplies collision geometry.
public static class PopulationSpriteDisplay
{
    private const float HeightPerProxySize = 1.5f;
    private static Material waterMaterial;
    private static MaterialPropertyBlock waterProperties;
    private static readonly Dictionary<Sprite, Bounds> visibleBounds = new Dictionary<Sprite, Bounds>();

    public static SpriteRenderer Create(Transform proxy, Sprite sprite, Color tint,
        int sortingOrder = 0)
    {
        GameObject image = new GameObject("PopulationSprite");
        image.transform.SetParent(proxy, false);
        image.transform.localPosition = Vector3.up * 0.25f;
        SpriteRenderer renderer = image.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = sortingOrder;
        Configure(renderer, sprite, tint);
        return renderer;
    }

    public static void Configure(SpriteRenderer renderer, Sprite sprite, Color tint)
    {
        if (renderer == null) return;
        renderer.sprite = sprite;
        renderer.color = tint;
        float height = sprite == null ? 1f : Mathf.Max(0.01f, sprite.bounds.size.y);
        renderer.transform.localScale = Vector3.one * (HeightPerProxySize / height);
    }

    public static void FaceCamera(SpriteRenderer renderer, Camera camera)
    {
        if (renderer != null && camera != null)
            renderer.transform.rotation = camera.transform.rotation;
    }

    public static void ConfigureAir(SpriteRenderer renderer)
    {
        if (renderer == null) return;
        Material material = Resources.Load<Material>("Population/AirPopulationSprite");
        if (material != null) renderer.sharedMaterial = material;
    }

    public static void ConfigureWater(SpriteRenderer renderer, float height, float fraction = .25f)
    {
        if (!renderer) return;
        if (!waterMaterial) waterMaterial = Resources.Load<Material>("Water/WaterPopulationSprite");
        if (waterMaterial && renderer.sharedMaterial != waterMaterial) renderer.sharedMaterial = waterMaterial;
        float line = -10000;
        if (renderer.sprite)
        {
            Bounds bounds = VisibleBounds(renderer.sprite);
            line = bounds.min.y + bounds.size.y * Mathf.Clamp01(fraction);
            // Anchor the silhouette's waterline, compensating for transparent margins,
            // imported pivots, body size and the camera-facing billboard's inclination.
            Vector3 anchor = renderer.transform.parent.position; anchor.y = height + .008f;
            Vector3 scale = renderer.transform.lossyScale;
            renderer.transform.position = anchor - renderer.transform.right * (bounds.center.x * scale.x)
                - renderer.transform.up * (line * scale.y);
        }
        if (waterProperties == null) waterProperties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(waterProperties);
        waterProperties.SetFloat("_WaterHeight", height);
        waterProperties.SetFloat("_WaterSpriteLine", line); renderer.SetPropertyBlock(waterProperties);
    }

    private static Bounds VisibleBounds(Sprite sprite)
    {
        if (visibleBounds.TryGetValue(sprite, out Bounds bounds)) return bounds;
        var vertices = sprite.vertices;
        bounds = new Bounds(vertices.Length > 0 ? (Vector3)vertices[0] : Vector3.zero, Vector3.zero);
        foreach (var vertex in vertices) bounds.Encapsulate((Vector3)vertex);
        visibleBounds.Add(sprite, bounds); return bounds;
    }

    public static float WaterRadius(float size, float multiplier, Sprite sprite, Camera camera, float fraction)
    {
        float radius = size * multiplier;
        if (!sprite) return radius;
        Bounds bounds = VisibleBounds(sprite);
        float scale = size * HeightPerProxySize / Mathf.Max(.01f, sprite.bounds.size.y);
        Vector3 right = camera ? camera.transform.right : Vector3.right;
        Vector3 up = camera ? camera.transform.up : Vector3.up; right.y = up.y = 0;
        float line = bounds.min.y + bounds.size.y * Mathf.Clamp01(fraction);
        for (int corner = 0; corner < 4; corner++)
        {
            float x = (corner & 1) == 0 ? -bounds.extents.x : bounds.extents.x;
            float y = ((corner & 2) == 0 ? bounds.min.y : bounds.max.y) - line;
            radius = Mathf.Max(radius, (right * x + up * y).magnitude * scale);
        }
        return radius;
    }
}
