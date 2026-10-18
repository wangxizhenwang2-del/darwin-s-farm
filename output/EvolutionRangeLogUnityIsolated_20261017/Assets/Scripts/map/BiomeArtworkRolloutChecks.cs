#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Runs against real WhiteBox terrain, colliders, artwork and the living representative.
// Every fixture is confined to a disposable Play session.
public sealed class BiomeArtworkRolloutChecks : MonoBehaviour
{
    public static string Result { get; private set; } = "Not run";
    public static readonly string Output = Path.GetFullPath(".utmp/biome-rollout");
    private MapGridManager grid;
    private MapTileColorTransitionSystem visuals;
    private SimulationEnvironmentController environment;
    private PopulationMovementController movement;
    private WhiteboxPopulation population;
    private MapTileInstance origin;
    private string identity;
    private string ecologyId;
    private int count;
    private int transitionFrames;
    private readonly List<string> passed = new List<string>();
    private readonly List<MapTileInstance> fixtures = new List<MapTileInstance>();

    private struct Profile
    {
        public BiomeType biome;
        public string asset;
        public int temperature, humidity, elevation;
        public Profile(BiomeType b, string a, int t, int h, int e)
        { biome = b; asset = a; temperature = t; humidity = h; elevation = e; }
    }
    private static readonly Profile[] Profiles =
    {
        new Profile(BiomeType.Desert,"荒漠",55,35,0),
        new Profile(BiomeType.Grassland,"草原",55,55,0),
        new Profile(BiomeType.Forest,"森林",35,75,0),
        new Profile(BiomeType.Rainforest,"雨林",55,75,0),
        new Profile(BiomeType.Tundra,"苔原",30,30,1),
        new Profile(BiomeType.Highland,"高山",50,30,1),
        new Profile(BiomeType.SnowMountain,"雪山",25,45,2),
        new Profile(BiomeType.Volcano,"火山",65,45,2)
    };

