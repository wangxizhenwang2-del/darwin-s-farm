using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DarwinFarm.Environment;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Test-only runner, compiled in memory by Unity Pipeline; never imported into Assets.
public static class GameplayAudit
{
    private static readonly string Folder = Path.GetFullPath("../output/GameplayTutorialTest_20261020");
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, Fields).GetValue(source);
    private static T Find<T>() where T : UnityEngine.Object => UnityEngine.Object.FindFirstObjectByType<T>();
    private static void Click(object source, string field)
    {
        object value = source.GetType().GetField(field, Fields).GetValue(source);
        Button button = value as Button;
        if (button == null) button = ((GameObject)value).GetComponent<Button>();
        button.onClick.Invoke();
    }
    private static string Write(string name, object value)
    {
        string json = JsonConvert.SerializeObject(value, Formatting.Indented);
        File.WriteAllText(Path.Combine(Folder, name + ".json"), json);
        return json;
    }
    private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    private static bool Reachable(Button b)
    {
        var hits = new List<RaycastResult>();
        var canvas = b.GetComponentInParent<Canvas>();
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, ((RectTransform)b.transform).TransformPoint(((RectTransform)b.transform).rect.center));
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) {position = point}, hits);
        return hits.Count > 0 && (hits[0].gameObject.transform == b.transform || hits[0].gameObject.transform.IsChildOf(b.transform));
    }
    private static async Task Shot(string name)
    {
        ScreenCapture.CaptureScreenshot(Path.Combine(Folder, name + ".png"));
        await Task.Delay(220);
    }
    private static async Task Dismiss()
    {
        for (int i = 0; i < 220; i++)
        {
            var unlock = Find<SpeciesUnlockPresenter>();
            if (unlock == null || !unlock.IsPresenting) { if (i > 15) return; }
            else if (unlock.Phase == SpeciesUnlockPresenter.CardPhase.Ready) unlock.Continue();
            await Task.Delay(80);
        }
        throw new Exception("Starting discovery card did not become dismissible.");
    }
    public static async Task<string> Tutorial(int round)
    {
        SceneManager.LoadScene("WhiteBox");
        await Task.Delay(900);
        var checks = new List<object>();
        var j = Find<DarwinJournalCanvas>();
        var db = Field<TutorialDatabase>(j, "tutorial");
        if (!Field<GameObject>(j, "tutorialPanel").activeSelf) Click(j, "titleTutorial");
        await Task.Delay(120);
        var categories = Field<Button[]>(j, "categoryButtons");
        var pages = Field<Button[]>(j, "pageButtons");
        var body = Field<TMP_Text>(j, "tutorialBody");
        var scroll = Field<ScrollRect>(j, "tutorialScroll");
        int initialDay = Find<SimulationTime>().currentDay;
        for (int c = 0; c < db.categories.Length; c++)
        {
            bool categoryHit = Reachable(categories[c]);
            categories[c].onClick.Invoke();
            for (int p = 0; p < db.categories[c].pages.Length; p++)
            {
                bool pageHit = Reachable(pages[p]);
                pages[p].onClick.Invoke();
                await Task.Delay(65);
                Canvas.ForceUpdateCanvases();
                body.ForceMeshUpdate();
                var expected = db.categories[c].pages[p];
                bool content = body.text.Contains(expected.content) && body.text.Contains(expected.title);
                float textHeight = body.preferredHeight;
                float contentHeight = scroll.content.rect.height;
                bool reset = contentHeight <= scroll.viewport.rect.height + 1 || scroll.verticalNormalizedPosition > .98f;
                scroll.verticalNormalizedPosition = 0;
                await Task.Delay(35);
                checks.Add(new {category=db.categories[c].title, page=expected.title, categoryHit, pageHit,
                    content, reset, textHeight, contentHeight, viewportHeight=scroll.viewport.rect.height,
                    lines=body.textInfo.lineCount, clippedByContent=textHeight > contentHeight + 8});
                if ((c == 0 && p == 2) || (c == 2 && p == 0) || (c == 5 && p == 1))
                { scroll.verticalNormalizedPosition = 1; await Task.Delay(80); await Shot("round"+round+"-tutorial-"+c+"-"+p); }
            }
        }
        bool titleTutorialPauses = initialDay == Find<SimulationTime>().currentDay;
        Click(j, "tutorialClose");
        bool returnedTitle = Field<GameObject>(j, "titlePanel").activeSelf;
        Click(j, "titleNew");
        Find<SimulationTime>().Pause();
        await Task.Delay(350);
        bool autoTutorialShown = Field<GameObject>(j, "tutorialPanel").activeSelf;
        await Shot("round"+round+"-new-game");
        await Task.Delay(3400);
        await Dismiss();
        bool gameplay = j.IsGameplayVisible;
        var clock = Find<SimulationTime>();
        clock.Play();
        Click(j, "gameMenuButton");
        Click(j, "pauseTutorial");
        int pausedDay = clock.currentDay;
        await Task.Delay(450);
        bool pauseTutorialPauses = pausedDay == clock.currentDay;
        Click(j, "tutorialClose");
        bool returnsPause = Field<GameObject>(j, "pausePanel").activeSelf;
        Click(j, "pauseResume");
        bool resumes = j.IsGameplayVisible && !clock.IsPaused && Time.timeScale > 0;
        clock.Pause();
        var summary = new {round, categories=db.categories.Length, pages=checks.Count, titleTutorialPauses,
            returnedTitle, autoTutorialShown, gameplay, pauseTutorialPauses, returnsPause, resumes,
            initial=Snapshot(), checks};
        return Write("round"+round+"-tutorial", summary);
    }
    private static object Snapshot()
    {
        var simulation = Find<SimulationController>();
        var grid = Find<MapGridManager>();
        var clock = Find<SimulationTime>();
        var account = Find<BioenergyAccount>();
        var economy = Find<PlayerEconomyController>();
        var tiles = grid.GetPlacedTiles().Where(t => t != null && t.Block != null).OrderBy(t=>t.Coordinate.x).ThenBy(t=>t.Coordinate.y).Select(t => new {
            x=t.Coordinate.x,y=t.Coordinate.y, terrain=t.Definition.name, t.Block.temperature,t.Block.humidity,
            t.Block.elevation, plants=t.Block.plantBiomass, capacity=t.Block.maxPlantBiomass, recovery=t.Block.habitatRecovery,
            populations=(t.Block.community ?? new List<PopulationData>()).Where(p=>p!=null && p.speciesAmount>0).Select(p=>new {
                species=p.species != null ? p.species.SpeciesName : p.lineageName, id=p.PopulationId,
                count=p.speciesAmount, temp=p.fitTemperature, humidity=p.fitHumidity,movement=p.movementAbility,
                size=p.size,fertility=p.fertility,energy=p.energySatisfactionToday,
                progress=p.evolutionConversions.Select(e=>new {target=e.target != null ? e.target.SpeciesName : "?",e.effectiveDays,e.failedDraws}).ToArray()
            }).ToArray()
        }).ToArray();
        return new {day=clock.currentDay,clock.SecondsPerDay, living=simulation.LivingSpecies.Select(s=>s.SpeciesName).OrderBy(s=>s).ToArray(),
            balance=account.Balance,costsEnabled=account.CostsEnabled, lost=economy.IsGameLost, tiles};
    }
    private static async Task NewWorld(int seed)
    {
        SceneManager.LoadScene("WhiteBox");
        await Task.Delay(900);
        Find<SimulationController>().ConfigureDeterministicRun(seed);
        Click(Find<DarwinJournalCanvas>(), "titleNew");
        Find<SimulationTime>().Pause();
        await Task.Delay(3500);
        await Dismiss();
        Find<SimulationTime>().Pause();
    }
    public static async Task<string> Difficulty(string policy, int seed, int days, int round)
    {
        await NewWorld(seed);
        var clock = Find<SimulationTime>();
        var simulation = Find<SimulationController>();
        var grid = Find<MapGridManager>();
        var economy = Find<PlayerEconomyController>();
        var technology = Find<EnvironmentTechnologyController>();
        var events = new List<object>();
        var actions = new List<object>();
        var samples = new List<object> {Snapshot()};
        simulation.OnSpeciesEvolved += (b, a, p, s, n) => events.Add(new {kind="evolved", day=clock.currentDay, from=a.species?.SpeciesName, to=s.SpeciesName, founders=n});
        simulation.OnSpeciesExtinct += s => events.Add(new {kind="extinct",day=clock.currentDay,species=s.SpeciesName});
        simulation.OnSpeciesRevived += s => events.Add(new {kind="revived",day=clock.currentDay,species=s.SpeciesName});
        Action<EnvironmentTechnologyKind,int> deploy = (kind,tier) => {
            bool ok=technology.TryDeploy(Vector2Int.zero,new EnvironmentTechnologyChoice(kind,tier),out var preview);
            actions.Add(new {day=clock.currentDay,kind=kind.ToString(),tier,ok,error=preview.Error,preview.After,preview.Cost});
        };
        if (policy == "climate-once" || policy == "climate-maintained")
        { deploy(EnvironmentTechnologyKind.TemperatureChange,1); deploy(EnvironmentTechnologyKind.HumidityChange,1); }
        if (policy == "neighbor-rainforest")
        {
            var definitions = UnityEditor.AssetDatabase.FindAssets("t:MapTileDefinition").Select(g=>UnityEditor.AssetDatabase.LoadAssetAtPath<MapTileDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(g))).Where(d=>d!=null).ToArray();
            var rainforest = definitions.FirstOrDefault(d=>d.name == "雨林" || d.name.IndexOf("Rainforest",StringComparison.OrdinalIgnoreCase)>=0);
            if(rainforest == null) throw new Exception("Rainforest definition not found: "+string.Join(",",definitions.Select(d=>d.name)));
            bool ok=economy.TryExpand(Vector2Int.right,rainforest,0,out var error);
            actions.Add(new {day=clock.currentDay,kind="expand-rainforest",ok,error});
        }
        if (policy == "neighbor-guided")
        {
            grid.TryGetTile(Vector2Int.zero,out var origin);
            bool ok=economy.TryExpand(Vector2Int.right,origin.Definition,0,out var error);
            actions.Add(new {day=clock.currentDay,kind="expand-unlocked-neighbor",ok,error});
            foreach(var choice in new[] {new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.TemperatureChange,1),new EnvironmentTechnologyChoice(EnvironmentTechnologyKind.HumidityChange,2)})
            {
                bool applied=technology.TryDeploy(Vector2Int.right,choice,out var preview);
                actions.Add(new {day=clock.currentDay,kind="neighbor-"+choice.Kind,tier=choice.Tier,ok=applied,error=preview.Error});
            }
        }
        if (policy == "wrong-food-removal")
        { deploy(EnvironmentTechnologyKind.PlantRecoveryChange,-2); deploy(EnvironmentTechnologyKind.PlantStockChange,-2); }
        int firstLoss=-1,minimumPopulation=int.MaxValue,maxSpecies=1;
        for(int i=0;i<days;i++)
        {
            if(policy == "climate-maintained" && i>0 && i%150==0)
            { deploy(EnvironmentTechnologyKind.TemperatureChange,1); deploy(EnvironmentTechnologyKind.HumidityChange,1); }
            clock.NextDay();
            int count=simulation.ReadBlocks().Where(b=>b!=null && b.community!=null).Sum(b=>b.community.Where(p=>p!=null).Sum(p=>Math.Max(0,p.speciesAmount)));
            minimumPopulation=Math.Min(minimumPopulation,count);
            maxSpecies=Math.Max(maxSpecies,simulation.LivingSpecies.Count);
            if(economy.IsGameLost && firstLoss<0)firstLoss=clock.currentDay;
            if(clock.currentDay<=10 || clock.currentDay%25==0 || firstLoss==clock.currentDay) samples.Add(Snapshot());
            if(i%50==0)await Task.Delay(1);
            if(economy.IsGameLost)break;
        }
        await Task.Delay(120);
        await Dismiss();
        clock.Pause();
        await Shot("round"+round+"-"+policy+"-seed"+seed);
        string hash=simulation.CanonicalModelHash();
        return Write("round"+round+"-"+policy+"-seed"+seed,new {round,policy,seed,requestedDays=days,
            firstLoss,minimumPopulation,maxSpecies,finalHash=hash,actions,events,final=Snapshot(),samples});
    }
    public static string State() => Write("current-state",Snapshot());
    private static async Task Key(Key key)
    {
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(key));
        await Task.Delay(100);
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        await Task.Delay(100);
    }
    private static async Task Pointer(Vector2 point)
    {
        InputSystem.QueueStateEvent(Mouse.current, new MouseState {position=point});
        await Task.Delay(70);
        InputSystem.QueueStateEvent(Mouse.current, new MouseState {position=point}.WithButton(MouseButton.Left));
        await Task.Delay(80);
        InputSystem.QueueStateEvent(Mouse.current, new MouseState {position=point});
        await Task.Delay(80);
    }
    public static async Task<string> Controls(int round)
    {
        var checks = new List<object>();
        var clock = Find<SimulationTime>();
        var maps = Find<SpeciesMapsUI>();
        var build = Find<MapBuildModeController>();
        await Key(UnityEngine.InputSystem.Key.M);
        checks.Add(new {name="M opens species/evolution network",pass=maps.IsOpen});
        await Shot("round"+round+"-evolution-map");
        var nodes = maps.GetComponentsInChildren<SpeciesMapNodeView>(true);
        var colorful = nodes.First(n=>n.gameObject.activeInHierarchy && n.Species!=null && n.Species.SpeciesName=="彩鸡");
        var portrait = Field<Image>(colorful,"portrait");
        var canvas = colorful.GetComponentInParent<Canvas>();
        Vector2 nodePoint = RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,portrait.transform.position);
        await Pointer(nodePoint);
        var clueRoot = maps.GetComponentsInChildren<Transform>(true).First(t=>t.name=="SpeciesCluePopup");
        var clue = clueRoot.GetComponentsInChildren<TMP_Text>(true).Select(t=>t.text).ToArray();
        checks.Add(new {name="Click colorful-chicken portrait opens evolution clue",pass=clueRoot.gameObject.activeInHierarchy,text=string.Join("\n",clue)});
        await Shot("round"+round+"-evolution-clue");
        await Key(UnityEngine.InputSystem.Key.Escape);
        checks.Add(new {name="Escape closes network",pass=!maps.IsOpen});
        await Key(UnityEngine.InputSystem.Key.N);
        checks.Add(new {name="N opens building mode",pass=build.IsBuildMode});
        await Shot("round"+round+"-build-palette");
        await Key(UnityEngine.InputSystem.Key.N);
        checks.Add(new {name="N closes building mode",pass=!build.IsBuildMode});
        var information = Find<GameInformationUI>();
        var grid = Find<MapGridManager>();
        grid.TryGetTile(Vector2Int.zero,out var origin);
        Vector2 tilePoint = Camera.main.WorldToScreenPoint(origin.Core.bounds.center);
        tilePoint.x -= 75;
        InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(UnityEngine.InputSystem.Key.LeftAlt));
        await Task.Delay(80);
        await Pointer(tilePoint);
        InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
        await Task.Delay(80);
        checks.Add(new {name="Alt-left-click selects origin information",pass=information.TileIsOpen || information.PopulationIsOpen});
        await Shot("round"+round+"-information");
        information.ClosePopulation(); information.CloseTile();
        var session = Find<EnvironmentToolSession>();
        session.SelectKind(Array.IndexOf(EnvironmentTechnologyCatalog.All,EnvironmentTechnologyKind.TemperatureChange));
        session.SelectOption(1);
        bool target=information.TrySelectTileAt(tilePoint);
        session.Refresh();
        string preview=session.PreviewText;
        bool canApply=session.CanDeploy;
        session.Deploy();
        string applyNotice=session.Notice;
        bool cancelAllowed=session.CanCancel;
        session.CancelEffect();
        checks.Add(new {name="Tool choice-target-preview-apply-cancel",pass=target && canApply && applyNotice=="已应用" && cancelAllowed && session.Notice=="已撤销效果",preview,applyNotice,cancelNotice=session.Notice});
        await Shot("round"+round+"-tool-preview");
        session.End();
        clock.Pause();
        await Key(UnityEngine.InputSystem.Key.Space);
        bool spaceResumes=!clock.IsPaused;
        await Key(UnityEngine.InputSystem.Key.Digit4);
        bool speed4=clock.Speed==4;
        int before=clock.currentDay;
        await Task.Delay(1050);
        int advanced=clock.currentDay-before;
        await Key(UnityEngine.InputSystem.Key.Space);
        bool spacePauses=clock.IsPaused;
        checks.Add(new {name="Space pause/resume and 4x shortcut",pass=spaceResumes && speed4 && advanced>=3 && spacePauses,advanced});
        return Write("round"+round+"-controls",new {round,checks,final=Snapshot()});
    }
}
