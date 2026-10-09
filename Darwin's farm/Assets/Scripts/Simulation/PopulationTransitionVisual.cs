using System;
using UnityEngine;

// A temporary, non-colliding symbol for an already-committed ecological change.
// It never participates in population movement or model calculations.
public sealed class PopulationTransitionVisual : MonoBehaviour
{
    private Vector3[] path;
    private float[] cumulativeDistances;
    private float totalDistance;
    private float arcHeight;
    private float duration;
    private float elapsed;
    private Color sourceColor;
    private Color targetColor;
    private Renderer[] renderers;
    private SpriteRenderer[] sourceImages;
    private SpriteRenderer[] targetImages;
    private Camera viewCamera;
    private MaterialPropertyBlock properties;
    private Action completed;

    public void Begin(Vector3[] waypoints, float arc, float seconds, int visibleMembers,
        PopulationMovementSettings settings, Material material, Sprite fromSprite,
        Sprite toSprite, Color from, Color to, Camera camera, Action onCompleted)
    {
        path = waypoints;
        arcHeight = Mathf.Max(0f, arc);
        duration = Mathf.Max(0.1f, seconds);
        sourceColor = from;
        targetColor = to;
        viewCamera = camera;
        completed = onCompleted;
        properties = new MaterialPropertyBlock();
        cumulativeDistances = new float[path.Length];
        for (int i = 1; i < path.Length; i++)
            cumulativeDistances[i] = cumulativeDistances[i - 1] +
                Vector3.Distance(path[i - 1], path[i]);
        totalDistance = cumulativeDistances[path.Length - 1];
        transform.position = path[0];
        Vector3 heading = path[path.Length - 1] - path[0];
        heading.y = 0f;
        if (heading.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(heading, Vector3.up);

        int count = Mathf.Clamp(visibleMembers, 1, 4);
        renderers = new Renderer[count];
        sourceImages = new SpriteRenderer[count];
        targetImages = new SpriteRenderer[count];
        float spacing = Mathf.Max(0.8f, settings.spacing);
        for (int i = 0; i < count; i++)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = i == 0 ? "TransitionLeader" : $"TransitionFollower_{i}";
            cube.transform.SetParent(transform, false);
            cube.transform.localPosition = Vector3.back * (spacing * i);
            cube.transform.localScale = Vector3.one *
                (i == 0 ? settings.leaderSize : settings.followerSize);
            Collider collider = cube.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            renderers[i] = cube.GetComponent<Renderer>();
            if (material != null) renderers[i].sharedMaterial = material;
            renderers[i].enabled = fromSprite == null && toSprite == null;
            sourceImages[i] = PopulationSpriteDisplay.Create(cube.transform,
                fromSprite != null ? fromSprite : toSprite, from, 1);
            if (toSprite != null && toSprite != fromSprite)
                targetImages[i] = PopulationSpriteDisplay.Create(cube.transform,
                    toSprite, new Color(to.r, to.g, to.b, 0f), 2);
            Camera activeCamera = viewCamera != null ? viewCamera : Camera.main;
            PopulationSpriteDisplay.FaceCamera(sourceImages[i], activeCamera);
            PopulationSpriteDisplay.FaceCamera(targetImages[i], activeCamera);
        }
        ApplyColor(sourceColor);
    }

    private void Update()
    {
        if (path == null) return;
        elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        float eased = t * t * (3f - 2f * t);
        Vector3 current = PositionAt(eased);
        if (arcHeight > 0f) current += Vector3.up *
            (Mathf.Sin(Mathf.PI * eased) * arcHeight);
        Vector3 delta = current - transform.position;
        delta.y = 0f;
        if (delta.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(delta, Vector3.up);
        transform.position = current;
        transform.localScale = Vector3.one *
            Mathf.Lerp(1f, 0.2f, Mathf.Clamp01((t - 0.8f) * 5f));
        ApplyColor(Color.Lerp(sourceColor, targetColor, eased));
        Camera camera = viewCamera != null ? viewCamera : Camera.main;
        for (int i = 0; i < sourceImages.Length; i++)
        {
            PopulationSpriteDisplay.FaceCamera(sourceImages[i], camera);
            PopulationSpriteDisplay.FaceCamera(targetImages[i], camera);
        }
        if (t >= 1f) Finish();
    }

    private Vector3 PositionAt(float progress)
    {
        if (totalDistance <= 0.0001f) return path[path.Length - 1];
        float distance = totalDistance * progress;
        for (int i = 1; i < path.Length; i++)
            if (distance <= cumulativeDistances[i])
            {
                float segment = cumulativeDistances[i] - cumulativeDistances[i - 1];
                float local = segment <= 0.0001f ? 1f :
                    (distance - cumulativeDistances[i - 1]) / segment;
                return Vector3.Lerp(path[i - 1], path[i], local);
            }
        return path[path.Length - 1];
    }

    private void ApplyColor(Color color)
    {
        float blend = Mathf.Clamp01((elapsed / duration - 0.25f) * 2f);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            renderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            renderer.SetPropertyBlock(properties);
            if (targetImages[i] == null)
            {
                sourceImages[i].color = color;
                continue;
            }
            sourceImages[i].color = new Color(sourceColor.r, sourceColor.g,
                sourceColor.b, 1f - blend);
            targetImages[i].color = new Color(targetColor.r, targetColor.g,
                targetColor.b, blend);
        }
    }

    public void Cancel() => Finish();

    public void CancelSilently()
    {
        completed = null;
        Destroy(gameObject);
    }

    private void Finish()
    {
        Action callback = completed;
        completed = null;
        callback?.Invoke();
        Destroy(gameObject);
    }
}
