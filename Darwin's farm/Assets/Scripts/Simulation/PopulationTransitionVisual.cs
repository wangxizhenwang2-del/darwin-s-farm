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
    private MaterialPropertyBlock properties;
    private Action completed;

    public void Begin(Vector3[] waypoints, float arc, float seconds, int visibleMembers,
        PopulationMovementSettings settings, Material material, Color from, Color to,
        Action onCompleted)
    {
        path = waypoints;
        arcHeight = Mathf.Max(0f, arc);
        duration = Mathf.Max(0.1f, seconds);
        sourceColor = from;
        targetColor = to;
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
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            renderer.SetPropertyBlock(properties);
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
