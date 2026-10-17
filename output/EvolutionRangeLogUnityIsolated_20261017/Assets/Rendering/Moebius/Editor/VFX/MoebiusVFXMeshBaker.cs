using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Darwin.Rendering.Editor
{
    /// <summary>Author particle helper assets from explicit static surfaces. Never runs in a player.</summary>
    public sealed class MoebiusVFXMeshBaker : EditorWindow
    {
        [SerializeField] List<MeshFilter> sources = new List<MeshFilter>();
        [SerializeField] ParticleSystem particles;
        [SerializeField] MeshCollider collisionTarget;
        [SerializeField] float maximumSlope = 60, heightOffset = .05f;
        Vector2 scroll;

        [MenuItem("Tools/Moebius/VFX Mesh Baker")]
        public static void Open() => GetWindow<MoebiusVFXMeshBaker>("VFX Mesh Baker");

        void OnEnable() => minSize = new Vector2(440, 560);

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Source Surfaces", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select ground / rocks in the Hierarchy, then add them below. Children with MeshFilters are included. Remove water, foliage and other surfaces you do not want. Change this list between emission and collision bakes if needed. Source models and UVs are never modified.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Selected Meshes"))
                    foreach (var go in Selection.gameObjects)
                    {
                        if (EditorUtility.IsPersistent(go)) continue;
                        foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
                            if (filter.sharedMesh && !sources.Contains(filter)) sources.Add(filter);
                    }
                if (GUILayout.Button("Clear", GUILayout.Width(60))) sources.Clear();
            }
            for (int i = 0; i < sources.Count; i++)
                using (new EditorGUILayout.HorizontalScope())
                {
                    sources[i] = (MeshFilter)EditorGUILayout.ObjectField(sources[i], typeof(MeshFilter), true);
                    if (GUILayout.Button("Remove", GUILayout.Width(64))) { sources.RemoveAt(i--); }
                }
            EditorGUILayout.Space(10);
            particles = (ParticleSystem)EditorGUILayout.ObjectField("Particle System", particles, typeof(ParticleSystem), true);
            EditorGUILayout.LabelField("Emission Mesh", EditorStyles.boldLabel);
            maximumSlope = EditorGUILayout.Slider("Maximum Slope (Degrees)", maximumSlope, 0, 89.9f);
            heightOffset = EditorGUILayout.FloatField("Height Offset (Metres)", heightOffset);
            EditorGUILayout.HelpBox("Keeps upward-facing triangles and raises them vertically by Height Offset. Use a larger offset for airborne particles. Only the selected Particle System's Shape is changed to Mesh / Triangle; its shape transform is reset because placement is baked into the mesh. Box and Cone emitters do not need this operation.", MessageType.Info);
            using (new EditorGUI.DisabledScope(sources.Count == 0 || !particles || EditorApplication.isPlaying))
                if (GUILayout.Button("Generate and Assign Emission Mesh")) Bake(true);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Collision Mesh", EditorStyles.boldLabel);
            collisionTarget = (MeshCollider)EditorGUILayout.ObjectField("Target Mesh Collider", collisionTarget, typeof(MeshCollider), true);
            EditorGUILayout.HelpBox("Combines all source triangles, including walls and undersides. Drag an existing particle Ground Collision helper here, or add a MeshCollider to an empty child first. Only its mesh is replaced; gameplay layers and particle collision settings are preserved. This is not an automatic low-poly simplifier.", MessageType.Info);
            if (particles && collisionTarget)
            {
                var collision = particles.collision;
                bool compatible = collision.enabled && collision.type == ParticleSystemCollisionType.World &&
                    collision.mode == ParticleSystemCollisionMode.Collision3D &&
                    (collision.collidesWith.value & (1 << collisionTarget.gameObject.layer)) != 0 &&
                    collisionTarget.enabled && !collisionTarget.isTrigger;
                if (!compatible) EditorGUILayout.HelpBox("To collide with this helper, enable Particle System > Collision > World / 3D, include the helper's layer in Collides With, and enable the collider with Is Trigger off. Existing biome helpers normally use Ignore Raycast (layer 2).", MessageType.Warning);
            }
            using (new EditorGUI.DisabledScope(sources.Count == 0 || !collisionTarget || EditorApplication.isPlaying))
                if (GUILayout.Button("Generate and Assign Collision Mesh")) Bake(false);
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("Meshes are saved as .asset files. Choose the same standalone mesh asset to update it while retaining its GUID and references; all its users will see the update. Bindings and existing-mesh edits support Undo. Save the scene / prefab after baking. Bake again after changing source geometry or its position relative to the target. Existing authored biome exclusion regions are not inferred by this generic tool.", MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        void Bake(bool emission)
        {
            Mesh generated = null;
            try
            {
                var target = emission ? (Component)particles : collisionTarget;
                ValidateTarget(target);
                if (!emission) ValidateCollider(collisionTarget);
                var matrix = emission ? EmissionToWorld(particles) : collisionTarget.transform.localToWorldMatrix;
                generated = Build(sources, matrix, emission, maximumSlope, heightOffset);
                string suffix = emission ? "_Emission" : "_Collision";
                string suggested = target.name + suffix;
                foreach (char c in Path.GetInvalidFileNameChars()) suggested = suggested.Replace(c, '_');
                string path = EditorUtility.SaveFilePanelInProject("Save VFX Mesh", suggested, "asset", "Choose a new mesh asset, or an existing standalone mesh to update.");
                if (string.IsNullOrEmpty(path)) return;
                var output = AssetDatabase.LoadMainAssetAtPath(path);
                foreach (var source in sources)
                    if (source && source.sharedMesh == output)
                        throw new ArgumentException("The output cannot overwrite a selected source mesh. Choose a separate helper asset.");
                var saved = Save(generated, path);
                if (saved == generated) generated = null;
                if (emission) AssignEmission(particles, saved);
                else AssignCollision(collisionTarget, saved);
                AssetDatabase.SaveAssets();
                EditorGUIUtility.PingObject(saved);
                ShowNotification(new GUIContent($"Saved and assigned: {saved.triangles.Length / 3:N0} triangles"));
                SceneView.RepaintAll();
            }
            catch (Exception exception) { EditorUtility.DisplayDialog("VFX Mesh Baker", exception.Message, "OK"); }
            finally { if (generated) DestroyImmediate(generated); }
        }

        public static void ValidateTarget(Component target)
        {
            if (!target || EditorUtility.IsPersistent(target) || !target.gameObject.scene.IsValid())
                throw new ArgumentException("Choose a scene instance or open the prefab in Prefab Mode first.");
        }

        static void ValidateCollider(MeshCollider collider)
        {
            if (collider.convex) throw new ArgumentException("Use a non-convex MeshCollider for terrain collision. Convex cooking changes the terrain shape.");
            if (collider.attachedRigidbody && !collider.attachedRigidbody.isKinematic)
                throw new ArgumentException("A non-convex terrain collider cannot belong to a dynamic Rigidbody.");
        }

        // Shape scales spawn positions in the particle rotation basis, without hierarchy shear.
        // Local ignores ancestor scale; Hierarchy uses the full transform matrix.
        public static Matrix4x4 EmissionToWorld(ParticleSystem system)
        {
            var transform = system.transform;
            if (system.main.scalingMode == ParticleSystemScalingMode.Hierarchy) return transform.localToWorldMatrix;
            return Matrix4x4.TRS(transform.position, transform.rotation,
                system.main.scalingMode == ParticleSystemScalingMode.Local ? transform.localScale : transform.lossyScale);
        }

        public static Mesh Build(IEnumerable<MeshFilter> sources, Matrix4x4 targetToWorld, bool emission, float maximumSlope, float heightOffset)
        {
            if (Mathf.Abs(targetToWorld.determinant) < 1e-12f)
                throw new ArgumentException("The target transform has zero scale. Restore a non-zero scale before baking.");
            if (float.IsNaN(heightOffset) || float.IsInfinity(heightOffset) || float.IsNaN(maximumSlope) || float.IsInfinity(maximumSlope))
                throw new ArgumentException("Height Offset and Maximum Slope must be finite values.");
            var inverse = targetToWorld.inverse;
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var visited = new HashSet<MeshFilter>();
            float minimumUp = Mathf.Cos(Mathf.Clamp(maximumSlope, 0, 89.9f) * Mathf.Deg2Rad);
            foreach (var filter in sources)
            {
                if (!filter || !filter.sharedMesh || !visited.Add(filter)) continue;
                var sourceToWorld = filter.transform.localToWorldMatrix;
                if (Mathf.Abs(sourceToWorld.determinant) < 1e-12f) continue;
                bool sourceMirrored = sourceToWorld.determinant < 0;
                bool targetMirrored = targetToWorld.determinant < 0;
                // Editor-only readback also supports imported models with Read/Write disabled.
                using (var array = MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
                using (var positions = new NativeArray<Vector3>(array[0].vertexCount, Allocator.Temp))
                {
                    var data = array[0]; data.GetVertices(positions);
                    var remap = new Dictionary<int, int>();
                    for (int slot = 0; slot < data.subMeshCount; slot++)
                    {
                        var submesh = data.GetSubMesh(slot);
                        if (submesh.topology != MeshTopology.Triangles) continue;
                        using (var triangles = new NativeArray<int>(submesh.indexCount, Allocator.Temp))
                        {
                            data.GetIndices(triangles, slot, true);
                            for (int i = 0; i < triangles.Length; i += 3)
                            {
                                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                                if (sourceMirrored) (b, c) = (c, b);
                                var wa = sourceToWorld.MultiplyPoint3x4(positions[a]);
                                var wb = sourceToWorld.MultiplyPoint3x4(positions[b]);
                                var wc = sourceToWorld.MultiplyPoint3x4(positions[c]);
                                var normal = Vector3.Cross(wb - wa, wc - wa);
                                if (normal.sqrMagnitude < 1e-16f || (emission && normal.normalized.y < minimumUp)) continue;
                                if (targetMirrored) (b, c) = (c, b);
                                foreach (int index in new[] { a, b, c })
                                {
                                    if (!remap.TryGetValue(index, out int mapped))
                                    {
                                        mapped = vertices.Count; remap.Add(index, mapped);
                                        var world = sourceToWorld.MultiplyPoint3x4(positions[index]);
                                        if (emission) world.y += heightOffset;
                                        vertices.Add(inverse.MultiplyPoint3x4(world));
                                    }
                                    indices.Add(mapped);
                                }
                            }
                        }
                    }
                }
            }
            if (indices.Count == 0) throw new ArgumentException(emission
                ? "No upward-facing triangles match Maximum Slope. Select ground surfaces or increase the slope limit."
                : "No usable triangles were found in the source surfaces.");
            var mesh = new Mesh { name = emission ? "VFX Emission" : "VFX Collision", indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh Save(Mesh generated, string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Save to a standalone .asset file inside Assets.");
            var existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing)
            {
                if (!(existing is Mesh mesh) || AssetDatabase.IsSubAsset(mesh) || AssetDatabase.LoadAllAssetsAtPath(path).Length != 1)
                    throw new ArgumentException("Only a standalone Mesh asset can be updated. Do not overwrite a source model or another asset type.");
                Undo.RegisterCompleteObjectUndo(mesh, "Update VFX mesh");
                string oldName = mesh.name;
                EditorUtility.CopySerialized(generated, mesh); mesh.name = oldName;
                EditorUtility.SetDirty(mesh);
                return mesh;
            }
            generated.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(generated, path);
            return generated;
        }

        public static void AssignEmission(ParticleSystem target, Mesh mesh)
        {
            ValidateTarget(target);
            Undo.RecordObject(target, "Assign VFX emission mesh");
            var shape = target.shape;
            shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Mesh; shape.mesh = mesh;
            shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
            shape.useMeshMaterialIndex = false; shape.useMeshColors = false; shape.normalOffset = 0;
            shape.position = shape.rotation = Vector3.zero; shape.scale = Vector3.one;
            Changed(target);
        }

        public static void AssignCollision(MeshCollider target, Mesh mesh)
        {
            ValidateTarget(target); ValidateCollider(target);
            Undo.RecordObject(target, "Assign VFX collision mesh");
            // Null first forces recooking when updating the existing mesh asset in place.
            target.sharedMesh = null; target.sharedMesh = mesh;
            Changed(target);
        }

        static void Changed(Component target)
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            EditorUtility.SetDirty(target);
            EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
        }
    }
}
