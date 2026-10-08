using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DarwinFarm.Environment;
using UnityEngine;
using UnityEngine.InputSystem;

// Test-scene only. Every operation and daily result is exported as UTF-8 CSV.
[DisallowMultipleComponent]
public sealed class V41RegressionHarness : MonoBehaviour
{
    [SerializeField] private MapTileDefinition[] tilePresets;
    [SerializeField] private int initialSeed = 20261008;
    private MapGridManager grid;
    private MapSimulationBridge bridge;
    private SimulationController simulation;
    private SimulationEnvironmentController environment;
    private SimulationInterventionController interventions;
    private SimulationTime clock;
    private readonly List<SpeciesData> fixtures = new List<SpeciesData>();
    private StreamWriter actions, tiles, populations, eventsFile;
    private string runPath = "Starting";
    private string caseId = "SETUP", operatorName = "", note = "";
    private string x = "0", y = "0", rotation = "0", amount = "10", count = "100";
    private string tempOffset = "20", humidityOffset = "20", monsoonId = "0", seedText;
    private string editT = "68", editH = "68", editR = "100000", editS = "100000", editZ = "0", tileHeight = "0";
    private string secondsPerDay = "5";
    private int presetIndex, speciesIndex, kindIndex;
    private bool matchTargetTraits, show = true, ready;
    private bool starvationLock;
    private Vector2Int starvationTile;
    private Vector2 scroll;
    private int sequence;
    private string status = "Waiting for map initialization";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] Kinds = { "Temperature", "Humidity", "PlantBiomass", "PlantRecovery", "Elevation", "WaterCoverage" };

    private void Awake()
    {
        grid = GetComponent<MapGridManager>(); bridge = GetComponent<MapSimulationBridge>();
        simulation = GetComponent<SimulationController>(); clock = GetComponent<SimulationTime>();
        interventions = GetComponent<SimulationInterventionController>();
        if (clock != null) clock.Pause();
        UnityEngine.Random.InitState(initialSeed);
        seedText = initialSeed.ToString(Invariant);
        CreateFixtures();
    }

    private IEnumerator Start()
    {
        yield return null; // Let MapGridManager, bridge and environment initialize.
        environment = GetComponent<SimulationEnvironmentController>();
        if (grid == null || bridge == null || simulation == null || clock == null ||
            interventions == null || environment == null || !grid.GetPlacedTiles().Any())
        { status = "ERROR: test scene components or initial tile are missing"; yield break; }
        OpenRun();
        grid.TilesChanged += OnTilesChanged;
        simulation.OnBeforeDaySimulated += OnBeforeDay;
        simulation.OnDaySimulated += OnDay;
        simulation.OnPopulationMigrated += OnMigrated;
        simulation.OnSpeciesEvolved += OnEvolved;
        simulation.OnSpeciesUnlocked += OnUnlocked;
        simulation.OnSpeciesExtinct += OnExtinct;
        simulation.OnSpeciesRevived += OnRevived;
        simulation.OnBlockCommunityChanged += OnCommunityChanged;
        simulation.OnBlockEnvironmentChanged += OnBlockEnvironmentChanged;
        environment.Changed += OnEnvironmentChanged;
        environment.SettlementAvailable += OnSettlement;
        Application.logMessageReceived += OnUnityLog;
        ready = true;
        Snapshot("initial", 0);
        Event("run_started", "seed=" + initialSeed);
        Flush();
        status = "Ready; paused. Export folder: " + runPath;
    }

    private void OnDisable()
    {
        if (ready)
        {
            // On Play-mode shutdown Unity may destroy tiles before this callback.
            // Every CSV writer auto-flushes, so closing needs no final snapshot.
            Event("run_stopped", "");
            if (grid != null) grid.TilesChanged -= OnTilesChanged;
            if (simulation != null)
            {
                simulation.OnBeforeDaySimulated -= OnBeforeDay;
                simulation.OnDaySimulated -= OnDay;
                simulation.OnPopulationMigrated -= OnMigrated;
                simulation.OnSpeciesEvolved -= OnEvolved;
                simulation.OnSpeciesUnlocked -= OnUnlocked;
                simulation.OnSpeciesExtinct -= OnExtinct;
                simulation.OnSpeciesRevived -= OnRevived;
                simulation.OnBlockCommunityChanged -= OnCommunityChanged;
                simulation.OnBlockEnvironmentChanged -= OnBlockEnvironmentChanged;
            }
            if (environment != null)
            {
                environment.Changed -= OnEnvironmentChanged;
                environment.SettlementAvailable -= OnSettlement;
            }
            Application.logMessageReceived -= OnUnityLog;
            ready = false;
        }
        actions?.Dispose(); tiles?.Dispose(); populations?.Dispose(); eventsFile?.Dispose();
    }

    private void OnDestroy()
    {
        foreach (SpeciesData species in fixtures) if (species != null) Destroy(species);
    }

    private void OpenRun()
    {
        runPath = Path.Combine(Application.persistentDataPath, "V41TestRuns",
            DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", Invariant));
        Directory.CreateDirectory(runPath);
        actions = Open("actions.csv", "seq,time,day,case_id,operator,action,input,result,note");
        tiles = Open("tiles.csv", "seq,sample,day,time,x,y,preset,biome,rotation,height,terrain,water,T,T_target,T_phase,T_days,T_progress,H,H_target,H_phase,H_days,H_progress,R,R_target,R_phase,R_days,R_progress,plant_stock,plant_cap,plant_growth,plant_consumed,algae_stock,monsoon_id,monsoon_applied,local_climate,lab_starvation_lock");
        populations = Open("populations.csv", "seq,sample,day,time,x,y,index,species,species_id,lineage,niche,bank,count,level,fit_T,fit_H,movement,size,fertility,fitness,energy_need,allocated,carrying_capacity,satisfaction,actual_satisfaction,births,deaths,reserve,reserve_start,hunt_energy,predation_deaths,pre_predation_count,next_migration_day,mutation_days,mutation_previous_count");
        eventsFile = Open("events.csv", "seq,time,day,case_id,event,details");
        File.WriteAllText(Path.Combine(runPath, "README.txt"),
            "V4.1 regression lab | Scene: V41RegressionLab.unity\n" +
            "CSV encoding: UTF-8 BOM. Numeric values: invariant culture.\n" +
            "seq links actions to before/after snapshots. sample=day_N is after daily simulation.\n" +
            "S02: lab_starvation_lock=1 means R is clamped to zero after environment advancement and before ecology; lab_starvation_clamp events record each clamp.\n" +
            "S03: reserve_start and hunt_energy are the predator energy ledger; predation_deaths records prey killed by predators.\n" +
            "Run started: " + DateTime.Now.ToString("O", Invariant) + "\n" +
            "Unity: " + Application.unityVersion + "\n" +
            "Seed: " + initialSeed + "\n" +
            "Actions made through the lab panel have exact inputs. For actions made through game UI, enter a note and click Record observation.\n");
    }

    private StreamWriter Open(string name, string header)
    {
        var writer = new StreamWriter(Path.Combine(runPath, name), false,
            new System.Text.UTF8Encoding(true)) { AutoFlush = true };
        writer.WriteLine(header); return writer;
    }

    private static string C(object value)
    {
        string s = Convert.ToString(value, Invariant) ?? "";
        return "\"" + s.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
    }
    private static void Row(StreamWriter writer, params object[] values) =>
        writer.WriteLine(string.Join(",", values.Select(C)));
    private string Now() => DateTime.Now.ToString("O", Invariant);
    private static string Name(SpeciesData s) => s == null ? "" :
        (string.IsNullOrWhiteSpace(s.SpeciesName) ? s.name : s.SpeciesName);
    private void Flush() { actions?.Flush(); tiles?.Flush(); populations?.Flush(); eventsFile?.Flush(); }

    private void Event(string name, string details)
    {
        if (eventsFile != null) Row(eventsFile, sequence, Now(), clock != null ? clock.currentDay : -1, caseId, name, details);
    }
    private void OnUnityLog(string message, string stack, LogType type)
    {
        if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            Event("unity_" + type, message + (type == LogType.Exception ? " | " + stack : ""));
    }
    private void OnTilesChanged()
    {
        Event("map_tiles_changed", "observe after bridge sync");
        StartCoroutine(SnapshotAtFrameEnd("map_change", sequence));
    }
    private IEnumerator SnapshotAtFrameEnd(string sample, int seq)
    { yield return new WaitForEndOfFrame(); if (ready) Snapshot(sample, seq); }
    private void OnDay(int day) { Snapshot("day_" + day, sequence); Event("day_completed", "day=" + day); Flush(); }
    private void OnBeforeDay(int day)
    {
        if (!starvationLock) return;
        if (!bridge.TryGetBlock(starvationTile, out var block))
        { starvationLock = false; Event("lab_starvation_stopped", "tile removed"); return; }
        int recoveryBeforeClamp = block.habitatRecovery;
        bool success = ApplyStarvationClamp(false, out string error);
        Event("lab_starvation_clamp", "tile=" + starvationTile.x + ":" + starvationTile.y +
            ";day=" + day + ";R_before=" + recoveryBeforeClamp +
            ";R_after=" + block.habitatRecovery + ";S=" + block.plantBiomass.ToString(Invariant) +
            ";result=" + Result(success, error));
        if (!success) starvationLock = false;
    }
    private void OnMigrated(BlockInfo from, BlockInfo to, PopulationData p, int n) =>
        Event("migration", "from=" + Position(from) + ";to=" + Position(to) + ";species=" + Name(p?.species) + ";count=" + n);
    private void OnUnlocked(SpeciesData s) => Event("species_unlocked", Name(s));
    private void OnExtinct(SpeciesData s) => Event("species_extinct", Name(s));
    private void OnRevived(SpeciesData s) => Event("species_revived", Name(s));
    private void OnCommunityChanged(BlockInfo b) => Event("community_changed", "tile=" + Position(b));
    private void OnBlockEnvironmentChanged(BlockInfo b) => Event("block_environment_changed", "tile=" + Position(b));
    private void OnEnvironmentChanged(IReadOnlyList<EnvironmentReadSnapshot> states)
    {
        Event("environment_changed", "tile_count=" + states.Count);
        StartCoroutine(SnapshotAtFrameEnd("environment_change", sequence));
    }
    private void Update()
    {
        if (!ready) return;
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            string place = PointerPlace(mouse.position.ReadValue());
            if (mouse.leftButton.wasPressedThisFrame) Event("input_mouse_left_down", place);
            if (mouse.leftButton.wasReleasedThisFrame) Event("input_mouse_left_up", place);
            if (mouse.rightButton.wasPressedThisFrame) Event("input_mouse_right_down", place);
            if (mouse.rightButton.wasReleasedThisFrame) Event("input_mouse_right_up", place);
            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f) Event("input_mouse_wheel", place + ";delta=" + wheel.ToString(Invariant));
        }
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.nKey.wasPressedThisFrame) Event("input_key_N", "build mode");
        if (keyboard.rKey.wasPressedThisFrame) Event("input_key_R", "rotate");
        if (keyboard.escapeKey.wasPressedThisFrame) Event("input_key_Escape", "cancel");
        if (keyboard.wKey.wasPressedThisFrame) Event("input_key_W", "camera");
        if (keyboard.aKey.wasPressedThisFrame) Event("input_key_A", "camera");
        if (keyboard.sKey.wasPressedThisFrame) Event("input_key_S", "camera");
        if (keyboard.dKey.wasPressedThisFrame) Event("input_key_D", "camera");
    }
    private string PointerPlace(Vector2 screen)
    {
        string where = "screen=" + screen.x.ToString("F1", Invariant) + ":" + screen.y.ToString("F1", Invariant);
        Camera camera = Camera.main;
        if (camera != null && Physics.Raycast(camera.ScreenPointToRay(screen), out RaycastHit hit, 1000f))
        {
            MapTileInstance tile = hit.collider.GetComponentInParent<MapTileInstance>();
            if (tile != null) where += ";tile=" + tile.Coordinate.x + ":" + tile.Coordinate.y;
            where += ";world=" + hit.point.x.ToString("F2", Invariant) + ":" +
                hit.point.y.ToString("F2", Invariant) + ":" + hit.point.z.ToString("F2", Invariant);
        }
        return where;
    }
    private void OnEvolved(BlockInfo block, PopulationData source, PopulationData result, SpeciesData target, int n) =>
        Event("preset_evolution", "tile=" + Position(block) + ";from=" + Name(source?.species) + ";to=" + Name(target) + ";count=" + n);
    private void OnSettlement(MonsoonSettlement s) =>
        Event("monsoon_settlement", "id=" + s.MonsoonId + ";reason=" + s.Reason + ";receipt=" +
            s.Payment?.ReceiptId + ";refund=" + s.RefundAmount.ToString(Invariant));
    private string Position(BlockInfo block)
    {
        if (block == null) return "?";
        var tile = block.GetComponent<MapTileInstance>();
        return tile == null ? "?" : tile.Coordinate.x + ":" + tile.Coordinate.y;
    }

    private void Snapshot(string sample, int seq)
    {
        if (tiles == null || grid == null) return;
        foreach (MapTileInstance tile in grid.GetPlacedTiles().Where(t => t != null)
                     .OrderBy(t => t.Coordinate.x).ThenBy(t => t.Coordinate.y))
        {
            if (tile == null) continue;
            Vector2Int pos = tile.Coordinate; BlockInfo b = tile.Block;
            EnvironmentReadSnapshot read = null; environment?.TryRead(pos, out read);
            EnvironmentSnapshot e = read?.Environment;
            Row(tiles, seq, sample, clock.currentDay, Now(), pos.x, pos.y,
                tile.Definition?.displayName, tile.Biome, tile.RotationSteps, tile.HeightLevel,
                e?.Terrain.ToString(), b?.waterCoverage.ToString(),
                e?.Temperature.Value, e?.Temperature.Target, e?.Temperature.Phase, e?.Temperature.RemainingDays, e?.Temperature.Progress,
                e?.Humidity.Value, e?.Humidity.Target, e?.Humidity.Phase, e?.Humidity.RemainingDays, e?.Humidity.Progress,
                e?.Recovery.Value, e?.Recovery.Target, e?.Recovery.Phase, e?.Recovery.RemainingDays, e?.Recovery.Progress,
                read?.PlantStock, read?.PlantCapacity, b?.plantGrowthToday, b?.consumedBiomassToday,
                b?.algaeBiomass, e?.MonsoonId, e?.MonsoonApplied, e?.HasLocalClimate,
                starvationLock && pos == starvationTile);
            if (b?.community == null) continue;
            for (int i = 0; i < b.community.Count; i++)
            {
                PopulationData p = b.community[i]; if (p == null) continue;
                Row(populations, seq, sample, clock.currentDay, Now(), pos.x, pos.y, i,
                    Name(p.species), p.speciesId, p.lineageName, p.ecologicalNiche, p.landPositionBank,
                    p.speciesAmount, p.trophicLevel, p.fitTemperature, p.fitHumidity, p.movementAbility,
                    p.size, p.fertility, p.environmentalFitness, p.energyNeed, p.allocatedBiomass,
                    p.carryingCapacity, p.energySatisfactionToday, p.actualEnergySatisfactionToday,
                    p.birthsToday, p.deathsToday, p.energyReserve, p.reserveAtDayStart,
                    p.huntingEnergyToday, p.predationDeathsToday, p.populationBeforePredation,
                    p.nextMigrationDay,
                    p.mutationDaysElapsed, p.previousMutationPopulation);
            }
        }
    }

    private void Action(string name, string input, Func<string> execute)
    {
        if (!ready) return;
        int seq = ++sequence;
        Snapshot("before_" + name, seq);
        string result;
        try { result = execute(); }
        catch (Exception ex) { result = "EXCEPTION: " + ex; Event("harness_exception", result); }
        Snapshot("after_" + name, seq);
        Row(actions, seq, Now(), clock.currentDay, caseId, operatorName, name, input, result, note);
        Flush(); status = name + ": " + result;
    }
    private bool Coord(out Vector2Int pos)
    {
        bool xValid = int.TryParse(x, NumberStyles.Integer, Invariant, out int xx);
        bool yValid = int.TryParse(y, NumberStyles.Integer, Invariant, out int yy);
        bool valid = xValid && yValid;
        pos = new Vector2Int(xx, yy); return valid;
    }
    private static bool Integer(string value, out int parsed) => int.TryParse(value, NumberStyles.Integer, Invariant, out parsed);
    private static bool Number(string value, out float parsed) => float.TryParse(value, NumberStyles.Float, Invariant, out parsed);
    private static string Result(bool success, string error = null) => success ? "OK" : "REJECTED: " + (error ?? "API returned false");

    private void Place()
    {
        string input = $"x={x};y={y};preset={presetIndex};rotation={rotation}";
        Action("place_tile", input, () =>
        {
            if (!Coord(out var pos) || !Integer(rotation, out int r) || tilePresets == null ||
                presetIndex < 0 || presetIndex >= tilePresets.Length || tilePresets[presetIndex] == null)
                return "INVALID INPUT";
            return Result(grid.TryPlaceTile(pos, tilePresets[presetIndex], r));
        });
    }
    private void SetHeight()
    {
        Action("set_height", $"x={x};y={y};height={tileHeight}", () =>
        {
            if (!Coord(out var pos) || !Integer(tileHeight, out int h)) return "INVALID INPUT";
            return Result(grid.TrySetTileHeight(pos, h));
        });
    }
    private void LoadEnvironmentFields()
    {
        if (!Coord(out var pos) || !bridge.TryGetBlock(pos, out var b)) return;
        editT = b.temperature.ToString(Invariant); editH = b.humidity.ToString(Invariant);
        editR = b.habitatRecovery.ToString(Invariant); editS = b.plantBiomass.ToString(Invariant);
        editZ = b.elevation.ToString(Invariant);
    }
    private void EditEnvironment()
    {
        Action("setup_environment", $"x={x};y={y};T={editT};H={editH};R={editR};S={editS};Z={editZ}", () =>
        {
            if (!Coord(out var pos) || !Integer(editT, out int t) || !Integer(editH, out int h) ||
                !Integer(editR, out int r) || !Number(editS, out float s) || !Integer(editZ, out int z))
                return "INVALID INPUT";
            var edit = new SimulationEnvironmentEdit
            { temperature = t, humidity = h, habitatRecovery = r, plantBiomass = s,
              maxPlantBiomass = EnvironmentRules.PlantCapacity, elevation = z };
            return Result(interventions.TryApplyEnvironment(pos, edit, out string error), error);
        });
    }
    private bool ApplyStarvationClamp(bool clearStock, out string error)
    {
        error = null;
        if (!bridge.TryGetBlock(starvationTile, out var block))
        { error = "tile missing"; return false; }
        var edit = new SimulationEnvironmentEdit
        {
            temperature = block.temperature, humidity = block.humidity,
            habitatRecovery = 0, elevation = block.elevation,
            maxPlantBiomass = EnvironmentRules.PlantCapacity,
            plantBiomass = clearStock ? 0f : block.plantBiomass
        };
        return interventions.TryApplyEnvironment(starvationTile, edit, out error);
    }
    private void StartStarvationFixture()
    {
        Action("start_S02_starvation_lock", $"x={x};y={y};R=0;S=0;clamp_before_each_ecology_day=true", () =>
        {
            if (starvationLock) return "REJECTED: stop the current starvation lock first";
            if (!Coord(out var pos)) return "INVALID COORDINATE";
            if (environment.TryRead(pos, out var state) &&
                (state.Environment.HasLocalClimate || state.Environment.HasMonsoon))
                return "REJECTED: stop climate/monsoon effects or start a fresh Play run";
            starvationTile = pos;
            bool success = ApplyStarvationClamp(true, out string error);
            starvationLock = success;
            return Result(success, error);
        });
    }
    private void StopStarvationFixture() =>
        Action("stop_S02_starvation_lock", "", () => { starvationLock = false; return "OK"; });
    private void Intervene()
    {
        string input = $"x={x};y={y};kind={Kinds[kindIndex]};amount={amount}";
        Action("intervention", input, () =>
        {
            if (!Coord(out var pos) || !Number(amount, out float a)) return "INVALID INPUT";
            var request = new SimulationInterventionRequest
            { center = pos, radius = 0, kind = (SimulationInterventionKind)kindIndex, amount = a };
            bool ok = interventions.TryApply(request, out var preview);
            return Result(ok, preview?.Error);
        });
    }
    private void Cancel(EnvironmentAttribute attribute)
    {
        Action("cancel_" + attribute, $"x={x};y={y}", () =>
        {
            if (!Coord(out var pos)) return "INVALID INPUT";
            return Result(environment.TryCancel(pos, attribute, out string error), error);
        });
    }
    private void DeployMonsoon()
    {
        Action("deploy_monsoon", $"x={x};y={y};T={tempOffset};H={humidityOffset}", () =>
        {
            if (!Coord(out var pos) || !Integer(tempOffset, out int t) || !Integer(humidityOffset, out int h))
                return "INVALID INPUT";
            bool ok = environment.TryDeployMonsoon(pos, t, h, out long id, out string error);
            if (ok) monsoonId = id.ToString(Invariant);
            return Result(ok, error) + ";id=" + id;
        });
    }
    private void CancelMonsoon()
    {
        Action("cancel_monsoon", "id=" + monsoonId, () =>
        {
            if (!long.TryParse(monsoonId, out long id)) return "INVALID INPUT";
            return Result(environment.TryCancelMonsoon(id, out string error), error);
        });
    }
    private void SeedPopulation()
    {
        Action("seed_population", $"x={x};y={y};species={fixtures[speciesIndex].name};count={count};match_target={matchTargetTraits}", () =>
        {
            if (!Coord(out var pos) || !Integer(count, out int n) || n <= 0 || !bridge.TryGetBlock(pos, out var block))
                return "INVALID INPUT OR TILE";
            SpeciesData s = fixtures[speciesIndex];
            if (block.community.Exists(resident => resident != null &&
                resident.speciesAmount > 0 && resident.species == s &&
                resident.ecologicalNiche == s.baseEcologicalNiche))
                return "REJECTED: species already on tile; use fresh Play for a clean fixture";
            SpeciesData traits = matchTargetTraits && s.evolutionTargets.Count > 0 ? s.evolutionTargets[0] : s;
            PopulationData draft = Draft(s, traits, n);
            var edits = new List<SimulationPopulationEdit>();
            foreach (PopulationData resident in block.community)
                if (resident != null) edits.Add(new SimulationPopulationEdit { Resident = resident, Draft = resident });
            edits.Add(new SimulationPopulationEdit { Draft = draft });
            return Result(interventions.TryApplyCommunity(pos, edits, out string error), error);
        });
    }
    private static PopulationData Draft(SpeciesData s, SpeciesData traits, int n) => new PopulationData
    {
        species = s, lineageName = s.name, speciesAmount = n,
        ecologicalNiche = s.baseEcologicalNiche,
        trophicLevel = s.trophicLevel, trophicLevelInitialized = true,
        movementAbility = traits.baseMovementAbility, habitatNiche = s.baseHabitatNiche,
        fitTemperature = traits.baseFitTemperature, fitHumidity = traits.baseFitHumidity,
        size = traits.baseSize, fertility = traits.baseFertility
    };
    private void SetupPredationFixture()
    {
        Action("setup_S03_clean_food_chain", "x=0;y=0;A=100;L1=20;L2=10;Day=0;fresh_scene=true", () =>
        {
            Vector2Int pos = Vector2Int.zero;
            if (clock.currentDay != 0 || !bridge.TryGetBlock(pos, out var block) ||
                block.community == null || block.community.Count != 0 || starvationLock)
                return "REJECTED: exit Play and re-enter for an empty Day 0 scene";
            if (!environment.TryRead(pos, out var state) || state.Environment.HasMonsoon ||
                state.Environment.HasLocalClimate || block.temperature != 68 ||
                block.humidity != 68 || block.habitatRecovery != 100000 ||
                Mathf.Abs(block.plantBiomass - 100000f) > 0.01f)
                return "REJECTED: environment is not the fresh grassland baseline";
            var edits = new List<SimulationPopulationEdit>
            {
                new SimulationPopulationEdit { Draft = Draft(fixtures[0], fixtures[0], 100) },
                new SimulationPopulationEdit { Draft = Draft(fixtures[2], fixtures[2], 20) },
                new SimulationPopulationEdit { Draft = Draft(fixtures[3], fixtures[3], 10) }
            };
            return Result(interventions.TryApplyCommunity(pos, edits, out string error), error);
        });
    }
    private void RemoveFixturePopulation()
    {
        Action("remove_fixture_population", $"x={x};y={y};species={fixtures[speciesIndex].name}", () =>
        {
            if (!Coord(out var pos) || !bridge.TryGetBlock(pos, out var block)) return "INVALID TILE";
            var edits = new List<SimulationPopulationEdit>();
            foreach (PopulationData resident in block.community)
                if (resident != null && resident.species != fixtures[speciesIndex])
                    edits.Add(new SimulationPopulationEdit { Resident = resident, Draft = resident });
            return Result(interventions.TryApplyCommunity(pos, edits, out string error), error);
        });
    }
    private void CreateFixtures()
    {
        fixtures.Add(MakeSpecies("Lab A herbivore", 0, 50, 50, 30, 10, 50));
        fixtures.Add(MakeSpecies("Lab B herbivore", 0, 55, 55, 35, 12, 55));
        fixtures.Add(MakeSpecies("Lab L1 predator", 1, 50, 50, 30, 15, 40));
        fixtures.Add(MakeSpecies("Lab L2 predator", 2, 50, 50, 30, 20, 35));
        fixtures[0].evolutionTargets.Add(fixtures[1]);
        fixtures[1].evolutionTargets.Add(fixtures[0]);
    }
    private static SpeciesData MakeSpecies(string name, int level, int t, int h, int move, int size, int fertility)
    {
        SpeciesData s = ScriptableObject.CreateInstance<SpeciesData>();
        s.name = name; s.trophicLevel = level; s.baseFitTemperature = t; s.baseFitHumidity = h;
        s.baseMovementAbility = move; s.baseSize = size; s.baseFertility = fertility;
        return s;
    }

    private void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (GUI.Button(new Rect(8, 8, 110, 28), show ? "Hide V4.1 QA" : "Show V4.1 QA")) show = !show;
        if (!show) return;
        GUILayout.BeginArea(new Rect(8, 40, 480, Screen.height - 48), GUI.skin.box);
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Label("V4.1 REGRESSION LAB  |  Day " + (clock?.currentDay ?? -1));
        GUILayout.Label(status);
        GUILayout.Label("Run folder (select and copy):");
        GUILayout.TextField(runPath);
        GUILayout.BeginHorizontal(); GUILayout.Label("Case", GUILayout.Width(55)); caseId = GUILayout.TextField(caseId);
        GUILayout.Label("Tester", GUILayout.Width(50)); operatorName = GUILayout.TextField(operatorName); GUILayout.EndHorizontal();
        GUILayout.Label("Action / observation note (record game UI clicks here)"); note = GUILayout.TextField(note);
        if (GUILayout.Button("Record observation / checkpoint"))
            Action("observation", note, () => "RECORDED");
        GUILayout.Space(8);
        GUILayout.BeginHorizontal(); GUILayout.Label("X", GUILayout.Width(15)); x = GUILayout.TextField(x, GUILayout.Width(45));
        GUILayout.Label("Y", GUILayout.Width(15)); y = GUILayout.TextField(y, GUILayout.Width(45));
        GUILayout.Label("Amount", GUILayout.Width(53)); amount = GUILayout.TextField(amount, GUILayout.Width(65));
        GUILayout.Label("Count", GUILayout.Width(45)); count = GUILayout.TextField(count, GUILayout.Width(65)); GUILayout.EndHorizontal();
        DrawSelectedTile();
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Pause")) Action("pause", "", () => { clock.Pause(); return "OK"; });
        if (GUILayout.Button("Play")) Action("play", "", () => { clock.Play(); return "OK"; });
        if (GUILayout.Button("2x")) Action("double_speed", "", () => { clock.DoubleSpeed(); return "OK"; });
        if (GUILayout.Button("+1 day")) Step(1);
        if (GUILayout.Button("+7 days")) Step(7);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Label("Seconds/day", GUILayout.Width(90)); secondsPerDay = GUILayout.TextField(secondsPerDay, GUILayout.Width(65));
        if (GUILayout.Button("Set")) Action("seconds_per_day", secondsPerDay, () =>
        { if (!Number(secondsPerDay, out float n)) return "INVALID INPUT"; clock.SetSecondsPerDay(n); return "OK: " + clock.SecondsPerDay; });
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Label("Seed", GUILayout.Width(40)); seedText = GUILayout.TextField(seedText, GUILayout.Width(90));
        if (GUILayout.Button("Apply RNG seed")) Action("random_seed", seedText, () =>
        { if (!Integer(seedText, out int n)) return "INVALID INPUT"; UnityEngine.Random.InitState(n); return "OK"; });
        GUILayout.EndHorizontal();
        DrawToggles();
        GUILayout.Space(8);
        GUILayout.Label("Tile preset: " + (tilePresets != null && tilePresets.Length > 0 ? tilePresets[presetIndex]?.displayName : "none"));
        if (GUILayout.Button("Next preset")) presetIndex = tilePresets == null || tilePresets.Length == 0 ? 0 : (presetIndex + 1) % tilePresets.Length;
        GUILayout.BeginHorizontal(); GUILayout.Label("Rotation 0..3", GUILayout.Width(100)); rotation = GUILayout.TextField(rotation, GUILayout.Width(50));
        if (GUILayout.Button("Place tile")) Place(); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Label("Height 0..2", GUILayout.Width(100)); tileHeight = GUILayout.TextField(tileHeight, GUILayout.Width(50));
        if (GUILayout.Button("Set tile height")) SetHeight(); GUILayout.EndHorizontal();
        if (GUILayout.Button("Load selected tile into setup fields")) LoadEnvironmentFields();
        GUILayout.BeginHorizontal(); GUILayout.Label("T", GUILayout.Width(15)); editT = GUILayout.TextField(editT, GUILayout.Width(50));
        GUILayout.Label("H", GUILayout.Width(15)); editH = GUILayout.TextField(editH, GUILayout.Width(50));
        GUILayout.Label("R", GUILayout.Width(15)); editR = GUILayout.TextField(editR, GUILayout.Width(65));
        GUILayout.Label("S", GUILayout.Width(15)); editS = GUILayout.TextField(editS, GUILayout.Width(65));
        GUILayout.Label("Z", GUILayout.Width(15)); editZ = GUILayout.TextField(editZ, GUILayout.Width(35)); GUILayout.EndHorizontal();
        if (GUILayout.Button("Apply raw environment setup (not player intervention)")) EditEnvironment();
        GUILayout.Label("S02 starvation fixture: one-time R=0/S=0, then clamp R before each ecology day");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Start S02 starvation lock")) StartStarvationFixture();
        if (GUILayout.Button("Stop S02 lock")) StopStarvationFixture();
        GUILayout.EndHorizontal();
        GUILayout.Label("Intervention: " + Kinds[kindIndex]);
        if (GUILayout.Button("Next intervention kind")) kindIndex = (kindIndex + 1) % Kinds.Length;
        if (GUILayout.Button("Apply intervention")) Intervene();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Cancel T")) Cancel(EnvironmentAttribute.Temperature);
        if (GUILayout.Button("Cancel H")) Cancel(EnvironmentAttribute.Humidity);
        if (GUILayout.Button("Cancel R")) Cancel(EnvironmentAttribute.Recovery);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Label("Monsoon T", GUILayout.Width(80)); tempOffset = GUILayout.TextField(tempOffset, GUILayout.Width(55));
        GUILayout.Label("H", GUILayout.Width(15)); humidityOffset = GUILayout.TextField(humidityOffset, GUILayout.Width(55));
        if (GUILayout.Button("Deploy")) DeployMonsoon(); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Label("Monsoon ID", GUILayout.Width(85)); monsoonId = GUILayout.TextField(monsoonId, GUILayout.Width(90));
        if (GUILayout.Button("Cancel")) CancelMonsoon(); GUILayout.EndHorizontal();
        GUILayout.Label("Fixture: " + fixtures[speciesIndex].name);
        if (GUILayout.Button("Next species")) speciesIndex = (speciesIndex + 1) % fixtures.Count;
        matchTargetTraits = GUILayout.Toggle(matchTargetTraits, "Match linked target traits (A/B evolution)");
        if (GUILayout.Button("Seed population at X,Y")) SeedPopulation();
        if (GUILayout.Button("Set up fresh S03 food chain (A/L1/L2 = 100/20/10)")) SetupPredationFixture();
        if (GUILayout.Button("Remove selected fixture population at X,Y")) RemoveFixturePopulation();
        GUILayout.Space(8);
        if (GUILayout.Button("Export / flush CSV now"))
            Action("export", runPath, () => { Flush(); return "FILES READY: " + runPath; });
        GUILayout.EndScrollView(); GUILayout.EndArea();
    }

    private void DrawToggles()
    {
        if (simulation == null) return;
        SimulationTuning tuning = simulation.ReadTuning();
        GUILayout.BeginHorizontal();
        bool migration = GUILayout.Toggle(tuning.migrationEnabled, "Migration");
        bool mutation = GUILayout.Toggle(tuning.mutationEnabled, "Mutation");
        bool niches = GUILayout.Toggle(tuning.ecologicalNichesEnabled, "Niches");
        GUILayout.EndHorizontal();
        if (migration != tuning.migrationEnabled || mutation != tuning.mutationEnabled || niches != tuning.ecologicalNichesEnabled)
            Action("set_toggles", $"migration={migration};mutation={mutation};niches={niches}", () =>
            { tuning.migrationEnabled = migration; tuning.mutationEnabled = mutation; tuning.ecologicalNichesEnabled = niches;
              simulation.ApplyTuning(tuning); return "OK"; });
    }
    private void Step(int days) => Action("advance_days", "days=" + days, () =>
    { clock.Pause(); for (int i = 0; i < days; i++) clock.NextDay(); return "OK"; });
    private void DrawSelectedTile()
    {
        if (!ready || !Coord(out var pos) || !grid.TryGetTile(pos, out MapTileInstance tile))
        { GUILayout.Label("Selected tile: missing / invalid coordinates"); return; }
        BlockInfo b = tile.Block; environment.TryRead(pos, out var read);
        if (read == null) { GUILayout.Label("Selected tile: awaiting environment sync"); return; }
        var e = read.Environment;
        GUILayout.Label($"[{pos.x},{pos.y}] {tile.Definition.displayName} / {e.Terrain} / height {tile.HeightLevel} / {b?.waterCoverage}");
        GUILayout.Label($"T {e.Temperature.Value:F1} → {e.Temperature.Target:F1} {e.Temperature.Phase} {e.Temperature.RemainingDays}d | H {e.Humidity.Value:F1} → {e.Humidity.Target:F1} {e.Humidity.Phase} {e.Humidity.RemainingDays}d");
        GUILayout.Label($"R {e.Recovery.Value:F0} → {e.Recovery.Target:F0} {e.Recovery.Phase} {e.Recovery.RemainingDays}d | Plant {read.PlantStock:F0}/{read.PlantCapacity} | growth {b?.plantGrowthToday:F0} / eaten {b?.consumedBiomassToday:F0}");
        GUILayout.Label($"Monsoon {e.MonsoonId?.ToString() ?? "none"} applied={e.MonsoonApplied} local={e.HasLocalClimate} | populations {b?.community?.Count ?? 0} | S02 lock={(starvationLock && pos == starvationTile ? "ON" : "off")}");
        if (b?.community != null) foreach (PopulationData p in b.community)
            if (p != null) GUILayout.Label($"• {Name(p.species)} {p.speciesAmount} N={p.ecologicalNiche} L={p.trophicLevel} F={p.environmentalFitness} K={p.carryingCapacity:F0} sat={p.energySatisfactionToday:F2} birth/death={p.birthsToday}/{p.deathsToday} predation={p.predationDeathsToday} reserve={p.energyReserve:F2} hunt={p.huntingEnergyToday:F2}");
    }
}
