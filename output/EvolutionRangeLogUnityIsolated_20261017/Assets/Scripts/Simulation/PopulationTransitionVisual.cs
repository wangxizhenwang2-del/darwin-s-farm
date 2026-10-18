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
    private Renderer proxyRenderer;
    private SpriteRenderer sourceImage;
    private SpriteRenderer targetImage;
    private Camera viewCamera;
    private MaterialPropertyBlock properties;
    private Action completed;

    public void Begin(Vector3[] waypoints, float arc, float seconds,
        PopulationMovementSettings settings, Material material, Sprite fromSprite,
        Sprite toSprite, Color from, Color to, Camera camera, Action onCompleted) =>
        Begin(waypoints, arc, seconds, settings, material, fromSprite,
            toSprite, from, to, camera, onCompleted, false);

    public void Begin(Vector3[] waypoints, float arc, float seconds,
        PopulationMovementSettings settings, Material material, Sprite fromSprite,
        Sprite toSprite, Color from, Color to, Camera camera, Action onCompleted,
        bool airborne)
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

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "TransitionLeader";
        cube.transform.SetParent(transform, false);
        cube.transform.localScale = Vector3.one * settings.leaderSize;
        Collider collider = cube.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        proxyRenderer = cube.GetComponent<Renderer>();
        if (material != null) proxyRenderer.sharedMaterial = material;
        proxyRenderer.enabled = fromSprite == null && toSprite == null;
        sourceImage = PopulationSpriteDisplay.Create(cube.transform,
            fromSprite != null ? fromSprite : toSprite, from, 1);
        if (toSprite != null && toSprite != fromSprite)
            targetImage = PopulationSpriteDisplay.Create(cube.transform,
                toSprite, new Color(to.r, to.g, to.b, 0f), 2);
        if (airborne)
        {
            PopulationSpriteDisplay.ConfigureAir(sourceImage);
            PopulationSpriteDisplay.ConfigureAir(targetImage);
        }
        Camera activeCamera = viewCamera != null ? viewCamera : Camera.main;
        PopulationSpriteDisplay.FaceCamera(sourceImage, activeCamera);
        PopulationSpriteDisplay.FaceCamera(targetImage, activeCamera);
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
        PopulationSpriteDisplay.FaceCamera(sourceImage, camera);
        PopulationSpriteDisplay.FaceCamera(targetImage, camera);
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
        proxyRenderer.GetPropertyBlock(properties);
        properties.SetColor("_BaseColor", color);
        properties.SetColor("_Color", color);
        proxyRenderer.SetPropertyBlock(properties);
        if (targetImage == null)
        {
            sourceImage.color = color;
            return;
        }
        sourceImage.color = new Color(sourceColor.r, sourceColor.g, sourceColor.b, 1f - blend);
        targetImage.color = new Color(targetColor.r, targetColor.g, targetColor.b, blend);
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
