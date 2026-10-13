using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum DirectedTrait { Temperature, Humidity, Movement, Fertility, Size }

[DisallowMultipleComponent]
public sealed class PlayerEconomyController : MonoBehaviour
{
    public sealed class HarvestPreview
    {
        public int Harvested { get; internal set; }
        public int Remaining { get; internal set; }
        public long Earned { get; internal set; }
        public int NextDailyIncome { get; internal set; }
        public bool SpeciesExtinct { get; internal set; }
    }

    public sealed class WaterShapePreview
    {
        public int OpeningsBefore { get; internal set; }
        public int OpeningsAfter { get; internal set; }
        public int WaterAreaBefore { get; internal set; }
        public int WaterAreaAfter { get; internal set; }
        public int ResidentGroupsInCurrentArea { get; internal set; }
    }

    [Serializable]
    private sealed class SavedStorage
    {
        public List<SavedBackup> entries = new List<SavedBackup>();
    }

    [Serializable]
    private sealed class SavedBackup
    {
        public string id, speciesKey, lineageName, speciesId;
        public EcologicalNiche niche;
        public int trophicLevel, movement, habitatNiche, temperature, humidity, size,
            fertility, nextEvolutionDay, nextMigrationDay;
    }

    [Serializable]
    public sealed class StoredPopulation
    {
        public string id;
        public PopulationData copy;
    }

