using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DarwinFarm.Environment;

[InitializeOnLoad]
public static class JournalSaveSmoke
{
    private const string Flag="JournalSaveSmokeActive";
    private const string Phase="JournalSaveSmokePhase";
    private const string Expected="JournalSaveSmokeExpected";
    private const string ExpectedEnvironment="JournalSaveSmokeEnvironment";
    private const string ExpectedEconomy="JournalSaveSmokeEconomy";
    private const string ExpectedTechnology="JournalSaveSmokeTechnology";
    private static int frames;
    static JournalSaveSmoke() => EditorApplication.update += Tick;
    public static void RunBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/WhiteBox.unity");
        SessionState.SetBool(Flag,true);
        SessionState.SetInt(Phase,0);
        SessionState.SetString(Expected,string.Empty);
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying)return;
        if(++frames<12)return;
        frames=0;
        try
        {
            GameSaveService.TestFolderOverride=Path.Combine(Application.temporaryCachePath,"DarwinJournalSaveSmoke");
            if(SessionState.GetInt(Phase,0)==0)
            {
                var canvas=GameObject.Find("JournalCanvas");
                Require(canvas!=null,"Canvas not loaded");
                var safe=canvas.transform.Find("SafeArea");
                Click(safe.Find("TitlePanel/NewGame"));
                Click(safe.Find("GameMenuButton"));
                var clock=UnityEngine.Object.FindFirstObjectByType<SimulationTime>();
                var model=UnityEngine.Object.FindFirstObjectByType<SimulationController>();
                Require(clock!=null&&model!=null,"Simulation not loaded");
                var grid=UnityEngine.Object.FindFirstObjectByType<MapGridManager>();
                var bridge=UnityEngine.Object.FindFirstObjectByType<MapSimulationBridge>();
                var environment=UnityEngine.Object.FindFirstObjectByType<SimulationEnvironmentController>();
                Require(grid.TryGetTile(Vector2Int.zero,out var origin),"Origin tile missing");
                Require(grid.TryPlaceTile(new Vector2Int(1,0),origin.Definition),"Could not place second tile");
                Require(grid.TryGetTile(new Vector2Int(1,0),out var neighbor),"Second tile missing");
                var resident=new PopulationData { speciesAmount=7,species=origin.Block.community[0].species,
                    speciesId=origin.Block.community[0].speciesId,
                    lineageName=origin.Block.community[0].lineageName,
                    landPositionBlock=neighbor.Block,lastMigrationSource=origin.Block,
                    fitTemperature=50,fitHumidity=50,size=10,fertility=50 };
                neighbor.Block.community.Add(resident);
                bridge.Synchronize();
                var technology=UnityEngine.Object.FindFirstObjectByType<EnvironmentTechnologyController>();
                Require(technology.TryDeploy(new Vector2Int(1,0),
                    new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.TemperatureChange,1),
                    out var preview),"Environment technology: "+preview.Error);
                var detector=UnityEngine.Object.FindFirstObjectByType<EcologyNewsDetector>();
                detector.Bus.Publish(new EcologicalEventData { eventId="smoke:1",gameDay=clock.currentDay,
                    type=EcologicalEventType.NewSpecies,message="测试生态新闻" });
                string hash=model.CanonicalModelHash();
                SessionState.SetString(ExpectedEnvironment,
                    Newtonsoft.Json.JsonConvert.SerializeObject(environment.CaptureSaveState()));
                SessionState.SetString(ExpectedEconomy,
                    UnityEngine.Object.FindFirstObjectByType<BioenergyAccount>().CaptureState());
                SessionState.SetString(ExpectedTechnology,
                    Newtonsoft.Json.JsonConvert.SerializeObject(technology.CaptureSaveState()));
                int day=clock.currentDay;
                GameSaveService.TryDelete(1,out _);
                Require(GameSaveService.TrySave(1,out string error),"First save: "+error);
                clock.NextDay();
                Require(GameSaveService.TrySave(1,out error),"Overwrite save: "+error);
                string file=Path.Combine(GameSaveService.TestFolderOverride,"slot_1.json");
                File.WriteAllText(file,"{broken");
                var slot=GameSaveService.ReadSlot(1);
                Require(slot.valid&&slot.backup&&slot.day==day,"Backup fallback failed");
                Require(GameSaveService.TryPrepareLoad(1,out error),"Prepare backup: "+error);
                SessionState.SetString(Expected,hash);
                SessionState.SetInt(Phase,1);
                SceneManager.LoadScene("WhiteBox");
            }
            else
            {
                var clock=UnityEngine.Object.FindFirstObjectByType<SimulationTime>();
                var model=UnityEngine.Object.FindFirstObjectByType<SimulationController>();
                Require(clock!=null&&model!=null,"Loaded simulation missing");
                Require(!GameSaveService.HasPending,"Pending restore not consumed");
                Require(Time.timeScale>0,"Loaded game did not resume");
                string actual=model.CanonicalModelHash();
                if(actual!=SessionState.GetString(Expected,""))
                {
                    GameSaveService.TrySave(2,out string diagnosticError);
                    Debug.LogError("JournalSaveSmoke mismatch expected="+SessionState.GetString(Expected,"")+
                        " actual="+actual+" folder="+GameSaveService.TestFolderOverride+
                        " diagnostic="+diagnosticError);
                }
                Require(actual==SessionState.GetString(Expected,""),
                    "Ecology state changed across save/load");
                var environment=UnityEngine.Object.FindFirstObjectByType<SimulationEnvironmentController>();
                Require(Newtonsoft.Json.JsonConvert.SerializeObject(environment.CaptureSaveState())==
                    SessionState.GetString(ExpectedEnvironment,""),"Environment timeline changed across save/load");
                Require(UnityEngine.Object.FindFirstObjectByType<BioenergyAccount>().CaptureState()==
                    SessionState.GetString(ExpectedEconomy,""),"Bioenergy changed across save/load");
                string actualTechnology=Newtonsoft.Json.JsonConvert.SerializeObject(
                    UnityEngine.Object.FindFirstObjectByType<EnvironmentTechnologyController>().CaptureSaveState());
                Require(actualTechnology==SessionState.GetString(ExpectedTechnology,""),
                    "Technology history changed across save/load: "+
                    SessionState.GetString(ExpectedTechnology,"")+" / "+actualTechnology);
                var newsState=UnityEngine.Object.FindFirstObjectByType<EcologyNewsHUD>().CaptureSaveState();
                Require(newsState.seen.Contains("smoke:1")&&
                    (newsState.pending.Exists(e=>e.eventId=="smoke:1")||
                     newsState.current?.eventId=="smoke:1"),
                    "Saved ecology news disappeared after load");
                var grid=UnityEngine.Object.FindFirstObjectByType<MapGridManager>();
                Require(grid.TryGetTile(new Vector2Int(1,0),out var neighbor),"Saved tile was not restored");
                Require(grid.TryGetTile(Vector2Int.zero,out var origin),"Saved origin missing");
                Require(neighbor.Block.community.Count==1&&
                    neighbor.Block.community[0].lastMigrationSource==origin.Block,
                    "Population cross-tile link was not restored");
                Require(GameSaveService.TryDelete(1,out string error),"Test cleanup: "+error);
                SessionState.SetBool(Flag,false);
                Debug.Log("JournalSaveSmoke: PASS — complete model roundtrip, overwrite, checksum and backup fallback.");
                EditorApplication.Exit(0);
            }
        }
        catch(Exception e)
        {
            SessionState.SetBool(Flag,false);
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }
    private static void Click(Transform target)
    {
        Require(target!=null,"UI button missing");
        target.GetComponent<Button>().onClick.Invoke();
    }
    private static void Require(bool condition,string message)
    { if(!condition)throw new InvalidOperationException("JournalSaveSmoke: "+message); }
}
