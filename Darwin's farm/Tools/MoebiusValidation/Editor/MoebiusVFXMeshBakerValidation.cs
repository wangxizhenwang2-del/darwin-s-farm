using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Darwin.Rendering.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Darwin.Rendering.Validation
{
    public static class MoebiusVFXMeshBakerValidation
    {
        static readonly List<string> notes = new List<string>();
        static void Check(bool pass, string label) { if (!pass) throw new Exception(label); notes.Add("PASS: " + label); }
        public static void Run()
        {
            const string folder = "Assets/VFXMeshBakerTests";
            try
            {
                if (!Application.dataPath.Replace('\\', '/').Contains("/Temp/") || !File.Exists("VFXMeshBaker.marker")) throw new Exception("Use the isolated fixture.");
                Directory.CreateDirectory("Validation/VFXMeshBaker");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "VFXMeshBakerTests");
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "Ground"; ground.transform.position = new Vector3(11, 2, -3);
                ground.transform.localScale = new Vector3(-6, 2, 4);
                var source = ground.GetComponent<MeshFilter>();
                var parent = new GameObject("Scaled Parent");
                parent.transform.SetPositionAndRotation(new Vector3(2, 3, 5), Quaternion.Euler(0, 27, 0));
                parent.transform.localScale = new Vector3(2, 3, .5f);
                var emitter = new GameObject("Test Emitter"); emitter.transform.SetParent(parent.transform, false);
                emitter.transform.localPosition = new Vector3(1, -2, 1);
                emitter.transform.localRotation = Quaternion.Euler(0, 18, 0);
                emitter.transform.localScale = new Vector3(.75f, 1.2f, .8f);
                var system = emitter.AddComponent<ParticleSystem>(); system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = 10; main.startSpeed = 0; main.maxParticles = 2000; main.playOnAwake = false;
                var emission = system.emission; emission.enabled = false;
                foreach (ParticleSystemScalingMode mode in Enum.GetValues(typeof(ParticleSystemScalingMode)))
                {
                    main.scalingMode = mode;
                    var mapping = MoebiusVFXMeshBaker.EmissionToWorld(system);
                    var mesh = MoebiusVFXMeshBaker.Build(new[] { source, source }, mapping, true, 60, .7f);
                    Check(mesh.triangles.Length == 6, mode + ": mirrored source / upward filter / duplicate source removal");
                    foreach (var v in mesh.vertices)
                        Check(Mathf.Abs(mapping.MultiplyPoint3x4(v).y - 3.7f) < .0001f, mode + ": metre height offset / coordinate transform");
                    var shape = system.shape; shape.position = new Vector3(8, 9, 10); shape.scale = Vector3.one * 7;
                    MoebiusVFXMeshBaker.AssignEmission(system, mesh);
                    Check(system.shape.position == Vector3.zero && system.shape.scale == Vector3.one && main.scalingMode == mode, mode + ": shape binding without changing scaling mode");
                    system.Clear(); system.Emit(200);
                    var particles = new ParticleSystem.Particle[2000]; int count = system.GetParticles(particles);
                    Check(count == 200, mode + ": native particles spawned");
                    float worst = 0;
                    for (int i = 0; i < count; i++)
                    {
                        var p = particles[i].position;
                        worst = Mathf.Max(worst, Mathf.Abs(p.y - 3.7f));
                        if (Mathf.Abs(p.y - 3.7f) > .002f || p.x < 7.99f || p.x > 14.01f || p.z < -5.01f || p.z > -0.99f)
                            throw new Exception(mode + ": native spawn outside baked source: " + p + ", y error " + worst);
                    }
                    notes.Add("PASS: " + mode + ": actual native particle positions match world-space source, worst height error " + worst);
                    system.Clear(); shape = system.shape; shape.mesh = null; Object.DestroyImmediate(mesh);
                }
                var colliderObject = new GameObject("Ground Collision");
                colliderObject.transform.SetPositionAndRotation(new Vector3(7, -2, 1), Quaternion.Euler(0, 48, 0));
                colliderObject.transform.localScale = new Vector3(-.7f, 2, 1.3f); colliderObject.layer = 2;
                var collider = colliderObject.AddComponent<MeshCollider>();
                var collisionMesh = MoebiusVFXMeshBaker.Build(new[] { source }, colliderObject.transform.localToWorldMatrix, false, 0, 999);
                Check(collisionMesh.triangles.Length == 36, "Collision retains all source triangles and ignores emission offset / slope");
                var saved = MoebiusVFXMeshBaker.Save(collisionMesh, folder + "/Collision.asset");
                string guid = AssetDatabase.AssetPathToGUID(folder + "/Collision.asset");
                MoebiusVFXMeshBaker.AssignCollision(collider, saved); Physics.SyncTransforms();
                Check(collider.Raycast(new Ray(new Vector3(11, 20, -3), Vector3.down), out var hit, 40) && Mathf.Abs(hit.point.y - 3) < .001f, "Native collider raycast matches transformed / mirrored ground");
                Check(colliderObject.layer == 2 && !collider.convex, "Collision binding preserves layer and non-convex settings");

                Undo.IncrementCurrentGroup();
                var replacement = MoebiusVFXMeshBaker.Build(new[] { source }, Matrix4x4.identity, true, 60, 0);
                MoebiusVFXMeshBaker.Save(replacement, folder + "/Collision.asset"); Undo.FlushUndoRecordObjects();
                Check(guid == AssetDatabase.AssetPathToGUID(folder + "/Collision.asset") && saved.triangles.Length == 6, "Updating asset preserves GUID and existing references");
                Undo.PerformUndo(); Check(saved.triangles.Length == 36, "Existing mesh geometry supports Undo");
                Object.DestroyImmediate(replacement);
                Undo.IncrementCurrentGroup();
                var priorShape = system.shape; priorShape.shapeType = ParticleSystemShapeType.Box;
                Undo.FlushUndoRecordObjects();
                MoebiusVFXMeshBaker.AssignEmission(system, saved); Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check(system.shape.shapeType == ParticleSystemShapeType.Box, "Particle binding supports Undo");
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(collider, "Clear test collider"); collider.sharedMesh = null; Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                MoebiusVFXMeshBaker.AssignCollision(collider, saved); Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check(!collider.sharedMesh, "Collider binding supports Undo");

                Mesh unreadable = null;
                foreach (string modelGuid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Art" }))
                {
                    unreadable = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(modelGuid)).OfType<Mesh>().FirstOrDefault(m => !m.isReadable && m.vertexCount > 0);
                    if (unreadable) break;
                }
                if (!unreadable)
                {
                    string modelPath = AssetDatabase.FindAssets("t:Mesh", new[] { "Assets/Art" })
                        .Select(AssetDatabase.GUIDToAssetPath).First(p => AssetImporter.GetAtPath(p) is ModelImporter);
                    string copyPath = folder + "/UnreadableModel" + Path.GetExtension(modelPath);
                    if (!AssetDatabase.LoadMainAssetAtPath(copyPath)) Check(AssetDatabase.CopyAsset(modelPath, copyPath), "Copy model into isolated test folder");
                    var importer = (ModelImporter)AssetImporter.GetAtPath(copyPath);
                    importer.isReadable = false; importer.SaveAndReimport();
                    unreadable = AssetDatabase.LoadAllAssetsAtPath(copyPath).OfType<Mesh>().First(m => !m.isReadable && m.vertexCount > 0);
                }
                Check(unreadable, "Found a real imported mesh with Read/Write disabled (test copy if needed)");
                var importedObject = new GameObject("Unreadable Model"); var importedFilter = importedObject.AddComponent<MeshFilter>(); importedFilter.sharedMesh = unreadable;
                var importedResult = MoebiusVFXMeshBaker.Build(new[] { importedFilter }, Matrix4x4.identity, false, 60, 0);
                Check(importedResult.triangles.Length > 0 && !unreadable.isReadable, "Imported unreadable mesh baked without modifying importer");
                Object.DestroyImmediate(importedResult);

                var dense = new Mesh { indexFormat = IndexFormat.UInt32 };
                var denseVertices = new Vector3[66000]; var denseIndices = new int[66000];
                for (int i = 0; i < denseVertices.Length; i += 3)
                {
                    denseVertices[i] = new Vector3(i, 0, 0); denseVertices[i + 1] = new Vector3(i, 0, 1); denseVertices[i + 2] = new Vector3(i + 1, 0, 0);
                    denseIndices[i] = i; denseIndices[i + 1] = i + 1; denseIndices[i + 2] = i + 2;
                }
                dense.vertices = denseVertices; dense.triangles = denseIndices; importedFilter.sharedMesh = dense;
                var denseResult = MoebiusVFXMeshBaker.Build(new[] { importedFilter }, Matrix4x4.identity, true, 60, 0);
                Check(denseResult.indexFormat == IndexFormat.UInt32 && denseResult.vertexCount == 66000, "Meshes above 65,535 vertices use 32-bit indices");
                Object.DestroyImmediate(denseResult); Object.DestroyImmediate(dense);
                bool rejected = false;
                try { MoebiusVFXMeshBaker.Build(new[] { source }, Matrix4x4.Scale(Vector3.zero), true, 60, 0); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "Zero-scale target is rejected");
                rejected = false;
                try { MoebiusVFXMeshBaker.Save(saved, folder + "/Wrong.fbx"); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "Imported source model overwrite is rejected");
                Check(EditorSceneManager.GetActiveScene().isDirty, "Target assignments mark scene dirty");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rendering/Moebius/Content/VFX/Grassland/Prefabs/GrasslandVFX.prefab");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var prefabParticles = instance.GetComponentInChildren<ParticleSystem>(true);
                var prefabCollider = instance.GetComponentInChildren<MeshCollider>(true);
                var prefabEmission = MoebiusVFXMeshBaker.Build(new[] { source }, MoebiusVFXMeshBaker.EmissionToWorld(prefabParticles), true, 60, .2f);
                var savedEmission = MoebiusVFXMeshBaker.Save(prefabEmission, folder + "/PrefabEmission.asset");
                MoebiusVFXMeshBaker.AssignEmission(prefabParticles, savedEmission);
                MoebiusVFXMeshBaker.AssignCollision(prefabCollider, saved);
                var modifications = PrefabUtility.GetPropertyModifications(instance);
                Check(modifications.Any(p => p.objectReference == savedEmission) && modifications.Any(p => p.objectReference == saved), "Existing Grassland VFX prefab instance records both mesh overrides");
                Check(EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), folder + "/BakerTest.unity"), "Save isolated test scene");
                EditorSceneManager.OpenScene(folder + "/BakerTest.unity");
                var loadedPrefab = Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(p => p.shape.mesh == savedEmission);
                Check(loadedPrefab && Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(c => c.sharedMesh == saved), "Prefab mesh bindings survive scene save / reload");
                var menu = TypeCache.GetMethodsWithAttribute<MenuItem>().SelectMany(m => m.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>());
                Check(menu.Count(m => m.menuItem == "Tools/Moebius/VFX Mesh Baker" && !m.validate) == 1, "One grouped Moebius menu entry");
                Check(EditorApplication.ExecuteMenuItem("Tools/Moebius/VFX Mesh Baker"), "Editor window opens from the registered menu");
                EditorWindow.GetWindow<MoebiusVFXMeshBaker>().Close();
                AssetDatabase.SaveAssets();
                notes.Add("Errors: 0");
                File.WriteAllLines("Validation/VFXMeshBaker/Report.txt", notes); EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Directory.CreateDirectory("Validation/VFXMeshBaker"); notes.Add(e.ToString());
                File.WriteAllLines("Validation/VFXMeshBaker/Report.txt", notes); Debug.LogException(e); EditorApplication.Exit(1);
            }
        }
    }
}