    private static readonly Vector2Int[] Directions =
    { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
    private readonly List<StoredPopulation> storage = new List<StoredPopulation>();
    private MapGridManager grid;
    private MapSimulationBridge bridge;
    private SimulationController simulation;
    private SimulationTime clock;
    private BioenergyAccount account;
    private bool hasSeenWildPopulation;
    public IReadOnlyList<StoredPopulation> Storage => storage.AsReadOnly();
    public event Action GameLost;
    public bool IsGameLost { get; private set; }

    public string CaptureStorageState()
    {
        var state = new SavedStorage();
        foreach (StoredPopulation entry in storage)
        {
            PopulationData copy = entry.copy;
            state.entries.Add(new SavedBackup
            {
                id = entry.id, speciesKey = BioenergyAccount.SpeciesKey(copy),
                lineageName = copy.lineageName, speciesId = copy.speciesId,
                niche = copy.ecologicalNiche, trophicLevel = copy.trophicLevel,
                movement = copy.movementAbility, habitatNiche = copy.habitatNiche,
                temperature = copy.fitTemperature, humidity = copy.fitHumidity,
                size = copy.size, fertility = copy.fertility,
                nextEvolutionDay = copy.nextEvolutionDay,
                nextMigrationDay = copy.nextMigrationDay
            });
        }
        return JsonUtility.ToJson(state);
    }

    // Load with the account snapshot after the map, species graph and clock are restored.
    public bool TryRestoreStorageState(string json)
    {
        if (simulation == null || string.IsNullOrWhiteSpace(json)) return false;
        SavedStorage state;
        try { state = JsonUtility.FromJson<SavedStorage>(json); }
        catch { return false; }
        if (state == null || state.entries == null) return false;
        var restored = new List<StoredPopulation>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (SavedBackup entry in state.entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.id) || !ids.Add(entry.id) ||
                string.IsNullOrEmpty(entry.speciesKey) || entry.size < 1 || entry.size > 100 ||
                entry.movement < 0 || entry.movement > 100 || entry.fertility < 0 ||
                entry.fertility > 100 || entry.temperature < 0 || entry.temperature > 100 ||
                entry.humidity < 0 || entry.humidity > 100) return false;
            SpeciesData species = simulation.FindSpeciesByEconomyId(entry.speciesKey);
            if (species == null && string.IsNullOrEmpty(entry.speciesId) &&
                string.IsNullOrEmpty(entry.lineageName)) return false;
            var copy = new PopulationData
            {
                species = species, lineageName = entry.lineageName,
                speciesId = entry.speciesId, ecologicalNiche = entry.niche,
                speciesAmount = 50, trophicLevel = entry.trophicLevel,
                trophicLevelInitialized = true, movementAbility = entry.movement,
                habitatNiche = entry.habitatNiche, fitTemperature = entry.temperature,
                fitHumidity = entry.humidity, size = entry.size, fertility = entry.fertility,
                nextEvolutionDay = entry.nextEvolutionDay,
                nextMigrationDay = entry.nextMigrationDay
            };
            restored.Add(new StoredPopulation { id = entry.id, copy = copy });
        }
        storage.Clear(); storage.AddRange(restored);
        IsGameLost = false;
        hasSeenWildPopulation = account != null && account.HasEverHadWildPopulation;
        return true;
    }

    private void Awake()
    {
        grid = GetComponent<MapGridManager>();
        bridge = GetComponent<MapSimulationBridge>();
        simulation = GetComponent<SimulationController>();
        clock = GetComponent<SimulationTime>();
        account = GetComponent<BioenergyAccount>();
    }

    private void OnEnable()
    {
        if (simulation != null) simulation.OnDaySimulated += CheckLoss;
    }

    private void OnDisable()
    {
        if (simulation != null) simulation.OnDaySimulated -= CheckLoss;
    }

    public bool TryExpand(Vector2Int coordinate, MapTileDefinition definition,
        int rotation, out string error)
    {
        error = null;
        if (account == null || grid == null || definition == null || !grid.CanPlaceAt(coordinate))
        { error = "目标地块不可建造"; return false; }
        return account.TryExecute(BioenergySettings.Current.expandTileCost, "地块扩张", (_, __) =>
            grid.TryPlaceTile(coordinate, definition, rotation), out error);
    }

    public bool CanDemolish(Vector2Int coordinate, out string error)
    {
        error = null;
        if (grid == null || simulation == null || coordinate == Vector2Int.zero ||
            !grid.TryGetTile(coordinate, out MapTileInstance tile) || tile.Block == null)
        { error = "初始地块或不存在的地块不能拆除"; return false; }
        if (tile.Block.community != null)
            foreach (PopulationData population in tile.Block.community)
                if (population != null && population.speciesAmount > 0)
                { error = "地块上有种群"; return false; }
        foreach (BlockInfo block in simulation.ReadBlocks())
            if (block != null && block.community != null)
                foreach (PopulationData population in block.community)
                    if (population != null && population.speciesAmount > 0 &&
                        population.landPositionBlock == tile.Block)
                    { error = "有种群占用此处河岸"; return false; }
        var remaining = new HashSet<Vector2Int>();
        foreach (MapTileInstance placed in grid.GetPlacedTiles())
            if (placed.Coordinate != coordinate) remaining.Add(placed.Coordinate);
        var visited = new HashSet<Vector2Int> { Vector2Int.zero };
        var queue = new Queue<Vector2Int>(); queue.Enqueue(Vector2Int.zero);
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            foreach (Vector2Int direction in Directions)
            {
                Vector2Int next = current + direction;
                if (remaining.Contains(next) && visited.Add(next)) queue.Enqueue(next);
            }
        }
        if (visited.Count != remaining.Count)
        { error = "拆除会切断其他地块与初始地块的连接"; return false; }
        return true;
    }

    public bool TryDemolish(Vector2Int coordinate, out string error)
    {
        if (!CanDemolish(coordinate, out error)) return false;
        return account.TryExecute(BioenergySettings.Current.demolishTileCost, "拆除地块", (_, __) =>
            CanDemolish(coordinate, out _) && grid.TryRemoveTile(coordinate), out error);
    }

    public bool TryPreviewWaterShape(Vector2Int coordinate, MapTileDefinition shape,
        int rotation, out WaterShapePreview preview, out string error)
    {
        preview = null; error = null;
        if (grid == null || account == null || shape == null ||
            shape.initialWaterCoverage == WaterCoverage.Land ||
            !grid.TryGetTile(coordinate, out MapTileInstance tile) ||
            tile.Block == null || tile.Block.waterCoverage == WaterCoverage.Land ||
            (tile.Definition == shape &&
                WaterBodyTopology.OpeningMask(tile.Definition, tile.RotationSteps) ==
                WaterBodyTopology.OpeningMask(shape, rotation)))
        { error = "水道改造目标无效或没有变化"; return false; }
        var before = WaterBodyTopology.Area(grid, coordinate);
        int groups = 0;
        foreach (MapTileInstance member in before)
            if (member.Block != null && member.Block.community != null)
                foreach (PopulationData resident in member.Block.community)
                    if (resident != null && resident.speciesAmount > 0) groups++;
        preview = new WaterShapePreview
        {
            OpeningsBefore = WaterBodyTopology.OpeningMask(tile.Definition, tile.RotationSteps),
            OpeningsAfter = WaterBodyTopology.OpeningMask(shape, rotation),
            WaterAreaBefore = before.Count,
            WaterAreaAfter = PredictWaterAreaSize(coordinate, shape, rotation),
            ResidentGroupsInCurrentArea = groups
        };
        return true;
    }

    public bool TryReshapeWater(Vector2Int coordinate, MapTileDefinition shape,
        int rotation, out string error)
    {
        if (!TryPreviewWaterShape(coordinate, shape, rotation, out _, out error)) return false;
        return account.TryExecute(BioenergySettings.Current.reshapeWaterCost, "水道改造", (_, __) =>
            grid.TryChangeWaterShape(coordinate, shape, rotation), out error);
    }

    private int PredictWaterAreaSize(Vector2Int source, MapTileDefinition shape, int rotation)
    {
        var visited = new HashSet<Vector2Int> { source };
        var queue = new Queue<Vector2Int>(); queue.Enqueue(source);
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            if (!grid.TryGetTile(current, out MapTileInstance currentTile)) continue;
            int currentMask = current == source ?
                WaterBodyTopology.OpeningMask(shape, rotation) :
                WaterBodyTopology.OpeningMask(currentTile.Definition, currentTile.RotationSteps);
            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int next = current + Directions[i];
                if (visited.Contains(next) || !grid.TryGetTile(next, out MapTileInstance neighbor) ||
                    neighbor.Block == null || !HabitatTopology.IsWater(neighbor.Block) ||
                    neighbor.HeightLevel != currentTile.HeightLevel) continue;
                int nextMask = WaterBodyTopology.OpeningMask(neighbor.Definition, neighbor.RotationSteps);
                int currentBit = 1 << i;
                int oppositeBit = 1 << ((i + 2) % 4);
                if ((currentMask & currentBit) == 0 || (nextMask & oppositeBit) == 0) continue;
                visited.Add(next); queue.Enqueue(next);
            }
        }
        return visited.Count;
    }

    public bool TryPreviewHarvest(BlockInfo source, PopulationData population,
        out HarvestPreview preview, out string error)
    {
        preview = null; error = null;
        if (!IsResident(source, population)) { error = "种群不存在"; return false; }
        int count = population.speciesAmount;
        BioenergySettings settings = BioenergySettings.Current;
        int percentageCount = (int)Math.Min(count,
            ((long)count * settings.harvestPercent + 99) / 100);
        int harvested = Math.Min(count, Math.Max(percentageCount, settings.minimumHarvestCount));
        int value = population.species != null ?
            Mathf.Clamp(population.species.bioenergyValue, 1, 100) :
            settings.FallbackSpeciesValue(population.trophicLevel);
        long earned = harvested > 1 ? (long)Math.Floor(
            settings.harvestIncomeMultiplier * (double)value * Math.Log(harvested)) : 0;
        long globalCount = 0;
        string key = BioenergyAccount.SpeciesKey(population);
        foreach (BlockInfo block in simulation.ReadBlocks())
            if (block != null && block.community != null)
                foreach (PopulationData resident in block.community)
                    if (resident != null && resident.speciesAmount > 0 &&
                        BioenergyAccount.SpeciesKey(resident) == key)
                        globalCount += resident.speciesAmount;
        long after = globalCount - harvested;
        long beforeIncome = globalCount > 1 ? (long)Math.Floor(
            settings.dailyIncomeMultiplier * (double)value * Math.Log(globalCount)) : 0;
        long afterIncome = after > 1 ? (long)Math.Floor(
            settings.dailyIncomeMultiplier * (double)value * Math.Log(after)) : 0;
        long nextDaily = (account != null ? account.PreviewDailyIncome() : 0) -
            beforeIncome + afterIncome;
        preview = new HarvestPreview { Harvested = harvested, Remaining = count - harvested,
            Earned = earned, NextDailyIncome = (int)Math.Max(0, Math.Min(int.MaxValue, nextDaily)),
            SpeciesExtinct = after == 0 };
        return true;
    }

    public bool TryHarvest(BlockInfo source, PopulationData population,
        out int harvested, out long earned, out string error)
    {
        harvested = 0; earned = 0;
        if (!TryPreviewHarvest(source, population, out HarvestPreview preview, out error))
            return false;
        harvested = preview.Harvested;
        earned = preview.Earned;
        population.speciesAmount -= harvested;
        if (population.speciesAmount == 0) source.community.Remove(population);
        simulation.RefreshSpeciesStatuses();
        simulation.NotifyPlayerCommunityChanged(source);
        account.Grant(earned, "种群收割:" + BioenergyAccount.SpeciesKey(population));
        return true;
    }

    public bool CanPlacePopulation(BlockInfo target, PopulationData population,
        BlockInfo source, out string error)
    {
        error = null;
        if (target == null || population == null || population.speciesAmount <= 0 ||
            target.community == null || source == target || !ContainsBlock(target))
        { error = "目标不存在或与当前位置相同"; return false; }
        bool water = HabitatTopology.IsWater(target);
        if (population.ecologicalNiche == EcologicalNiche.Water ? !water : water)
        { error = "目标地块与生态位不匹配"; return false; }
        if (population.ecologicalNiche == EcologicalNiche.Air)
            foreach (PopulationData resident in target.community)
                if (resident != null && resident.speciesAmount > 0 &&
                    resident.ecologicalNiche == EcologicalNiche.Air &&
                    !PopulationTransfer.IsSameSpecies(resident, population))
                { error = "目标天空槽位已被其他物种占用"; return false; }
        if (SimulationController.LivingPopulationCount(target) >= 5 && !target.community.Exists(resident =>
            resident != null &&
            resident.ecologicalNiche == population.ecologicalNiche &&
            PopulationTransfer.IsSameSpecies(resident, population)))
        { error = "目标地块已有 5/5 个种群条目"; return false; }
        return true;
    }

    public bool TryMigrate(BlockInfo source, PopulationData population,
        BlockInfo target, out string error)
    {
        error = null;
        if (!IsResident(source, population) ||
            !CanPlacePopulation(target, population, source, out error))
        { if (error == null) error = "来源种群不存在"; return false; }
        return account.TryExecute(BioenergySettings.Current.migrateCost, "手动迁移", (_, __) =>
        {
            if (!IsResident(source, population) ||
                !CanPlacePopulation(target, population, source, out _)) return false;
            PopulationData arrival = PopulationTransfer.CopyForMove(population, population.speciesAmount);
            arrival.RetainPopulationId(population);
            arrival.evolutionConversions.Clear();
            arrival.energyReserve = population.energyReserve;
            arrival.birthRemainder = population.birthRemainder;
            arrival.deathRemainder = population.deathRemainder;
            arrival.lastMigrationSource = source;
            arrival.landPositionBlock = null;
            arrival.landPositionBank = default(RiverBankSide);
            source.community.Remove(population);
            AddOrMerge(target, arrival);
            simulation.RefreshSpeciesStatuses();
            simulation.NotifyPlayerCommunityChanged(source);
            simulation.NotifyPlayerCommunityChanged(target);
            return true;
        }, out error);
    }

    public bool TryDirectedEvolution(BlockInfo source, PopulationData population,
        DirectedTrait first, int firstDelta, DirectedTrait second, int secondDelta,
        out string error)
    {
        error = null;
        if (!IsResident(source, population) || first == second ||
            !ValidDelta(firstDelta) || !ValidDelta(secondDelta) ||
            (population.ecologicalNiche == EcologicalNiche.Water &&
                (first == DirectedTrait.Temperature || first == DirectedTrait.Humidity ||
                 second == DirectedTrait.Temperature || second == DirectedTrait.Humidity)))
        { error = "定向进化的目标或两项性状无效"; return false; }
        int nextFirst = NextTrait(population, first, firstDelta);
        int nextSecond = NextTrait(population, second, secondDelta);
        if (nextFirst == ReadTrait(population, first) &&
            nextSecond == ReadTrait(population, second))
        { error = "性状已到边界，操作不会产生变化"; return false; }
        return account.TryExecute(BioenergySettings.Current.directedEvolutionCost, "定向进化", (_, __) =>
        {
            if (!IsResident(source, population)) return false;
            SetTrait(population, first, nextFirst);
            SetTrait(population, second, nextSecond);
            simulation.NotifyPlayerCommunityChanged(source);
            return true;
        }, out error);
    }

    public bool TryStore(BlockInfo source, PopulationData population,
        out string storageId, out string error)
    {
        storageId = null; error = null;
        if (!IsResident(source, population)) { error = "复制来源种群不存在"; return false; }
        string id = Guid.NewGuid().ToString("N");
        bool success = account.TryExecute(BioenergySettings.Current.storageCost, "生态仓建造及复制", (_, __) =>
        {
            if (!IsResident(source, population)) return false;
            PopulationData copy = PopulationTransfer.CopyForMove(population, 50);
            copy.evolutionConversions.Clear();
            storage.Add(new StoredPopulation { id = id, copy = copy });
            return true;
        }, out error);
        if (success) storageId = id;
        return success;
    }

    public bool TryRelease(string storageId, BlockInfo target, out string error)
    {
        error = null;
        StoredPopulation backup = storage.Find(entry => entry.id == storageId);
        if (backup == null || !CanPlacePopulation(target, backup.copy, null, out error))
        { if (error == null) error = "生态仓备份不存在"; return false; }
        PopulationData arrival = PopulationTransfer.CopyForMove(backup.copy, 50);
        arrival.evolutionConversions.Clear();
        AddOrMerge(target, arrival);
        storage.Remove(backup);
        simulation.RefreshSpeciesStatuses();
        simulation.NotifyPlayerCommunityChanged(target);
        return true;
    }

    public void RestartGame()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid())
        {
            if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
#if UNITY_EDITOR
            else if (!string.IsNullOrEmpty(scene.path))
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                    scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#endif
        }
    }

    private void CheckLoss(int day)
    {
        if (IsGameLost || simulation == null) return;
        foreach (BlockInfo block in simulation.ReadBlocks())
            if (block != null && block.community != null)
                foreach (PopulationData population in block.community)
                    if (population != null && population.speciesAmount > 0)
                    { hasSeenWildPopulation = true; return; }
        if (!hasSeenWildPopulation || storage.Count > 0) return;
        IsGameLost = true;
        if (clock != null) clock.Pause();
        GameLost?.Invoke();
    }

    private bool ContainsBlock(BlockInfo target)
    {
        if (simulation == null) return false;
        foreach (BlockInfo block in simulation.ReadBlocks()) if (block == target) return true;
        return false;
    }

    private bool IsResident(BlockInfo source, PopulationData population) =>
        source != null && population != null && population.speciesAmount > 0 &&
        source.community != null && source.community.Contains(population) && ContainsBlock(source);

    private static void AddOrMerge(BlockInfo target, PopulationData arrival)
    {
        PopulationData survivor = null;
        foreach (PopulationData resident in target.community)
            if (resident != null &&
                resident.ecologicalNiche == arrival.ecologicalNiche &&
                PopulationTransfer.IsSameSpecies(resident, arrival))
            { survivor = resident; break; }
        if (survivor == null) { target.community.Add(arrival); return; }
        PopulationTransfer.Merge(survivor, arrival);
        for (int i = target.community.Count - 1; i >= 0; i--)
        {
            PopulationData other = target.community[i];
            if (other == survivor || other == null ||
                other.ecologicalNiche != arrival.ecologicalNiche ||
                !PopulationTransfer.IsSameSpecies(other, arrival)) continue;
            PopulationTransfer.Merge(survivor, other);
            target.community.RemoveAt(i);
        }
        survivor.localSplitBranch = false;
        survivor.directedSplitTarget = null;
        survivor.sameSpeciesCoexistenceDays = 0;
        survivor.splitStrategy = 0;
        survivor.mutationSelectionProgress = 0f;
        survivor.mutationSelectionCandidate = -1;
    }

    private static bool ValidDelta(int delta) =>
        delta == -50 || delta == -25 || delta == 25 || delta == 50;
    private static int ReadTrait(PopulationData population, DirectedTrait trait)
    {
        switch (trait)
        {
            case DirectedTrait.Temperature: return population.fitTemperature;
            case DirectedTrait.Humidity: return population.fitHumidity;
            case DirectedTrait.Movement: return population.movementAbility;
            case DirectedTrait.Fertility: return population.fertility;
            default: return population.size;
        }
    }
    private static int NextTrait(PopulationData population, DirectedTrait trait, int delta) =>
        Mathf.Clamp(ReadTrait(population, trait) + delta, trait == DirectedTrait.Size ? 1 : 0, 100);
    private static void SetTrait(PopulationData population, DirectedTrait trait, int value)
    {
        switch (trait)
        {
            case DirectedTrait.Temperature:
                population.fitTemperature = value; population.temperatureMutationRemainder = 0; break;
            case DirectedTrait.Humidity:
                population.fitHumidity = value; population.humidityMutationRemainder = 0; break;
            case DirectedTrait.Movement:
                population.movementAbility = value; population.movementMutationRemainder = 0; break;
            case DirectedTrait.Fertility:
                population.fertility = value; population.fertilityMutationRemainder = 0; break;
            case DirectedTrait.Size:
                population.size = value; population.sizeMutationRemainder = 0; break;
        }
    }
}
