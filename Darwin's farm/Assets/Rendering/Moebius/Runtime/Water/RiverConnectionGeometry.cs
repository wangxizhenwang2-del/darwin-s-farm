using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Darwin.Rendering
{
    internal enum RiverEndKind { Closed, Joined, Falling, Receiving }
    internal sealed class RiverEnd
    {
        public MoebiusRiverTile tile, neighbour;
        public RiverPort port;
        public RiverEndKind kind;
        public float lipRadius, capInset, bankHeight;
    }

    internal static class RiverConnectionGeometry
    {
        static float S(Vector3 point, RiverEnd end) => Vector3.Dot(point - end.port.Center, end.port.Normal);
        static Vector3 Tangent(RiverEnd end) => Vector3.Cross(Vector3.up, end.port.Normal);

        static void Clip(List<Vector3> source, List<Vector3> result, Vector3 normal, float offset)
        {
            result.Clear(); if (source.Count == 0) return;
            var a = source[source.Count - 1]; float da = Vector3.Dot(a, normal) - offset;
            foreach (var b in source)
            {
                float db = Vector3.Dot(b, normal) - offset;
                if ((da <= 0) != (db <= 0)) result.Add(Vector3.LerpUnclamped(a, b, da / (da - db)));
                if (db <= .000001f) result.Add(b);
                a = b; da = db;
            }
        }

        public static Mesh Water(RiverSurfaceProfile profile, float level, List<RiverEnd> ends)
        {
            var positions = profile.waterMesh.vertices; var indices = profile.waterMesh.triangles;
            var vertices = new List<Vector3>(positions.Length); var triangles = new List<int>(indices.Length);
            var a = new List<Vector3>(32); var b = new List<Vector3>(32);
            for (int i = 0; i < indices.Length; i += 3)
            {
                a.Clear(); a.Add(positions[indices[i]]); a.Add(positions[indices[i + 1]]); a.Add(positions[indices[i + 2]]);
                foreach (var end in ends)
                {
                    if (a.Count == 0) break;
                    if (end.kind == RiverEndKind.Closed && !profile.fullTileWater)
                    {
                        float radius = end.port.width * .5f, center = -radius - end.capInset;
                        float maximum = float.NegativeInfinity; foreach (var point in a) maximum = Mathf.Max(maximum, S(point, end));
                        if (maximum <= center) continue;
                        // A convex semicircle is the intersection of its supporting half-planes.
                        // The existing channel walls bound its rear half; other river arms are untouched.
                        for (int j = 0; j <= 24 && a.Count > 0; j++)
                        {
                            float angle = Mathf.Lerp(-Mathf.PI * .5f, Mathf.PI * .5f, j / 24f);
                            var normal = end.port.Normal * Mathf.Cos(angle) + Tangent(end) * Mathf.Sin(angle);
                            var origin = end.port.Center + end.port.Normal * center;
                            Clip(a, b, normal, Vector3.Dot(origin, normal) + radius);
                            var swap = a; a = b; b = swap;
                        }
                    }
                }
                for (int j = 1; j + 1 < a.Count; j++)
                {
                    if (Vector3.Cross(a[j] - a[0], a[j + 1] - a[0]).sqrMagnitude < 1e-12f) continue;
                    int first = vertices.Count;
                    foreach (var p in new[] { a[0], a[j], a[j + 1] }) vertices.Add(new Vector3(p.x, level, p.z));
                    triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                }
            }
            var mesh = NewMesh("Connected River Surface"); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var bounds = mesh.bounds; bounds.Expand(new Vector3(0, 1.2f, 0)); mesh.bounds = bounds; return mesh;
        }

        public static Mesh Cap(RiverEnd end, float waterLevel, float rounding)
        {
            const int columns = 32, rows = 8;
            float radius = end.port.width * .5f, center = -radius - end.capInset;
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var sides = new List<int>();
            for (int z = 0; z <= rows; z++) for (int x = 0; x <= columns; x++)
            {
                float angle = Mathf.Lerp(-Mathf.PI * .5f, Mathf.PI * .5f, x / (float)columns);
                float t = Mathf.Sin(angle) * radius, edge = center + Mathf.Cos(angle) * radius;
                float s = Mathf.Lerp(edge, .015f, z / (float)rows);
                float u = Mathf.Clamp01((s - edge) / Mathf.Max(rounding, .08f));
                // Quarter-round shoreline: horizontal tangent at water and at the bank crest.
                float height = Mathf.Lerp(waterLevel, end.bankHeight, u * u * (3 - 2 * u));
                vertices.Add(end.port.Center + end.port.Normal * s + Tangent(end) * t + Vector3.up * height);
            }
            for (int z = 0; z < rows; z++) for (int x = 0; x < columns; x++)
            {
                int i = z * (columns + 1) + x;
                AddUp(vertices, triangles, i, i + columns + 1, i + 1);
                AddUp(vertices, triangles, i + 1, i + columns + 1, i + columns + 2);
            }
            // Close the exposed outside face when this river ends at the edge of a map.
            for (int x = 0; x < columns; x++)
            {
                var p = vertices[rows * (columns + 1) + x]; var q = vertices[rows * (columns + 1) + x + 1];
                int i = vertices.Count; vertices.Add(p); vertices.Add(q);
                vertices.Add(new Vector3(p.x, 0, p.z)); vertices.Add(new Vector3(q.x, 0, q.z));
                sides.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
            }
            var mesh = NewMesh("Rounded River Bank"); mesh.SetVertices(vertices); mesh.subMeshCount = 2;
            mesh.SetTriangles(triangles, 0); mesh.SetTriangles(sides, 1); mesh.RecalculateNormals();
            var normals = mesh.normals; for (int x = 0; x <= columns; x++) normals[x] = Vector3.up; mesh.normals = normals;
            mesh.RecalculateBounds(); return mesh;
        }
        static void AddUp(List<Vector3> vertices, List<int> indices, int a, int b, int c)
        {
            if (Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).y < 0) (b, c) = (c, b);
            indices.Add(a); indices.Add(b); indices.Add(c);
        }

        public static Texture2D Shore(RiverSurfaceProfile profile, List<RiverEnd> ends)
        {
            int size = profile.shoreResolution;
            if (profile.shoreDistances == null || profile.shoreDistances.Length != size * size) throw new InvalidOperationException("The river profile needs baked shoreline distance data.");
            var distance = new float[size * size]; var colors = new Color32[distance.Length];
            float dx = profile.shoreRect.z / size, dz = profile.shoreRect.w / size;
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                int i = z * size + x; float d = profile.shoreDistances[i] / 255f * profile.shoreRange;
                var p = new Vector3(profile.shoreRect.x + (x + .5f) * dx, 0, profile.shoreRect.y + (z + .5f) * dz);
                foreach (var end in ends)
                {
                    if (end.kind != RiverEndKind.Closed || profile.fullTileWater) continue;
                    float radius = end.port.width * .5f;
                    float s = S(p, end) + radius + end.capInset;
                    float t = Vector3.Dot(p - end.port.Center, Tangent(end));
                    float capDistance;
                    if (s >= 0) capDistance = Mathf.Max(0, radius - Mathf.Sqrt(s * s + t * t));
                    else capDistance = Mathf.Sqrt(s * s + Mathf.Pow(Mathf.Abs(t) - radius, 2));
                    d = Mathf.Min(d, capDistance);
                }
                distance[i] = d;
            }
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
            {
                int i = z * size + x;
                float gx = (distance[z * size + Mathf.Min(x + 1, size - 1)] - distance[z * size + Mathf.Max(x - 1, 0)]) / (2 * dx);
                float gz = (distance[Mathf.Min(z + 1, size - 1) * size + x] - distance[Mathf.Max(z - 1, 0) * size + x]) / (2 * dz);
                var gradient = Vector2.ClampMagnitude(new Vector2(gx, gz), 1);
                colors[i] = new Color(distance[i] / profile.shoreRange, gradient.x * .5f + .5f, gradient.y * .5f + .5f, 1);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            { name = "Connected River Shore", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(colors); texture.Apply(false, true); return texture;
        }

        public static Mesh Sheet(float width, float height, float radius)
        {
            const int columns = 32, rows = 64;
            float straight = height - radius, length = straight + radius * Mathf.PI * .5f;
            var vertices = new Vector3[(columns + 1) * (rows + 1)]; var normals = new Vector3[vertices.Length]; var uv = new Vector2[vertices.Length];
            var indices = new List<int>();
            for (int y = 0; y <= rows; y++) for (int x = 0; x <= columns; x++)
            {
                float v = y / (float)rows, d = v * length, angle = Mathf.Clamp((d - straight) / radius, 0, Mathf.PI * .5f);
                int i = y * (columns + 1) + x;
                vertices[i] = d <= straight ? new Vector3((x / (float)columns - .5f) * width, d - height * .5f, 0)
                    : new Vector3((x / (float)columns - .5f) * width, height * .5f - radius + radius * Mathf.Sin(angle), radius * (1 - Mathf.Cos(angle)));
                normals[i] = new Vector3(0, Mathf.Sin(angle), -Mathf.Cos(angle)); uv[i] = new Vector2(x / (float)columns, v);
                if (x < columns && y < rows) indices.AddRange(new[] { i, i + columns + 1, i + 1, i + 1, i + columns + 1, i + columns + 2 });
            }
            var mesh = NewMesh("Connected Curved Waterfall"); mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
            var bounds = mesh.bounds; bounds.Expand(.3f); mesh.bounds = bounds; return mesh;
        }

        public static Mesh Foundation(float halfSize, float bottom, float top, Vector4 insets, bool includeTop = true)
        {
            float left = -halfSize + insets.w, right = halfSize - insets.y, back = -halfSize + insets.z, front = halfSize - insets.x;
            var v = new List<Vector3>(); var t = new List<int>();
            Action<Vector3, Vector3, Vector3, Vector3> quad = (a, b, c, d) => { int i = v.Count; v.AddRange(new[] { a, b, c, d }); t.AddRange(new[] { i, i + 1, i + 2, i + 2, i + 1, i + 3 }); };
            quad(new Vector3(left, bottom, back), new Vector3(left, top, back), new Vector3(right, bottom, back), new Vector3(right, top, back));
            quad(new Vector3(right, bottom, front), new Vector3(right, top, front), new Vector3(left, bottom, front), new Vector3(left, top, front));
            quad(new Vector3(left, bottom, front), new Vector3(left, top, front), new Vector3(left, bottom, back), new Vector3(left, top, back));
            quad(new Vector3(right, bottom, back), new Vector3(right, top, back), new Vector3(right, bottom, front), new Vector3(right, top, front));
            // The original Biomes river mesh already covers the tile, including its Y=0
            // ledges. A second roof at that height fights across those exposed ledges.
            if (includeTop)
                quad(new Vector3(left, top, back), new Vector3(left, top, front), new Vector3(right, top, back), new Vector3(right, top, front));
            quad(new Vector3(left, bottom, front), new Vector3(left, bottom, back), new Vector3(right, bottom, front), new Vector3(right, bottom, back));
            var mesh = NewMesh("River Terrain Foundation"); mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        static Mesh NewMesh(string name) => new Mesh { name = name, hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
    }
}
