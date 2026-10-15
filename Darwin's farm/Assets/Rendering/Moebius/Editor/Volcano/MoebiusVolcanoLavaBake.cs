using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Darwin.Rendering.Editor
{
    /// <summary>One-time flow UVs for the placed Volcano (1); the player only scrolls the ink texture.</summary>
    public static class MoebiusVolcanoLavaBake
    {
        const string ScenePath = "Assets/Scenes/New Scene.unity";
        const string Folder = "Assets/Rendering/Moebius/Content/Volcano";
        const string InkPath = Folder + "/Volcano1_LavaFlowInk.png";
        const string LakeFieldPath = Folder + "/Volcano1_LavaLakeDualInletField.png";
        const string SourcePath = Folder + "/Volcano1_LavaSource.asset";

        [MenuItem("Tools/Moebius/Volcano/Bake Lava Flow UVs")]
        public static void Bake()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var volcano = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "Volcano (1)");
                if (!volcano) throw new InvalidOperationException("Volcano (1) is missing.");
                var flow = volcano.transform.Find("Lava")?.GetComponent<MeshFilter>();
                var lake = volcano.transform.Find("LavaLake")?.GetComponent<MeshFilter>();
                if (!flow || !lake) throw new InvalidOperationException("Lava or LavaLake is missing.");
                var importer = AssetImporter.GetAtPath("Assets/Building/Volcano.fbx") as ModelImporter;
                bool oldReadable = importer && importer.isReadable;
                if (importer && !oldReadable) { importer.isReadable = true; importer.SaveAndReimport(); }
                try
                {
                    SaveFlowMesh(flow, volcano.transform.Find("Rock")?.GetComponent<MeshFilter>());
                    SaveLakeMesh(lake, flow);
                }
                finally
                {
                    if (importer && !oldReadable) { importer.isReadable = false; importer.SaveAndReimport(); }
                }

                GenerateInk();
                var shader = Shader.Find("Darwin/Moebius Volcano Lava Flow");
                if (!shader) throw new InvalidOperationException("Lava shader is missing.");
                var ink = AssetDatabase.LoadAssetAtPath<Texture2D>(InkPath);
                if (!ink) throw new InvalidOperationException("Flow ink texture import failed.");
                AssignMaterial(flow, shader, ink, "Volcano1_LavaFlow", false);
                AssignMaterial(lake, shader, ink, "Volcano1_LavaLake", true);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[Volcano Lava Bake] Flow UVs, sparse brush ink, and both lava materials applied to Volcano (1).");
            }
            finally
            {
                if (opened && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("Tools/Moebius/Volcano/Bake Lake Dual Inlets")]
        public static void BakeLakeOnly()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var volcano = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "Volcano (1)");
                if (!volcano) throw new InvalidOperationException("Volcano (1) is missing.");
                var flow = volcano.transform.Find("Lava")?.GetComponent<MeshFilter>();
                var lake = volcano.transform.Find("LavaLake")?.GetComponent<MeshFilter>();
                if (!flow || !lake) throw new InvalidOperationException("Lava or LavaLake is missing.");
                SaveLakeMesh(lake, flow);
                var shader = Shader.Find("Darwin/Moebius Volcano Lava Flow");
                var ink = AssetDatabase.LoadAssetAtPath<Texture2D>(InkPath);
                if (!shader || !ink) throw new InvalidOperationException("Existing lava shader or flow ink is missing.");
                AssignMaterial(lake, shader, ink, "Volcano1_LavaLake", true);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[Volcano Lake Bake] Applied dual-inlet distance field to Volcano (1)/LavaLake.");
            }
            finally
            {
                if (opened && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void SaveFlowMesh(MeshFilter filter, MeshFilter rock)
        {
            if (!rock) throw new InvalidOperationException("Rock mesh is missing.");
            var source = AssetDatabase.LoadAssetAtPath<Mesh>(SourcePath);
            if (!source)
            {
                source = UnityEngine.Object.Instantiate(filter.sharedMesh);
                source.name = "Volcano1_LavaSource";
                AssetDatabase.CreateAsset(source, SourcePath);
            }
            var vertices = source.vertices;
            var world = vertices.Select(v => filter.transform.TransformPoint(v)).ToArray();
            float high = world.Max(p => p.y), low = world.Min(p => p.y);
            float range = high - low;
            if (range < 0.5f) throw new InvalidOperationException("Lava height is too small to establish flow direction.");
            Vector3 top = Vector3.zero, bottom = Vector3.zero;
            int topCount = 0, bottomCount = 0;
            foreach (var p in world)
            {
                if (p.y > high - range * 0.16f) { top += p; topCount++; }
                if (p.y < low + range * 0.16f) { bottom += p; bottomCount++; }
            }
            top /= topCount;
            bottom /= bottomCount;
            Vector3 tangent = (bottom - top).normalized;
            Vector2 down = new Vector2(tangent.x, tangent.z);
            if (down.sqrMagnitude < 0.0001f) down = Vector2.up;
            down.Normalize();
            Vector3 across = new Vector3(-down.y, 0, down.x);
            Vector3 outward = Vector3.Cross(across, tangent).normalized;
            var longitudinal = world.Select(p => Vector3.Dot(p - top, tangent)).ToArray();
            float neighborhood = Mathf.Max(0.16f, (longitudinal.Max() - longitudinal.Min()) * 0.025f);
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 center = Vector3.zero;
                int count = 0;
                for (int j = 0; j < vertices.Length; j++)
                {
                    if (Mathf.Abs(longitudinal[j] - longitudinal[i]) > neighborhood) continue;
                    center += world[j];
                    count++;
                }
                center /= count;
                Vector3 radial = world[i] - center;
                float angle = Mathf.Atan2(Vector3.Dot(radial, across), Vector3.Dot(radial, outward));
                uv[i] = new Vector2(0.5f + angle / (2 * Mathf.PI), Mathf.Clamp01((high - world[i].y) / range));
            }
            var triangles = source.triangles;
            double signedVolume = 0;
            for (int i = 0; i < triangles.Length; i += 3)
                signedVolume += Vector3.Dot(world[triangles[i]],
                    Vector3.Cross(world[triangles[i + 1]], world[triangles[i + 2]])) / 6.0;
            float orientation = signedVolume < 0 ? -1 : 1;
            Vector3 mountainCenter = rock.GetComponent<Renderer>().bounds.center;
            var exposed = new System.Collections.Generic.List<int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector3 center = (world[a] + world[b] + world[c]) / 3;
                if (center.y > high - range * 0.1f)
                {
                    exposed.Add(a); exposed.Add(b); exposed.Add(c);
                    continue;
                }
                Vector3 faceNormal = Vector3.Cross(world[b] - world[a], world[c] - world[a]).normalized * orientation;
                Vector3 radial = center - mountainCenter;
                radial.y = 0;
                if (radial.sqrMagnitude < 0.001f) continue;
                if (Vector3.Dot(faceNormal, radial.normalized) > 0.08f)
                {
                    exposed.Add(a); exposed.Add(b); exposed.Add(c);
                }
            }
            if (exposed.Count < triangles.Length / 6)
                throw new InvalidOperationException("Could not identify the exposed lava flow surface.");

            var rockWorld = rock.sharedMesh.vertices.Select(v => rock.transform.TransformPoint(v)).ToArray();
            var rockTriangles = rock.sharedMesh.triangles;
            var used = new bool[vertices.Length];
            foreach (var index in exposed) used[index] = true;
            for (int i = 0; i < world.Length; i++)
            {
                if (!used[i]) continue;
                Vector3 radial = world[i] - mountainCenter;
                radial.y = 0;
                float originalRadius = radial.magnitude;
                if (originalRadius < 0.001f) continue;
                radial /= originalRadius;
                float rockRadius = originalRadius;
                Vector3 rayOrigin = new Vector3(mountainCenter.x, world[i].y, mountainCenter.z);
                for (int t = 0; t < rockTriangles.Length; t += 3)
                {
                    Vector3 a = rockWorld[rockTriangles[t]], b = rockWorld[rockTriangles[t + 1]], c = rockWorld[rockTriangles[t + 2]];
                    if (world[i].y < Mathf.Min(a.y, b.y, c.y) || world[i].y > Mathf.Max(a.y, b.y, c.y)) continue;
                    if (RayTriangle(rayOrigin, radial, a, b, c, out float distance))
                        rockRadius = Mathf.Max(rockRadius, distance);
                }
                float lift = Mathf.Clamp01((high - world[i].y - range * 0.06f) / (range * 0.09f));
                float targetRadius = Mathf.Max(originalRadius, rockRadius + 0.045f);
                world[i] = rayOrigin + radial * Mathf.Lerp(originalRadius, targetRadius, lift);
                vertices[i] = filter.transform.InverseTransformPoint(world[i]);
            }
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = "Volcano1_LavaExposedUV";
            copy.vertices = vertices;
            copy.uv = uv;
            copy.triangles = exposed.ToArray();
            SplitFlowUVSeams(copy);
            copy.RecalculateBounds();
            string path = Folder + "/Volcano1_LavaExposedUV.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(copy, existing);
                UnityEngine.Object.DestroyImmediate(copy);
                copy = existing;
                EditorUtility.SetDirty(copy);
            }
            else AssetDatabase.CreateAsset(copy, path);
            filter.sharedMesh = copy;
            EditorUtility.SetDirty(filter);
            Debug.Log($"[Volcano Lava Bake] Exposed flow: {exposed.Count / 3}/{triangles.Length / 3} triangles, downhill XZ={down}, height={range:F2}m.");
        }

        static void SplitFlowUVSeams(Mesh mesh)
        {
            var vertices = mesh.vertices.ToList();
            var uv = mesh.uv.ToList();
            var triangles = mesh.triangles;
            int seamFaces = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                float a = uv[triangles[i]].x, b = uv[triangles[i + 1]].x, c = uv[triangles[i + 2]].x;
                if (Mathf.Max(a, b, c) - Mathf.Min(a, b, c) <= 0.55f) continue;
                seamFaces++;
                for (int corner = 0; corner < 3; corner++)
                {
                    int index = triangles[i + corner];
                    if (uv[index].x >= 0.5f) continue;
                    vertices.Add(vertices[index]);
                    uv.Add(new Vector2(uv[index].x + 1, uv[index].y));
                    triangles[i + corner] = vertices.Count - 1;
                }
                float minimum = Mathf.Min(uv[triangles[i]].x, uv[triangles[i + 1]].x, uv[triangles[i + 2]].x);
                int maximumCorner = 0;
                for (int corner = 1; corner < 3; corner++)
                    if (uv[triangles[i + corner]].x > uv[triangles[i + maximumCorner]].x) maximumCorner = corner;
                int maximumIndex = triangles[i + maximumCorner];
                if (uv[maximumIndex].x - minimum > 0.55f)
                {
                    vertices.Add(vertices[maximumIndex]);
                    uv.Add(new Vector2(minimum + 0.5f, uv[maximumIndex].y));
                    triangles[i + maximumCorner] = vertices.Count - 1;
                }
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            Debug.Log($"[Volcano Lava Bake] Split {seamFaces} wrapped UV faces without changing the visible flow path.");
        }

        static bool RayTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0;
            Vector3 edge1 = b - a, edge2 = c - a;
            Vector3 p = Vector3.Cross(direction, edge2);
            float det = Vector3.Dot(edge1, p);
            if (Mathf.Abs(det) < 0.000001f) return false;
            float inv = 1 / det;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < -0.0001f || u > 1.0001f) return false;
            Vector3 q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(direction, q) * inv;
            if (v < -0.0001f || u + v > 1.0001f) return false;
            distance = Vector3.Dot(edge2, q) * inv;
            return distance > 0;
        }

        static void SaveLakeMesh(MeshFilter filter, MeshFilter flow)
        {
            var source = filter.sharedMesh;
            var vertices = source.vertices;
            var world = vertices.Select(v => filter.transform.TransformPoint(v)).ToArray();
            var contacts = FindLakeContacts(flow, filter, world);
            float minX = world.Min(p => p.x), maxX = world.Max(p => p.x);
            float minZ = world.Min(p => p.z), maxZ = world.Max(p => p.z);
            float span = Mathf.Max(maxX - minX, maxZ - minZ);
            var center = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
            var uv = world.Select(p => new Vector2(0.5f + (p.x - center.x) / span,
                0.5f + (p.z - center.y) / span)).ToArray();
            SaveMesh(filter, uv, "Volcano1_LavaLakeUV");
            SaveLakeField(center, span, contacts[0], contacts[1]);
        }

        static Vector2[] FindLakeContacts(MeshFilter flow, MeshFilter lake, Vector3[] lakeWorld)
        {
            var source = AssetDatabase.LoadAssetAtPath<Mesh>(SourcePath);
            if (!source) throw new InvalidOperationException("Original Lava source mesh is missing for the lake contact bake.");
            float planeY = lakeWorld.Average(p => p.y);
            var world = source.vertices.Select(v => flow.transform.TransformPoint(v)).ToArray();
            var triangles = source.triangles;
            var nodes = new List<Vector2>();
            var edges = new List<List<int>>();
            var lookup = new Dictionary<Vector2Int, int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = world[triangles[i]], b = world[triangles[i + 1]], c = world[triangles[i + 2]];
                var hits = new List<Vector2>(2);
                AddPlaneHit(a, b, planeY, hits);
                AddPlaneHit(b, c, planeY, hits);
                AddPlaneHit(c, a, planeY, hits);
                if (hits.Count != 2) continue;
                int first = GetContactNode(hits[0], nodes, edges, lookup);
                int second = GetContactNode(hits[1], nodes, edges, lookup);
                if (first == second) continue;
                edges[first].Add(second);
                edges[second].Add(first);
            }
            var loops = new List<List<Vector2>>();
            var visited = new bool[nodes.Count];
            for (int start = 0; start < nodes.Count; start++)
            {
                if (visited[start]) continue;
                var loop = new List<Vector2>();
                int previous = -1, current = start;
                do
                {
                    if (visited[current] || edges[current].Count != 2)
                        throw new InvalidOperationException("Lava/lake cross-section is not a closed contact loop.");
                    visited[current] = true;
                    loop.Add(nodes[current]);
                    int next = edges[current][0] == previous ? edges[current][1] : edges[current][0];
                    previous = current;
                    current = next;
                } while (current != start);
                if (loop.Count < 3) throw new InvalidOperationException("Degenerate lava/lake contact loop.");
                loops.Add(loop);
            }
            if (loops.Count != 2)
                throw new InvalidOperationException($"Expected two Lava/LavaLake contact faces, found {loops.Count}.");
            var lakeTriangles = lake.sharedMesh.triangles;
            var contacts = loops.Select(loop => OverlapAreaCentroid(loop, lakeWorld, lakeTriangles)).OrderBy(p => p.x).ToArray();
            if (Vector2.Distance(contacts[0], contacts[1]) < 0.5f)
                throw new InvalidOperationException("The two lake contact centroids are too close together.");
            Debug.Log($"[Volcano Lake Bake] Contact-area centers XZ: A={contacts[0]}, B={contacts[1]}, separation={Vector2.Distance(contacts[0], contacts[1]):F3}m.");
            return contacts;
        }

        static void AddPlaneHit(Vector3 a, Vector3 b, float y, List<Vector2> hits)
        {
            float da = a.y - y, db = b.y - y;
            if (da * db >= 0) return;
            float t = da / (da - db);
            hits.Add(new Vector2(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.z, b.z, t)));
        }

        static int GetContactNode(Vector2 position, List<Vector2> nodes, List<List<int>> edges,
            Dictionary<Vector2Int, int> lookup)
        {
            var key = new Vector2Int(Mathf.RoundToInt(position.x * 1000), Mathf.RoundToInt(position.y * 1000));
            if (lookup.TryGetValue(key, out int index)) return index;
            index = nodes.Count;
            nodes.Add(position);
            edges.Add(new List<int>(2));
            lookup.Add(key, index);
            return index;
        }

        static Vector2 OverlapAreaCentroid(List<Vector2> loop, Vector3[] lakeWorld, int[] lakeTriangles)
        {
            const int samplesPerEdge = 36;
            const int samplesPerTriangle = samplesPerEdge * (samplesPerEdge + 1) / 2;
            Vector2 sum = Vector2.zero;
            float area = 0;
            for (int i = 0; i < lakeTriangles.Length; i += 3)
            {
                Vector3 av = lakeWorld[lakeTriangles[i]], bv = lakeWorld[lakeTriangles[i + 1]], cv = lakeWorld[lakeTriangles[i + 2]];
                Vector2 a = new Vector2(av.x, av.z), b = new Vector2(bv.x, bv.z), c = new Vector2(cv.x, cv.z);
                float triangleArea = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
                float sampleArea = triangleArea / samplesPerTriangle;
                for (int u = 0; u < samplesPerEdge; u++)
                for (int v = 0; v < samplesPerEdge - u; v++)
                {
                    Vector2 point = a + (b - a) * ((u + 1f / 3f) / samplesPerEdge)
                                      + (c - a) * ((v + 1f / 3f) / samplesPerEdge);
                    if (!InsidePolygon(loop, point)) continue;
                    sum += point * sampleArea;
                    area += sampleArea;
                }
            }
            if (area < 0.005f) throw new InvalidOperationException("A Lava contact face does not overlap LavaLake.");
            Debug.Log($"[Volcano Lake Bake] Contact overlap area={area:F4} square metres.");
            return sum / area;
        }

        static bool InsidePolygon(List<Vector2> loop, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                Vector2 a = loop[i], b = loop[j];
                if ((a.y > p.y) != (b.y > p.y) &&
                    p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        static void SaveLakeField(Vector2 center, float span, Vector2 a, Vector2 b)
        {
            const int size = 512;
            float range = span * 0.7f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = center + new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f) * span;
                float da = Vector2.Distance(p, a), db = Vector2.Distance(p, b);
                Vector2 near = da <= db ? a : b;
                Vector2 direction = p - near;
                float angle = Mathf.Atan2(direction.y, direction.x) / (2 * Mathf.PI) + 0.5f;
                pixels[y * size + x] = new Color32(
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(da / range) * 255),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(db / range) * 255),
                    (byte)Mathf.RoundToInt(angle * 255), 255);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(LakeFieldPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(LakeFieldPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(LakeFieldPath);
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        static void SaveMesh(MeshFilter filter, Vector2[] uv, string name)
        {
            string path = Folder + "/" + name + ".asset";
            var copy = UnityEngine.Object.Instantiate(filter.sharedMesh);
            copy.name = name;
            copy.uv = uv;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(copy, existing);
                UnityEngine.Object.DestroyImmediate(copy);
                copy = existing;
                EditorUtility.SetDirty(copy);
            }
            else AssetDatabase.CreateAsset(copy, path);
            filter.sharedMesh = copy;
            EditorUtility.SetDirty(filter);
        }

        static void GenerateInk()
        {
            const int size = 512;
            var values = new byte[size * size];
            for (int i = 0; i < values.Length; i++) values[i] = 255;
            var rng = new System.Random(1729);
            for (int stroke = 0; stroke < 22; stroke++)
            {
                float x0 = 0.06f + (float)rng.NextDouble() * 0.88f;
                float start = (float)rng.NextDouble();
                float length = 0.11f + (float)rng.NextDouble() * 0.25f;
                float bend = ((float)rng.NextDouble() - 0.5f) * 0.11f;
                int radius = rng.Next(2, 5);
                byte darkness = (byte)rng.Next(95, 165);
                for (int k = 0; k < Mathf.CeilToInt(length * size); k++)
                {
                    float progress = k / (length * size);
                    float x = x0 + bend * Mathf.Sin(progress * 2.3f) + 0.012f * Mathf.Sin(progress * 8 + stroke);
                    int cx = Mathf.RoundToInt(x * size);
                    int cy = (Mathf.RoundToInt((start + progress * length) * size)) % size;
                    float taper = Mathf.Min(1, progress * 9, (1 - progress) * 9);
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int px = cx + dx;
                        if (px < 0 || px >= size) continue;
                        float coverage = Mathf.Clamp01((radius + 0.5f - Mathf.Abs(dx)) * taper);
                        byte value = (byte)Mathf.Lerp(255, darkness, coverage);
                        int index = cy * size + px;
                        values[index] = Math.Min(values[index], value);
                    }
                }
            }
            var pixels = new Color32[values.Length];
            for (int i = 0; i < values.Length; i++) pixels[i] = new Color32(values[i], values[i], values[i], 255);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(InkPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(InkPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(InkPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        static void AssignMaterial(MeshFilter filter, Shader shader, Texture2D ink, string name, bool lake)
        {
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(shader) { name = name }; AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.SetTexture("_FlowInk", ink);
            material.SetFloat("_LakeMode", lake ? 1 : 0);
            material.SetFloat("_FlowSpeed", lake ? 0.06f : 0.075f);
            material.SetFloat("_FlowTiling", lake ? 2 : 3.2f);
            material.SetFloat("_BrushStrength", lake ? 0.22f : 0.32f);
            material.SetFloat("_BandWobble", lake ? 0.02f : 0.035f);
            material.SetFloat("_CoreWidth", lake ? 0.4f : 0.25f);
            material.SetFloat("_EmissionStrength", 0.04f);
            if (lake)
            {
                var field = AssetDatabase.LoadAssetAtPath<Texture2D>(LakeFieldPath);
                if (!field) throw new InvalidOperationException("Baked dual-inlet lake field is missing.");
                material.SetTexture("_LakeFlowField", field);
            }
            EditorUtility.SetDirty(material);
            var renderer = filter.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            EditorUtility.SetDirty(renderer);
        }
    }
}