    public static void Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter WhiteBox Play mode first.");
        if (FindFirstObjectByType<BiomeArtworkRolloutChecks>() == null)
            new GameObject("BiomeArtworkRolloutChecks").AddComponent<BiomeArtworkRolloutChecks>();
    }

    private static void Require(bool condition, string message)
    {
        if (condition) return;
        Result = "FAILED: " + message;
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output,"failure.txt"),Result);
        throw new InvalidOperationException(Result);
    }

    private IEnumerator Start()
    {
        Directory.CreateDirectory(Output);
        Result = "Running: setup";
        grid = FindFirstObjectByType<MapGridManager>();
        environment = grid.GetComponent<SimulationEnvironmentController>();
        visuals = grid.GetComponent<MapTileColorTransitionSystem>();
        movement = grid.GetComponent<PopulationMovementController>();
        grid.GetComponent<SimulationTime>().Pause();
        Require(grid.TryGetTile(Vector2Int.zero,out origin),"Missing origin.");
        yield return Ready();
        float spawnDeadline = Time.realtimeSinceStartup + 15f;
        while (movement.Populations.Count == 0 && Time.realtimeSinceStartup < spawnDeadline) yield return null;
        Require(movement.Populations.Count > 0,"Initial representative did not spawn.");
        population = movement.Populations[0]; identity = population.UniqueId;
        ecologyId = population.EcologicalPopulation.PopulationId; count = population.Count;
        fixtures.Add(origin);
        Vector2Int[] positions = {new Vector2Int(1,0),new Vector2Int(2,0),new Vector2Int(3,0),
            new Vector2Int(3,1),new Vector2Int(2,1),new Vector2Int(1,1),new Vector2Int(0,1)};
        for (int i=1;i<Profiles.Length;i++)
        {
            var definition = AssetDatabase.LoadAssetAtPath<MapTileDefinition>("Assets/Scripts/map/data/"+Profiles[i].asset+".asset");
            Require(grid.TryPlaceTile(positions[i-1],definition,i%4),"Could not place "+Profiles[i].biome);
            grid.TryGetTile(positions[i-1],out MapTileInstance tile); fixtures.Add(tile); Edit(tile,Profiles[i]);
        }
        yield return Ready();

        for (int rotation=0;rotation<4;rotation++)
        {
            Result = "Running: all biomes rotation "+rotation;
            foreach (MapTileInstance tile in fixtures)
            {
                tile.Initialize(tile.Coordinate,tile.Definition,tile.HeightLevel,rotation);
                tile.transform.rotation = Quaternion.Euler(0,rotation*90,0);
            }
            Refresh(); grid.Navigation.RequestUpdate();
            yield return Ready();
            foreach (MapTileInstance tile in fixtures) CheckArtwork(tile);
            CheckAllSeams();
        }
        Pass("32 placed biome/rotation combinations: source materials, UV coverage, shared edge colors/heights, collision ownership and build previews.");

        for (int i=0;i<Profiles.Length;i++)
        {
            Profile profile = Profiles[i];
            Result = "Running: "+profile.biome+" transition and movement";
            yield return Change(profile);
            yield return Recover();
            CheckArtwork(origin);
            Type regression = AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("PopulationTerrainRegressionChecks"))
                .First(t=>t!=null);
            string simulation = (string)regression.GetMethod("RunChecks").Invoke(null,null);
            Pass(profile.biome+": "+simulation.Replace("\n"," "));
            float end=Time.realtimeSinceStartup+5f, travelled=0;
            Vector3 previous=population.Leader.position;
            while(Time.realtimeSinceStartup<end)
            {
                yield return new WaitForSecondsRealtime(.1f);
                CheckIdentity();
                Require(population.ValidateMovement(out string error),profile.biome+": "+error);
                Vector3 current=population.Leader.position;
                travelled+=Vector2.Distance(new Vector2(previous.x,previous.z),new Vector2(current.x,current.z)); previous=current;
            }
            Require(travelled>.5f,profile.biome+": live representative stalled.");
            Pass(profile.biome+": live movement "+travelled.ToString("F2")+"m / 5 seconds, identity/count preserved.");
            Capture(profile.biome.ToString());
        }

        // Exercise every ordered pair; ground materials must never switch at full strength.
        for (int from=0;from<Profiles.Length;from++)
        {
            yield return Change(Profiles[from]);
            for (int to=0;to<Profiles.Length;to++)
            {
                if(to==from)continue;
                Result="Running: pair "+Profiles[from].biome+" -> "+Profiles[to].biome;
                yield return Change(Profiles[to]);
                yield return Change(Profiles[from]);
                CheckIdentity();
            }
        }
        Pass("All 56 ordered biome pairs (112 forward/return changes): retained outgoing material, faded material swaps, final cleanup, stable navigation after settlement.");

        Result="Running: rapid interruptions";
        for(int i=0;i<16;i++) {Edit(origin,Profiles[i%8]);yield return new WaitForSecondsRealtime(.14f);}
        yield return Ready();
        Require(!origin.GetComponentsInChildren<Transform>().Any(t=>t.name.StartsWith("RetiringBiomeVisual_")),
            "Interrupted changes leaked retiring decorations.");
        yield return Recover(); CheckIdentity();
        Pass("16 interrupted biome changes: no retired visuals/probes, stable identity and recovery.");

        Require(grid.TryPlaceTile(new Vector2Int(4,0),AssetDatabase.LoadAssetAtPath<MapTileDefinition>(
            "Assets/Scripts/map/data/湖泊.asset")),"Could not place lake fixture.");
        yield return Ready();
        grid.TryGetTile(new Vector2Int(4,0),out MapTileInstance water);
        Require(!water.GetComponentsInChildren<Transform>().Any(t=>t.name.StartsWith("BiomeVisual_")),"Land decorations appeared on water.");
        Require(water.transform.Find("ColorSurface").GetComponent<Renderer>().sharedMaterial.shader.name==
            "Shader Graphs/SG_TileVertexColor","Land art material leaked into water.");
        Pass("Water coverage retains its existing surface/selection behavior and no land decorations.");
        Require(!FindObjectsByType<Transform>(FindObjectsSortMode.None).Any(t=>t.name.StartsWith("BiomeGroundUVProbe_")),
            "Temporary ground probe leaked.");
        Material[] cached=Resources.FindObjectsOfTypeAll<Material>().Where(m=>m.name.Contains(" (WhiteBox ")).ToArray();
        Require(cached.Length==8,"Ground materials leaked: "+cached.Length);
        Pass("Exactly eight cached runtime ground materials after all changes; no per-transition material allocation.");
        Result="PASS: "+passed.Count+" integration groups; "+transitionFrames+" transition frames observed.";
        WriteReport(); Debug.Log(Result); Destroy(gameObject);
    }

    private void Edit(MapTileInstance tile,Profile profile)
    {
        BlockInfo b=tile.Block;
        Require(environment.TryEditEnvironment(tile.Coordinate,new SimulationEnvironmentEdit {temperature=profile.temperature,
            humidity=profile.humidity,elevation=profile.elevation,habitatRecovery=b.habitatRecovery,
            maxPlantBiomass=b.maxPlantBiomass,plantBiomass=b.plantBiomass},out string error),error);
        Require(tile.Biome==profile.biome,"Unexpected classified biome: "+tile.Biome+" expected "+profile.biome);
    }

    private IEnumerator Change(Profile profile)
    {
        var renderer=origin.transform.Find("ColorSurface").GetComponent<Renderer>();
        Material previous=renderer.sharedMaterial;
        bool different=origin.Biome!=profile.biome;
        Edit(origin,profile);
        if(different)Require(renderer.sharedMaterial==previous,"Authored ground switched immediately at transition start.");
        float minimum=1f,deadline=Time.realtimeSinceStartup+20f;
        do
        {
            yield return null; transitionFrames++;
            var block=new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            float strength=block.GetFloat("_TileArtStrength");
            Require(strength>=0f&&strength<=1.0001f,"Invalid material blend strength.");
            minimum=Mathf.Min(minimum,strength);
        }
        while((visuals.IsBiomeTransitioning||grid.Navigation.IsUpdating)&&Time.realtimeSinceStartup<deadline);
        Require(!visuals.IsBiomeTransitioning&&!grid.Navigation.IsUpdating,"Biome change did not finish.");
        if(different)Require(minimum<.4f,"Art-to-art change did not fade through its palette.");
        yield return null; yield return null;
        CheckArtwork(origin);
    }

    private IEnumerator Ready()
    {
        float deadline=Time.realtimeSinceStartup+25f;
        do{yield return null;}while((grid.Navigation.IsUpdating||visuals.IsBiomeTransitioning)&&Time.realtimeSinceStartup<deadline);
        Require(!grid.Navigation.IsUpdating&&!visuals.IsBiomeTransitioning,"Map did not settle.");
        yield return null;yield return null;
    }

    private IEnumerator Recover()
    {
        float end=Time.realtimeSinceStartup+10f;
        while(Time.realtimeSinceStartup<end)
        {if(population.ValidateMovement(out _)&&population.Leader.gameObject.activeInHierarchy)yield break;yield return null;}
        Require(false,"Representative failed to recover in "+origin.Biome);
    }

    private void CheckIdentity()
    {
        Require(population!=null&&population.UniqueId==identity&&population.EcologicalPopulation.PopulationId==ecologyId&&
            population.Count==count,"Biome presentation changed the population identity or count.");
        Require(population.transform.childCount==2,"Population no longer has one representative and one label.");
    }

    private void CheckArtwork(MapTileInstance tile)
    {
        var collider=tile.transform.Find("ColorSurface").GetComponent<MeshCollider>();
        var renderer=collider.GetComponent<Renderer>(); Mesh mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
        Require(renderer.sharedMaterial.HasProperty("_UseTilePaletteBlend")&&renderer.sharedMaterial.GetFloat("_UseTilePaletteBlend")==1f,
            tile.Biome+": missing authored ground.");
        Require(mesh.uv.Length==mesh.vertexCount&&mesh.uv2.Count(v=>v.x>.99f)>100,tile.Biome+": missing painted UV coverage.");
        Require(collider.enabled&&collider.gameObject.layer==LayerMask.NameToLayer("Ground"),"Ground lost collision/selection ownership.");
        Require(tile.transform.Find("BiomeVisual_"+tile.Biome)!=null,"Missing decoration root.");
        Transform visual = tile.transform.Find("BiomeVisual_"+tile.Biome);
        Require(visual.GetComponent<BiomeNativeGround>() != null, tile.Biome + ": missing native ground geometry.");
        Require(visual.GetComponentsInChildren<MeshRenderer>().Any(r => r.enabled &&
            r.sharedMaterial != null && r.sharedMaterial.shader.name == "Darwin/Moebius NPR" &&
            r.GetComponent<MeshFilter>().sharedMesh.name.EndsWith(" (Native Ground)")),
            tile.Biome + ": original ground shader/topology was replaced.");
        CheckVfx(tile);
        var previewMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        GameObject preview=visuals.CreatePlacementVisualPreview(transform,tile.Biome,previewMaterial,out Mesh previewMesh);
        try{Require(preview!=null&&previewMesh!=null&&!preview.GetComponentsInChildren<Collider>(true).Any(c=>c.enabled),
            "Invalid build preview for "+tile.Biome);}
        finally{if(preview!=null){preview.SetActive(false);Destroy(preview);}if(previewMesh!=null)Destroy(previewMesh);Destroy(previewMaterial);}
    }

    private void CheckVfx(MapTileInstance tile)
    {
        BiomeTileVfx[] rigs = tile.GetComponentsInChildren<BiomeTileVfx>(true);
        bool authoredVfx = tile.Biome != BiomeType.SnowMountain && tile.Biome != BiomeType.Volcano;
        Require(rigs.Length == (authoredVfx ? 1 : 0), tile.Biome + ": incorrect ambient rig count.");
        foreach (BiomeTileVfx rig in rigs)
        {
            Require(rig.isActiveAndEnabled, tile.Biome + ": ambient rig is inactive.");
            int alive = 0;
            foreach (ParticleSystem ps in rig.GetComponentsInChildren<ParticleSystem>())
            {
                Require(ps.isPlaying, tile.Biome + "/" + ps.name + ": particle playback stopped.");
                Require(ps.shape.mesh != null && ps.shape.mesh.vertexCount > 0, "Missing placed emission mesh.");
                foreach (Vector3 vertex in ps.shape.mesh.vertices)
                {
                    Vector3 point = tile.transform.InverseTransformPoint(ps.transform.TransformPoint(vertex));
                    Require(Mathf.Abs(point.x) <= 7.11f && Mathf.Abs(point.z) <= 7.11f,
                        tile.Biome + ": emission still uses demonstration-scene coordinates.");
                }
                Require(ps.trigger.colliderCount == 1 && ps.trigger.GetCollider(0) is Collider boundary && boundary.enabled,
                    "Particle boundary must belong to this placed tile.");
                Require(ps.collision.collidesWith == LayerMask.GetMask("Ground", "Obstacle"),
                    "Particles still collide with the disabled sample-scene proxies.");
                alive += ps.particleCount;
            }
            Require(alive > 0, tile.Biome + ": no live particles after warmup.");
        }
    }

    private void CheckAllSeams()
    {
        foreach(MapTileInstance first in fixtures)
            foreach(MapTileInstance second in fixtures)
            {
                if(Mathf.Abs(first.Coordinate.x-second.Coordinate.x)+Mathf.Abs(first.Coordinate.y-second.Coordinate.y)!=1)continue;
                var a=first.transform.Find("ColorSurface").GetComponent<MeshFilter>().sharedMesh;
                var b=second.transform.Find("ColorSurface").GetComponent<MeshFilter>().sharedMesh;
                var points=new Dictionary<Vector2Int,(float,Color)>(); Vector3[] va=a.vertices,vb=b.vertices;Color[] ca=a.colors,cb=b.colors;
                for(int i=0;i<1225;i++){Vector3 p=first.transform.TransformPoint(va[i]);points[new Vector2Int(Mathf.RoundToInt(p.x*100),Mathf.RoundToInt(p.z*100))]=(p.y,ca[i]);}
                int matches=0;
                for(int i=0;i<1225;i++)
                {
                    Vector3 p=second.transform.TransformPoint(vb[i]);
                    if(!points.TryGetValue(new Vector2Int(Mathf.RoundToInt(p.x*100),Mathf.RoundToInt(p.z*100)),out var expected))continue;
                    Color d=expected.Item2-cb[i];
                    int difference=Mathf.Abs(first.HeightLevel-second.HeightLevel);
                    float allowedHeight=difference>1?difference*MapGridManager.HeightStep+.0001f:.0001f;
                    Require(Mathf.Abs(p.y-expected.Item1)<=allowedHeight&&Mathf.Abs(d.r)+Mathf.Abs(d.g)+Mathf.Abs(d.b)<.0001f,
                        "Biome seam color/height mismatch.");matches++;
                }
                Require(matches==35,"Incomplete shared edge.");
                if(Mathf.Abs(first.HeightLevel-second.HeightLevel)>1)
                {
                    MapTileInstance high=first.HeightLevel>second.HeightLevel?first:second;
                    Require(high.GetComponentsInChildren<Transform>().Any(t=>t.name=="Cliff_NotWalkable"),
                        "An intentional two-level cliff lost its navigation blocker.");
                }
            }
    }

    private void Capture(string name)
    {
        Camera camera=movement.ViewCamera;
        var controls=camera.GetComponent<ScreenEdgeCamera>();if(controls!=null)controls.enabled=false;
        camera.transform.rotation=Quaternion.Euler(48,30,0);
        camera.transform.position=origin.transform.position+Vector3.up-camera.transform.forward*27f;
        camera.fieldOfView=48;
        RenderTexture previous=RenderTexture.active,oldTarget=camera.targetTexture;
        var rt=RenderTexture.GetTemporary(1280,900,24);
        var image=new Texture2D(1280,900,TextureFormat.RGB24,false);
        try
        {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,900),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());}
        finally{camera.targetTexture=oldTarget;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Destroy(image);}
    }

    private void Refresh()=>typeof(MapTileColorTransitionSystem).GetMethod("RefreshAll",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(visuals,null);
    private void Pass(string text){passed.Add(text);WriteReport();}
    private void WriteReport()=>File.WriteAllText(Path.Combine(Output,"integration-results.md"),"# Biome artwork integration\n\n"+Result+"\n\n"+
        string.Join("\n",passed.Select(s=>"- "+s)));
}
#endif
