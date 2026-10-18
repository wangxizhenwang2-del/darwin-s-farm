using System;
using System.Collections.Generic;
using UnityEngine;

// Retains Vincent's topology, split normals, UVs, vertex masks and material slots.
// Only vertex height adapts to the placed tile's base elevation and neighbours.
public sealed class BiomeNativeGround : MonoBehaviour
{
    private sealed class Entry
    {
        public MeshFilter filter;
        public Mesh original;
        public Mesh placed;
    }
    private readonly List<Entry> entries = new List<Entry>();

    public bool Owns(Renderer renderer)
    {
        foreach (Entry entry in entries)
            if (entry.filter.gameObject == renderer.gameObject) return true;
        return false;
    }

    public void SetVisible(bool visible)
    {
        foreach (Entry entry in entries) entry.filter.GetComponent<Renderer>().enabled = visible;
    }

    public void Project(Transform tile, Func<string, bool> isGround, Func<Vector3, Vector3> project)
    {
        if (entries.Count == 0)
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
            {
                if (!isGround(filter.name) || filter.sharedMesh == null) continue;
                Mesh original = filter.sharedMesh;
                Mesh placed = Instantiate(original);
                placed.name = original.name + " (Native Ground)";
                placed.hideFlags = HideFlags.DontSave;
                entries.Add(new Entry { filter = filter, original = original, placed = placed });
                filter.sharedMesh = placed;
            }
        foreach (Entry entry in entries)
        {
            Vector3[] points = entry.original.vertices;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 point = tile.InverseTransformPoint(entry.filter.transform.TransformPoint(points[i]));
                points[i] = entry.filter.transform.InverseTransformPoint(tile.TransformPoint(project(point)));
            }
            entry.placed.vertices = points;
            // RecalculateNormals would erase the artist's deliberately split normals.
            entry.placed.RecalculateBounds();
        }
    }

    private void OnDestroy()
    {
        foreach (Entry entry in entries)
            if (entry.placed != null) Destroy(entry.placed);
    }
}
