using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Darwin.Rendering;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Darwin.Rendering.Validation
{
    // Offline only: inspect existing scenes without regenerating or saving authored assets.
    public static class MoebiusRiverSeamValidation
    {
        const string Root = "Assets/Rendering/Moebius/Samples/RiverConnections/";
        const string Output = "Validation/RiverSeams/";
        static readonly List<string> Notes = new List<string>();
        public static void Before() => Run(false);
        public static void After() => Run(true);
        static void Check(bool pass, string message)
        {
            if (!pass) throw new Exception(message);
            Notes.Add("PASS: " + message);
        }
        static void Run(bool final)
        {
            try
            {
                if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/Temp/") || !File.Exists("RiverConnections.marker"))
                    throw new Exception("Use the marked isolated fixture.");
                Directory.CreateDirectory(Output); ShaderUtil.allowAsyncCompilation = false;
                var pipeline = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;
                string prefix = final ? "After" : "Before";
                EditorSceneManager.OpenScene(Root + "Scenes/01_Connected_Rivers.unity");
                var network = Object.FindFirstObjectByType<MoebiusRiverNetwork>();
                Check(network.RebuildNow(), "Network builds: " + network.LastError);
                Check(network.JoinedCount == 1, "Original equal-height join remains connected");
                foreach (var tile in network.GetComponentsInChildren<MoebiusRiverTile>())
                {
                    if (!tile.IsRiver) continue;
                    var water = network.GeneratedRoot.GetComponentsInChildren<MeshFilter>().First(f => f.name == "Connected Water" && (f.transform.position-tile.transform.position).sqrMagnitude < .0001f);
                    Notes.Add(tile.name + " position=" + tile.transform.position.ToString("F5") + " water=" + tile.WorldWaterHeight.ToString("F6"));
                    foreach (var port in tile.profile.ports)
                    {
                        var used = new HashSet<int>(water.sharedMesh.triangles);
                        var edge = used.Select(i => water.sharedMesh.vertices[i]).Where(p => Mathf.Abs(Vector3.Dot(p-port.Center, port.Normal)) < .006f)
                            .Select(p => water.transform.TransformPoint(p)).Distinct().OrderBy(p => p.z).ThenBy(p => p.x).ToArray();
                        Notes.Add("  " + port.side + " boundary (" + edge.Length + "): " + string.Join("; ", edge.Select(p => p.ToString("F5"))));
                    }
                }
                Capture(network, prefix + "_CapsuleSide", new Vector3(1,.55f,-7.0f), new Vector3(7,3.5f,-9), 4.8f);
                Capture(network, prefix + "_JoinCloseup", new Vector3(-8.5f,.25f,0), new Vector3(5,6,-8), 4.0f);
                Capture(network, prefix + "_JoinTop", new Vector3(-8.5f,.25f,0), new Vector3(.1f,12,-.2f), 3.7f);
                if (final)
                {
                    ValidateClosedBanks(network);
                    CheckFoundation(network);
                    CheckJoins(network);
                    foreach (string scene in new[]{"02_River_To_Land_Capsule", "03_Height_Difference_Waterfall", "04_Multiple_Water_Heights"})
                    {
                        EditorSceneManager.OpenScene(Root+"Scenes/"+scene+".unity");
                        network=Object.FindFirstObjectByType<MoebiusRiverNetwork>();
                        Check(network.RebuildNow(),scene+": network rebuilds");
                        Check(network.WaterfallCount==(scene.StartsWith("03")?1:scene.StartsWith("04")?3:0),scene+": expected waterfall count");
                        ValidateClosedBanks(network); CheckFoundation(network);
                        var camera=Camera.main;
                        Capture(network,"After_"+scene,camera.transform.position+camera.transform.forward*20,-camera.transform.forward*20,camera.orthographicSize);
                    }
                }
                File.WriteAllLines(Output + prefix + "Report.txt", Notes);
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Notes.Add(e.ToString()); Directory.CreateDirectory(Output); File.WriteAllLines(Output + "Failure.txt", Notes); Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void ValidateClosedBanks(MoebiusRiverNetwork network)
        {
            foreach (var filter in network.GeneratedRoot.GetComponentsInChildren<MeshFilter>().Where(f => f.name.StartsWith("Capsule Bank")))
            {
                var mesh = filter.sharedMesh;
                var points = mesh.vertices;
                var ids = new Dictionary<Vector3Int, int>();
                var remap = new int[points.Length];
                for (int i=0;i<points.Length;i++)
                {
                    var key = Vector3Int.RoundToInt(points[i] * 10000);
                    if (!ids.TryGetValue(key,out int id)) { id=ids.Count; ids.Add(key,id); }
                    remap[i]=id;
                }
                var edges = new Dictionary<(int,int),int>();
                var winding = new Dictionary<(int,int),int>();
                bool nondegenerate=true;
                foreach (int slot in Enumerable.Range(0,mesh.subMeshCount))
                {
                    var t=mesh.GetTriangles(slot);
                    for (int i=0;i<t.Length;i+=3)
                    {
                        int a=remap[t[i]], b=remap[t[i+1]], c=remap[t[i+2]];
                        nondegenerate &= Vector3.Cross(points[t[i+1]]-points[t[i]],points[t[i+2]]-points[t[i]]).sqrMagnitude>1e-16f;
                        foreach (var e in new[]{(a,b),(b,c),(c,a)})
                        {
                            var key=e.Item1<e.Item2?e:(e.Item2,e.Item1);
                            edges.TryGetValue(key,out int count); edges[key]=count+1;
                            winding.TryGetValue(key,out int sign); winding[key]=sign+(e.Item1<e.Item2?1:-1);
                        }
                    }
                }
                Check(nondegenerate,filter.name+": no degenerate triangles");
                Check(edges.Values.All(count=>count==2), filter.name+": watertight after positional welding; every edge has two incident faces");
                Check(winding.Values.All(sign=>sign==0), filter.name+": adjacent faces have consistent outward winding");
                double volume=0;
                var triangles=mesh.triangles;
                for(int i=0;i<triangles.Length;i+=3) volume+=Vector3.Dot(points[triangles[i]],Vector3.Cross(points[triangles[i+1]],points[triangles[i+2]]))/6.0;
                Check(volume>0,filter.name+": positive enclosed volume "+volume);
            }
        }
        static void CheckJoins(MoebiusRiverNetwork network)
        {
            var tiles=network.GetComponentsInChildren<MoebiusRiverTile>();
            var l=tiles.First(t=>t.name=="River L"); var cross=tiles.First(t=>t.name=="River Cross");
            var original=cross.transform.localPosition;
            ValidateSharedEdge(network,l,cross,"Exact grid");
            cross.transform.localPosition=original+new Vector3(-.04f,.02f,.01f);
            Check(network.RebuildNow()&&network.JoinedCount==1,"Sub-tolerance placement and height offsets remain connected");
            ValidateSharedEdge(network,l,cross,"Position and height tolerance");
            Capture(network,"After_ToleranceJoin",new Vector3(-8.52f,.26f,0),new Vector3(5,6,-8),4);
            var rotation=network.transform.rotation; network.transform.rotation=Quaternion.Euler(0,90,0)*rotation;
            Check(network.RebuildNow()&&network.JoinedCount==1,"Rotated network keeps the join");
            ValidateSharedEdge(network,l,cross,"90 degree network rotation");
            network.transform.rotation=rotation;cross.transform.localPosition=original;
            cross.waterLevel+=.02f;
            Check(network.RebuildNow()&&network.JoinedCount==1,"Independent water levels within tolerance join");
            ValidateSharedEdge(network,l,cross,"Independent water level");
            cross.waterLevel-=.02f;cross.transform.localPosition=original+Vector3.up*3;
            Check(network.RebuildNow()&&network.WaterfallCount==1,"Larger elevation difference still generates a waterfall");
            cross.transform.localPosition=original;
            Check(network.RebuildNow()&&network.JoinedCount==1&&network.WaterfallCount==0,"Returning terrain restores a seamless level join");
            int rebuilds=network.RebuildCount;
            network.SendMessage("LateUpdate");
            Check(network.RebuildCount==rebuilds,"Stationary network does not rebuild");
        }
        static void ValidateSharedEdge(MoebiusRiverNetwork network,MoebiusRiverTile l,MoebiusRiverTile cross,string label)
        {
            var a=l.profile.ports.First(p=>p.side==RiverSide.West);var b=cross.profile.ports.First(p=>p.side==RiverSide.East);
            var center=(l.transform.TransformPoint(a.Center)+cross.transform.TransformPoint(b.Center))*.5f;
            center.y=(l.WorldWaterHeight+cross.WorldWaterHeight)*.5f;
            var normal=l.transform.TransformDirection(a.Normal);
            var edges=new List<Vector3[]>();
            foreach(var tile in new[]{l,cross})
            {
                var filter=network.GeneratedRoot.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Connected Water"&&(f.transform.position-tile.transform.position).sqrMagnitude<.0001f);
                var mesh=filter.sharedMesh;var v=mesh.vertices;
                var edge=mesh.triangles.Distinct().Select(i=>filter.transform.TransformPoint(v[i]))
                    .Where(p=>Mathf.Abs(Vector3.Dot(p-center,normal))<.00005f).Distinct().ToArray();
                Check(edge.Length>=2,label+": boundary vertices found for "+tile.name);
                Check(edge.All(p=>Mathf.Abs(p.y-center.y)<.00001f),label+": shared mean height for "+tile.name);
                var port=tile==l?a:b;var material=filter.GetComponent<MeshRenderer>().sharedMaterial;
                Check(material.GetVector("_RiverWaveFadeWidths")[(int)port.side]>.1f,label+": GPU displacement attenuates at join for "+tile.name);
                edges.Add(edge);
            }
            float error=edges[0].Max(p=>edges[1].Min(q=>Vector3.Distance(p,q)));
            error=Mathf.Max(error,edges[1].Max(p=>edges[0].Min(q=>Vector3.Distance(p,q))));
            Check(error<.00002f,label+": opposing boundary vertices coincide; max error="+error);
        }
        static void CheckFoundation(MoebiusRiverNetwork network)
        {
            foreach(var tile in network.GetComponentsInChildren<MoebiusRiverTile>().Where(t=>t.IsRiver))
            {
                var bank=tile.originalWater.transform.parent.GetComponent<MeshFilter>();
                using(var data=MeshUtility.AcquireReadOnlyMeshData(bank.sharedMesh))
                using(var points=new NativeArray<Vector3>(data[0].vertexCount,Allocator.Temp))
                {
                    data[0].GetVertices(points);
                    float half=tile.profile.tileSize*.5f,highest=float.NegativeInfinity;
                    foreach(var p in points)
                    {
                        var local=tile.transform.InverseTransformPoint(bank.transform.TransformPoint(p));
                        if(Mathf.Abs(Mathf.Abs(local.x)-half)<.005f||Mathf.Abs(Mathf.Abs(local.z)-half)<.005f) highest=Mathf.Max(highest,local.y);
                    }
                    Check(tile.foundationTop>=highest-.0001f,tile.name+": foundation walls meet model perimeter; perimeter="+highest+", wall="+tile.foundationTop);
                }
                var foundation=network.GeneratedRoot.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Terrain Foundation"&&(f.transform.position-tile.transform.position).sqrMagnitude<.0001f);
                Check(!foundation.sharedMesh.normals.Any(n=>n.y>.5f),tile.name+": no duplicate foundation roof to reintroduce Z-fighting");
            }
        }
        static void Capture(MoebiusRiverNetwork network,string name,Vector3 target,Vector3 offset,float size)
        {
            var camera=Camera.main;
            camera.transform.SetPositionAndRotation(target+offset,Quaternion.LookRotation(-offset));
            camera.orthographic=true;camera.orthographicSize=size;camera.nearClipPlane=.1f;camera.farClipPlane=300;
            camera.GetUniversalAdditionalCameraData().SetRenderer(1);
            foreach(var renderer in network.GeneratedRoot.GetComponentsInChildren<MeshRenderer>())
            {
                var m=renderer.sharedMaterial;
                foreach(string property in new[]{"_WaveSpeed","_FlowSpeed","_FoamSpeed","_RippleSpeed","_RippleWobbleSpeed"}) if(m.HasProperty(property)) m.SetFloat(property,0);
            }
            var previous=camera.targetTexture;var active=RenderTexture.active;
            var rt=RenderTexture.GetTemporary(1400,1000,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var texture=new Texture2D(1400,1000,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,1400,1000),0,0);texture.Apply();
            File.WriteAllBytes(Output+name+".png",texture.EncodeToPNG());Object.DestroyImmediate(texture);
            camera.targetTexture=previous;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);
        }
    }
}
