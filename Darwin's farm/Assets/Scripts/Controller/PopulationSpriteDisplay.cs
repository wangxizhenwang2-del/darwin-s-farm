using UnityEngine;

// The cube transform remains the movement and clearance proxy. Its child is
// only a camera-facing illustration and never supplies collision geometry.
public static class PopulationSpriteDisplay
{
    private const float HeightPerProxySize = 1.5f;

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
}
