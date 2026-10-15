using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DarwinFarm.Environment;

// Uses production ecology/environment code. Synthetic state probes are labelled
// separately from legal player-operation runs and never count as reachability.
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static string Root, Out;
    private static Dictionary<string, SpeciesData> Catalog;
    private static List<(SpeciesData from, SpeciesData to)> Edges;
    private static List<(SpeciesData from, SpeciesData to)> AllEdges;
    private static readonly int[] ClimateAmounts = { -50, -25, 25, 50 };

    private static void Main(string[] args)
    {
        if (args.Contains("--rule-change-smoke"))
        {
            RuleChangeSmoke.Run();
            return;
        }
        Root = Directory.GetCurrentDirectory();
        if (!Directory.Exists(Path.Combine(Root, "Assets", "BiowebV46")))
            throw new Exception("Run from the Unity project directory.");
        Out = Path.Combine(Root, "Tools", "EvolutionDesignAudit", "Results");
        Directory.CreateDirectory(Out);
        foreach (string name in new[] { "resource-probes.json", "first-edge-single-operations.json" })
        {
            string baseline = Path.Combine(Out, "baseline-" + name);
            if (File.Exists(Path.Combine(Out, name)) && !File.Exists(baseline))
                File.Copy(Path.Combine(Out, name), baseline);
        }
        Load();
        var timer = Stopwatch.StartNew();
        Structure();
        ResourceProbes();
        StockBufferRegression();
        WholePreyHuntRegression();
        if (!args.Contains("--evolution-only")) LocalFoodSupportAudit();
        if (args.Contains("--single-operations")) SingleOperations();
        if (args.Contains("--all-directions")) AllDirectionOperations();
        if (args.Contains("--robust")) RobustRetest();
        if (args.Contains("--gate-diagnostics")) GateDiagnostics();
        if (args.Contains("--mushroom-cause")) MushroomCauseDiagnostics();
        Console.WriteLine($"Elapsed {timer.Elapsed.TotalSeconds:F2}s; reports: {Out}");
    }

    private static int Field(string text, string key) => int.Parse(
        Regex.Match(text, @"(?m)^  " + key + @": (\d+)\s*$").Groups[1].Value);

    private static void Load()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,
            "Assets", "BiowebV46", "bioweb.json")));
        var ids = new Dictionary<string, SpeciesData>();
        var assetTexts = new Dictionary<SpeciesData, string>();
        var assetGuids = new Dictionary<SpeciesData, string>();
        Catalog = new();
        foreach (var row in json.RootElement.GetProperty("species").EnumerateArray())
        {
            string id = row.GetProperty("id").GetString(), name = row.GetProperty("name").GetString();
            string asset = Directory.GetFiles(Path.Combine(Root, "Assets", "BiowebV46", "Species"),
                $"*-{id.Substring(0, 8)}.asset").Single();
            string text = File.ReadAllText(asset);
            var s = new SpeciesData {
                name = name, trophicLevel = Field(text, "trophicLevel"),
                baseMovementAbility = Field(text, "baseMovementAbility"),
                baseSize = Field(text, "baseSize"), baseFertility = Field(text, "baseFertility"),
                baseFitTemperature = Field(text, "baseFitTemperature"),
                baseFitHumidity = Field(text, "baseFitHumidity"),
                baseEcologicalNiche = (EcologicalNiche)Field(text, "baseEcologicalNiche")
            };
            typeof(SpeciesData).GetField("speciesName", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(s, name);
            typeof(SpeciesData).GetField("speciesId", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(s, id.Replace("-", ""));
            ids[id] = s; Catalog[name] = s;
            assetTexts[s] = text;
            assetGuids[s] = Regex.Match(File.ReadAllText(asset + ".meta"),
                @"(?m)^guid: ([0-9a-f]{32})").Groups[1].Value;
        }
        Edges = new(); AllEdges = new();
        foreach (var row in json.RootElement.GetProperty("evolutionLinks").EnumerateArray())
        {
            var a = ids[row.GetProperty("from").GetString()];
            var b = ids[row.GetProperty("to").GetString()];
            a.evolutionTargets.Add(b); b.evolutionTargets.Add(a);
            AllEdges.Add((a, b));
            if (a.baseEcologicalNiche == EcologicalNiche.Land &&
                b.baseEcologicalNiche == EcologicalNiche.Land &&
                a.name != "飞狸" && b.name != "飞狸") Edges.Add((a, b));
        }
        foreach (var row in json.RootElement.GetProperty("foodLinks").EnumerateArray())
            ids[row.GetProperty("to").GetString()].preySpecies.Add(ids[row.GetProperty("from").GetString()]);
        foreach (var s in Catalog.Values)
        foreach (var relation in new[] {
            (field: "evolutionTargets", expected: s.evolutionTargets),
            (field: "preySpecies", expected: s.preySpecies) })
        {
            string section = Regex.Match(assetTexts[s], @"(?ms)^  " + relation.field +
                @":(.*?)(?=^  [A-Za-z])").Groups[1].Value;
            string[] actual = Regex.Matches(section, @"guid: ([0-9a-f]{32})")
                .Select(m => m.Groups[1].Value).OrderBy(x => x).ToArray();
            string[] expected = relation.expected.Select(x => assetGuids[x]).OrderBy(x => x).ToArray();
            if (!actual.SequenceEqual(expected)) throw new Exception(s.name + " asset/export link mismatch.");
        }
        if (AllEdges.Count != 20 || Edges.Count != 9)
            throw new Exception("Expected 20 formal edges and 9 selected land edges.");
        Check(!Catalog["鸡"].evolutionTargets.Contains(Catalog["孔雀"]) &&
            !Catalog["孔雀"].evolutionTargets.Contains(Catalog["鸡"]),
            "Withdrawn chicken/peacock connection was restored.");
        Check(!Catalog["鲨母"].evolutionTargets.Contains(Catalog["虾兔"]) &&
            !Catalog["虾兔"].evolutionTargets.Contains(Catalog["鲨母"]),
            "Withdrawn shark/rabbit connection was restored.");
        // Ensure trait values come from the assets consumed by Unity.
        foreach (var row in json.RootElement.GetProperty("species").EnumerateArray())
        {
            var s = ids[row.GetProperty("id").GetString()];
            if (s.baseSize != row.GetProperty("baseSize").GetInt32() ||
                s.baseMovementAbility != row.GetProperty("baseMovementAbility").GetInt32() ||
                s.baseFertility != row.GetProperty("baseFertility").GetInt32())
                throw new Exception("Asset/export trait mismatch.");
        }
    }

    private static int[] Traits(SpeciesData s) => new[] {
        s.baseFitTemperature, s.baseFitHumidity, s.baseMovementAbility, s.baseSize, s.baseFertility };
    private static int[] Traits(PopulationData p) => new[] {
        p.fitTemperature, p.fitHumidity, p.movementAbility, p.size, p.fertility };
    private static PopulationData Population(SpeciesData s, int count) => new() {
        species = s, lineageName = s.name, speciesAmount = count,
        ecologicalNiche = s.baseEcologicalNiche, habitatNiche = 50,
        fitTemperature = s.baseFitTemperature, fitHumidity = s.baseFitHumidity,
        movementAbility = s.baseMovementAbility, size = s.baseSize, fertility = s.baseFertility,
        trophicLevel = s.trophicLevel, trophicLevelInitialized = true
    };
    private static object TraitObject(SpeciesData s) => new {
        temperature = s.baseFitTemperature, humidity = s.baseFitHumidity,
        movement = s.baseMovementAbility, size = s.baseSize, fertility = s.baseFertility,
        trophicLevel = s.trophicLevel
    };

    private static void Save(string file, object data)
    {
        string path=Path.Combine(Out,file), temporary=path+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(data,JsonOptions));
        File.Move(temporary,path,true);
    }

    private static void Structure()
    {
        var rows = new List<object>();
        foreach (var edge in Edges)
            foreach (var direction in new[] { edge, (edge.to, edge.from) })
            {
                var a = direction.Item1; var b = direction.Item2;
                foreach (var s in new[] { a, b })
                    if (s.baseFitTemperature < 0 || s.baseFitTemperature > 100 ||
                        s.baseFitHumidity < 0 || s.baseFitHumidity > 100 ||
                        s.baseMovementAbility < 0 || s.baseMovementAbility > 100 ||
                        s.baseSize < 1 || s.baseSize > 100 ||
                        s.baseFertility < 10 || s.baseFertility > 90)
                        throw new Exception("Template outside mutation range.");
                var need = Traits(a).Zip(Traits(b), (x, y) => Math.Max(0, Math.Abs(x-y)-5)).ToArray();
                rows.Add(new {
                    from = a.name, to = b.name, source = TraitObject(a), target = TraitObject(b),
                    traitGapOutsideTolerance = need,
                    sourceFood = a.preySpecies.Select(x => x.name).ToArray(),
                    targetFood = b.preySpecies.Select(x => x.name).ToArray(),
                    targetCanEatSource = b.preySpecies.Contains(a),
                    additionalTargetPreyRequired = b.trophicLevel > 0 && !b.preySpecies.Contains(a),
                    onlySourcePreyIsTarget = a.trophicLevel > 0 &&
                        a.preySpecies.Count == 1 && a.preySpecies[0] == b,
                    possibleSplitJump = "body +/-30 and fertility -/+30; requires real split gates",
                    status = "structure recorded; player reachability not yet established"
                });
            }
        Save("structure.json", rows);
        Console.WriteLine("Structure: 18 selected directions, all template traits inside legal ranges.");
    }

    private static BlockInfo ProbeBlock(int count, float stock, int recovery) => new() {
        elevation = 0, temperature = 55, humidity = 35,
        plantBiomass = stock, maxPlantBiomass = 1000000, habitatRecovery = recovery,
        community = new() { Population(Catalog["鸡"], count) }, waterCoverage = WaterCoverage.Land
    };

    private static void ResourceProbes()
    {
        var rows = new List<object>();
        var rules = new LandRuleTuning().Sanitize();
        foreach (int count in new[] { 160, 2000, 6000 })
        foreach (int recovery in new[] { 0, 1, 10000, 50000 })
        foreach (int stock in new[] { 50000, 500000, 1000000 })
        {
            var block = ProbeBlock(count, stock, recovery);
            LandLifeRules.Simulate(block, rules);
            var p = block.community[0];
            float budget = LandLifeRules.PlantFoodBudget(block,
                block.plantBiomass + block.consumedBiomassToday, rules);
            Check(block.consumedBiomassToday <= budget + .1f,
                "Daily plant consumption exceeded drawable budget.");
            Check(Math.Abs(block.plantBiomass + block.consumedBiomassToday -
                stock - block.plantGrowthToday) < .2f, "Plant ledger does not conserve stock.");
            rows.Add(new {
                kind = "synthetic one-day resource probe, not a legal player recipe",
                count, stock, recovery, stockAfter = block.plantBiomass,
                growth = block.plantGrowthToday, consumed = block.consumedBiomassToday,
                budget,
                satisfaction = p.actualEnergySatisfactionToday, born = p.birthsToday,
                died = p.deathsToday
            });
        }
        Save("resource-probes.json", rows);
        Console.WriteLine("Resource probes: 36 one-day cases.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void StockBufferRegression()
    {
        var rules = new LandRuleTuning().Sanitize();
        var rows = new List<object>();
        var today = new List<float>(); var renewable = new List<float>();
        var capacities = new List<float>(); var evolutionToday = new List<float>();
        var evolutionRenewal = new List<float>();
        foreach (int stock in new[] { 50000, 500000 })
        {
            var forecastBlock = ProbeBlock(2000, stock, 50000);
            FoodWeb.GrowPlants(forecastBlock);
            var p = forecastBlock.community[0];
            float predictedToday = LandLifeRules.ForecastPlantIntake(forecastBlock, p,
                p.fitTemperature, p.fitHumidity, p.movementAbility, p.size, p.fertility,
                false, rules);
            float predictedRenewable = LandLifeRules.ForecastPlantIntake(forecastBlock, p,
                p.fitTemperature, p.fitHumidity, p.movementAbility, p.size, p.fertility,
                true, rules);
            float capacity = LandLifeRules.EnvironmentalCarryingCapacity(forecastBlock, p, rules);
            var candidate = Population(Catalog["彩鸡"], 50);
            LandLifeRules.ForecastEvolutionPlantSupport(forecastBlock, p, candidate, 1950,
                rules, out float demand, out float candidateToday, out float candidateRenewal);
            var actual = ProbeBlock(2000, stock, 50000);
            LandLifeRules.Simulate(actual, rules);
            Check(Math.Abs(predictedToday - actual.community[0].allocatedBiomass) < .1f,
                "Current-trait food forecast differs from actual plant allocation.");
            today.Add(predictedToday); renewable.Add(predictedRenewable); capacities.Add(capacity);
            evolutionToday.Add(candidateToday); evolutionRenewal.Add(candidateRenewal);
            rows.Add(new {
                stock, actualConsumption = actual.consumedBiomassToday,
                satisfaction = actual.community[0].actualEnergySatisfactionToday,
                died = actual.community[0].deathsToday,
                predictedToday, predictedRenewable, capacity,
                candidateDemand = demand, candidateToday, candidateRenewal
            });
        }
        Check(today[1] > today[0] + 10000, "Larger stock did not buffer today's food shortage.");
        Check(Math.Abs(renewable[1] - renewable[0]) < .1f,
            "Stock incorrectly increased the renewable food forecast.");
        Check(Math.Abs(capacities[1] - capacities[0]) < .1f,
            "Stock incorrectly increased long-term carrying capacity.");
        // The small candidate in the 50k R cases is already fully fed. Use a
        // genuine low-renewal shortage to verify evolution's two food gates.
        evolutionToday.Clear(); evolutionRenewal.Clear();
        var evolutionRows = new List<object>();
        foreach (int stock in new[] { 1000, 500000 })
        {
            var block = ProbeBlock(2000, stock, 1000);
            FoodWeb.GrowPlants(block);
            var source = block.community[0];
            var candidate = Population(Catalog["彩鸡"], 100);
            LandLifeRules.ForecastEvolutionPlantSupport(block, source, candidate, 1900,
                rules, out float demand, out float current, out float longTerm);
            evolutionToday.Add(current); evolutionRenewal.Add(longTerm);
            Check(longTerm / demand < rules.evolutionRenewalSatisfaction,
                "Unsustainable evolution became qualified by large stock alone.");
            evolutionRows.Add(new { stock, recovery = 1000, demand,
                today = current, renewable = longTerm });
        }
        Check(evolutionToday[1] > evolutionToday[0] + 100,
            "Evolution today-food forecast failed to use stock buffer under shortage.");
        Check(Math.Abs(evolutionRenewal[1] - evolutionRenewal[0]) < .1f,
            "Evolution renewable forecast incorrectly used stock buffer.");

        var mixed = ProbeBlock(1200, 500000, 50000);
        mixed.community.Add(Population(Catalog["鹿"], 120));
        LandLifeRules.Simulate(mixed, rules);
        float budget = LandLifeRules.PlantFoodBudget(mixed,
            mixed.plantBiomass + mixed.consumedBiomassToday, rules);
        Check(mixed.consumedBiomassToday <= budget + .1f,
            "Multiple populations spent more than the shared plant budget.");
        Check(Math.Abs(mixed.community.Sum(p => p.allocatedBiomass) -
            mixed.consumedBiomassToday) < .1f,
            "Population allocations differ from actual plant expenditure.");
        Check(Math.Abs(mixed.plantBiomass + mixed.consumedBiomassToday -
            500000 - mixed.plantGrowthToday) < .2f,
            "Multiple-population plant ledger failed conservation.");
        Save("stock-buffer-regression.json", new {
            passed = true, rows, evolutionRows, mixedBudget = budget,
            mixedConsumption = mixed.consumedBiomassToday,
            mixedStockAfter = mixed.plantBiomass,
            checks = "today forecast=actual; R forecast and K unchanged; evolution budgets separated; shared ledger conserved"
        });
        Console.WriteLine("Stock buffer regression passed: today intake increases; renewable/K unchanged; ledger conserved.");
    }

    private static void WholePreyHuntRegression()
    {
        var prey=Population(Catalog["鸡"],94);
        prey.fitTemperature=75;prey.fitHumidity=75;prey.movementAbility=40;
        prey.size=20;prey.fertility=60;prey.predationRemainder=.175009f;
        var hunter=Population(Catalog["灵感菇狸"],11);
        hunter.fitTemperature=75;hunter.fitHumidity=80;hunter.movementAbility=35;
        hunter.size=25;hunter.fertility=65;hunter.energyReserve=95.081f;
        var block=new BlockInfo{elevation=0,temperature=75,humidity=75,
            habitatRecovery=130000,plantBiomass=500000,maxPlantBiomass=1000000,
            waterCoverage=WaterCoverage.Land,community=new(){prey,hunter}};
        LandLifeRules.Simulate(block,new LandRuleTuning().Sanitize());
        Check(prey.predationDeathsToday>=1,
            "Whole-prey hunting still rounded a capable hungry hunter down to zero catches.");
        Check(hunter.huntingEnergyToday>0f && hunter.actualEnergySatisfactionToday>=.9f,
            "Whole-prey catch did not feed the hunter through the real reserve ledger.");
        Save("whole-prey-regression.json",new{preyBefore=94,
            preyAfter=prey.speciesAmount,prey.predationDeathsToday,
            hunterBefore=11,hunterAfter=hunter.speciesAmount,hunter.huntingEnergyToday,
            hunter.actualEnergySatisfactionToday,hunter.energyReserve,hunter.deathsToday});
        Console.WriteLine("Whole-prey regression passed: capable small hunter caught and ate a whole prey.");
    }

    private sealed record SupplyWitness(string predator, string prey, string habitat,
        int initialPredator, int initialPrey, int initialStock, int days,
        int minimumPredatorLast100, int minimumPreyLast100,
        int finalPredator, int finalPrey, float averageSatisfactionLast100,
        float averagePreyNetRenewalLast100, float finalStock,
        string[] initialCommunity = null);

    private static int SupplyCases;
    private static readonly Dictionary<string, SupplyWitness> SupplyCache = new();
    private static readonly List<object> SupplyDiagnostics = new();

    private static bool LocalCompatible(EcologicalNiche site, SpeciesData p) =>
        site == EcologicalNiche.Water ? p.baseEcologicalNiche == EcologicalNiche.Water :
        p.baseEcologicalNiche == EcologicalNiche.Land;

    private static List<List<SpeciesData>> FoodPaths(SpeciesData consumer,
        SpeciesData excluded, EcologicalNiche site)
    {
        if (consumer == excluded || !LocalCompatible(site, consumer)) return new();
        if (consumer.trophicLevel == 0) return new() { new() { consumer } };
        var paths = new List<List<SpeciesData>>();
        foreach (var prey in consumer.preySpecies)
        {
            if (!FoodWeb.CanEat(Population(consumer, 20), Population(prey, 200))) continue;
            foreach (var path in FoodPaths(prey, excluded, site))
            { path.Add(consumer); paths.Add(path); }
        }
        return paths;
    }

    private static BlockInfo SupplyBlock(EnvironmentRules.HabitatProfile profile,
        int stock) => new() {
            elevation = profile.Defaults.Elevation, temperature = profile.Defaults.Temperature,
            humidity = profile.Defaults.Humidity, habitatRecovery = profile.Defaults.Recovery,
            plantBiomass = stock, maxPlantBiomass = EnvironmentRules.PlantCapacity,
            waterCoverage = WaterCoverage.Land, community = new()
        };

    private static SupplyWitness FindSupply(SpeciesData predator, SpeciesData prey)
    {
        string key = predator.name + "|" + prey.name;
        if (SupplyCache.TryGetValue(key, out var cached)) return cached;
        var rules = new LandRuleTuning().Sanitize();
        int longest = 0, completeCases = 0, lostHunter = 0, lostPrey = 0;
        object bestCase = null;
        var profiles = Enumerable.Range(0, 27).Select(i => EnvironmentRules.Profile((HabitatState)i))
            .OrderByDescending(p => {
                var b = SupplyBlock(p, p.Defaults.Recovery);
                return p.Defaults.Recovery * LandLifeRules.Fitness(b,
                    predator.baseFitTemperature, predator.baseFitHumidity) *
                    LandLifeRules.Fitness(b, prey.baseFitTemperature, prey.baseFitHumidity);
            }).ToArray();
        foreach (var profile in profiles)
        foreach (int initialPredator in new[] { 20, 50 })
        foreach (float fraction in new[] { .55f, .8f, .3f })
        foreach (int initialStock in new[] { 500000, profile.Defaults.Recovery })
        {
            SupplyCases++;
            var block = SupplyBlock(profile, initialStock);
            var root = Population(prey, 1); block.community.Add(root);
            int initialPrey = Math.Clamp((int)(fraction *
                LandLifeRules.EnvironmentalCarryingCapacity(block, root, rules)), 100, 10000);
            root.speciesAmount = initialPrey;
            block.community.Insert(0, Population(predator, initialPredator));
            block.SetNeighbors(new());
            var sim = new SimulationController();
            sim.SetBlocks(new() { block }); sim.SetEvolutionNetworkSpecies(Catalog.Values);
            // Isolate food coexistence: normal demography, trait selection and
            // local splitting remain active; formal species conversion is off.
            sim.SetPresetEvolutionParameters(false, 5, .1f, 10);
            int minimumA = int.MaxValue, minimumP = int.MaxValue;
            int countA = 0, countP = 0, preyAt500 = 0;
            float totalSatisfaction = 0, totalRenewal = 0;
            bool completed = true;
            int elapsed = 0;
            var trace = new List<object>();
            for (int day = 1; day <= 600; day++)
            {
                elapsed = day;
                sim.SimulateDay(day);
                var hunters = block.community.Where(p => p.species == predator).ToArray();
                var food = block.community.Where(p => p.species == prey).ToArray();
                countA = hunters.Sum(p => p.speciesAmount); countP = food.Sum(p => p.speciesAmount);
                if (day == 1 || day % 25 == 0 || countA < 10 || countP < 10)
                    trace.Add(new { day, hunterCount = countA, preyCount = countP,
                        stock = block.plantBiomass,
                        hunterTraits = hunters.Select(Traits).ToArray(),
                        preyTraits = food.Select(Traits).ToArray() });
                if (countA < 10 || countP < 10)
                {
                    if (countA < 10) lostHunter++;
                    if (countP < 10) lostPrey++;
                    completed = false; break;
                }
                if (day == 500) preyAt500 = countP;
                if (day > 500)
                {
                    minimumA = Math.Min(minimumA, countA); minimumP = Math.Min(minimumP, countP);
                    totalSatisfaction += hunters.Sum(p => p.actualEnergySatisfactionToday *
                        p.speciesAmount) / countA;
                    totalRenewal += food.Sum(p => LandLifeRules.Average(p.preyNetRenewalHistory));
                }
            }
            if (completed) completeCases++;
            if (elapsed > longest || elapsed == longest && completed)
            {
                longest = elapsed;
                bestCase = new { habitat = profile.Title, initialPredator, initialPrey, initialStock,
                    elapsed, completed, countA, countP,
                    meanTailSatisfaction = totalSatisfaction / 100,
                    meanTailPreyRenewal = totalRenewal / 100, preyAt500, trace };
            }
            if (completed && totalSatisfaction / 100 >= .9f && totalRenewal > 0 &&
                countP >= preyAt500 * .8f)
            {
                var witness = new SupplyWitness(predator.name, prey.name, profile.Title,
                    initialPredator, initialPrey, initialStock, 600, minimumA, minimumP,
                    countA, countP, totalSatisfaction / 100, totalRenewal / 100, block.plantBiomass);
                SupplyCache[key] = witness;
                SupplyDiagnostics.Add(new { predator = predator.name, prey = prey.name,
                    longest, completeCases, lostHunter, lostPrey, bestCase, witnessFound = true });
                Console.WriteLine($"Local food witness {predator.name}/{prey.name}: {profile.Title}, " +
                    $"tail minimum={minimumA}/{minimumP}, satisfaction={witness.averageSatisfactionLast100:F3}");
                return witness;
            }
        }
        SupplyCache[key] = null;
        SupplyDiagnostics.Add(new { predator = predator.name, prey = prey.name,
            longest, completeCases, lostHunter, lostPrey, bestCase, witnessFound = false });
        Console.WriteLine($"No tested 600-day food witness for {predator.name}/{prey.name}.");
        return null;
    }

    private static void LocalFoodSupportAudit()
    {
        var rows = new List<object>();
        foreach (var edge in AllEdges)
        foreach (var direction in new[] { edge, (edge.to, edge.from) })
        {
            var a = direction.Item1; var b = direction.Item2;
            var alternatives = a.preySpecies.Where(p => p != b &&
                FoodWeb.CanEat(Population(a, 20), Population(p, 200))).ToArray();
            EcologicalNiche site = a.baseEcologicalNiche == EcologicalNiche.Water
                ? EcologicalNiche.Water : EcologicalNiche.Land;
            var local = alternatives.Where(p => LocalCompatible(site, p)).ToArray();
            // Air consumers can feed above land according to the planned rules;
            // their complete life loop is pending, so this is only graph evidence.
            var paths = local.SelectMany(p => FoodPaths(p, b, site))
                .Select(p => { p.Add(a); return p; }).ToArray();
            SupplyWitness witness = null;
            string status;
            if (a.trophicLevel == 0) status = "不适用：源物种食植物";
            else if (alternatives.Length == 0) status = "不满足：没有非目标猎物";
            else if (paths.Length == 0) status = "不满足：当地替代食物链不完整";
            else if (a.baseEcologicalNiche != EcologicalNiche.Land)
                status = "结构具备：水域或空中当地维持待运行时验证";
            else
            {
                foreach (var path in paths.OrderBy(p => p.Count))
                {
                    if (path.Count != 2) continue;
                    witness = FindSupply(a, path[0]);
                    if (witness != null) break;
                }
                if (witness == null) witness = FindCommunitySupply(a, b, local);
                status = witness != null ? "满足：已有600日当地供给见证" :
                    "结构具备：尚未找到满足标准的当地供给配置";
            }
            rows.Add(new {
                from = a.name, to = b.name, sourceNiche = a.baseEcologicalNiche.ToString(),
                targetNiche = b.baseEcologicalNiche.ToString(), sourceTrophicLevel = a.trophicLevel,
                alternatives = alternatives.Select(p => p.name).ToArray(),
                localAlternatives = local.Select(p => p.name).ToArray(),
                completeFoodPaths = paths.Select(p => string.Join("→", p.Select(s => s.name))).ToArray(),
                status, witness,
                isSelectedLandDirection = Edges.Any(e => (e.from == a && e.to == b) ||
                    (e.to == a && e.from == b))
            });
        }
        Save("local-food-support.json", new {
            formalEvolutionEdges = AllEdges.Count, directedConversions = rows.Count,
            selectedLandEdges = Edges.Count, selectedLandDirections = Edges.Count * 2,
            foodEdges = Catalog.Values.Sum(s => s.preySpecies.Count),
            prohibitedExistingTargetRule = "preserved",
            witnessCriteria = "600 days; source and chosen prey remain >=10; last100 mean hunter satisfaction>=0.9; prey net renewal>0; last100 prey count loss<=20%",
            experimentScope = "food coexistence only, formal conversion disabled; normal mutation/local splits active; template initial traits; no species B in witness",
            supplyCases = SupplyCases, rows
        });
        Save("local-food-diagnostics.json", SupplyDiagnostics);
        Console.WriteLine($"Food support audit: {rows.Count} directions, {SupplyCases} local supply cases.");
    }

    private static SupplyWitness FindCommunitySupply(SpeciesData source, SpeciesData excluded,
        SpeciesData[] usablePrey)
    {
        var pool = Catalog.Values.Where(s => s.baseEcologicalNiche == EcologicalNiche.Land &&
            s != source && s != excluded).ToArray();
        var rules = new LandRuleTuning().Sanitize();
        for (int i=0; i<pool.Length; i++) for (int j=i+1; j<pool.Length; j++)
        for (int k=j+1; k<pool.Length; k++) for (int l=k+1; l<pool.Length; l++)
        {
            var species = new[] { source, pool[i], pool[j], pool[k], pool[l] };
            var prey = usablePrey.FirstOrDefault(p => species.Contains(p));
            if (prey == null || species.Where(s => s.trophicLevel > 0).Any(s =>
                !s.preySpecies.Any(p => species.Contains(p)))) continue;
            var profiles = Enumerable.Range(0,27).Select(n => EnvironmentRules.Profile((HabitatState)n))
                .OrderByDescending(p => {
                    var block = SupplyBlock(p, 500000);
                    return p.Defaults.Recovery * species.Average(s =>
                        LandLifeRules.Fitness(block, s.baseFitTemperature, s.baseFitHumidity));
                }).Take(8).ToArray();
            foreach (var profile in profiles)
            foreach (int initialSource in new[] { 20, 50 })
            foreach (int otherHunter in new[] { 20, 50 })
            {
                SupplyCases++;
                var block = SupplyBlock(profile, 500000);
                foreach (var s in species)
                {
                    var p = Population(s, s == source ? initialSource : otherHunter);
                    if (s.trophicLevel == 0)
                    {
                        var alone = SupplyBlock(profile, 500000);
                        alone.community.Add(p);
                        int sameLayer = species.Count(x => x.trophicLevel == 0 &&
                            (x.baseSize >= 25) == (s.baseSize >= 25));
                        p.speciesAmount = Math.Max(100, (int)(.65f *
                            LandLifeRules.EnvironmentalCarryingCapacity(alone, p, rules) / sameLayer));
                    }
                    block.community.Add(p);
                }
                var initial = block.community.Select(p => p.species.name + "=" + p.speciesAmount).ToArray();
                var sim = new SimulationController();
                sim.SetBlocks(new() { block }); sim.SetEvolutionNetworkSpecies(Catalog.Values);
                sim.SetPresetEvolutionParameters(false, 5, .1f, 10);
                int minSource=int.MaxValue, minPrey=int.MaxValue, aCount=0, pCount=0, preyAt500=0;
                float satisfaction=0, renewal=0;
                bool completed=true;
                for(int day=1; day<=600; day++)
                {
                    sim.SimulateDay(day);
                    if(species.Any(s => block.community.Where(p => p.species==s)
                        .Sum(p=>p.speciesAmount)<10)) { completed=false; break; }
                    var hunters=block.community.Where(p=>p.species==source).ToArray();
                    var food=block.community.Where(p=>p.species==prey).ToArray();
                    aCount=hunters.Sum(p=>p.speciesAmount); pCount=food.Sum(p=>p.speciesAmount);
                    if(day==500) preyAt500=pCount;
                    if(day>500)
                    {
                        minSource=Math.Min(minSource,aCount); minPrey=Math.Min(minPrey,pCount);
                        satisfaction+=hunters.Sum(p=>p.actualEnergySatisfactionToday*p.speciesAmount)/aCount;
                        renewal+=food.Sum(p=>LandLifeRules.Average(p.preyNetRenewalHistory));
                    }
                }
                if(completed && satisfaction/100>=.9f && renewal>0 && pCount>=preyAt500*.8f)
                {
                    Console.WriteLine($"Community food witness {source.name}→{excluded.name}: " +
                        string.Join(",",initial));
                    return new(source.name,prey.name,profile.Title,initialSource,
                        int.Parse(initial[Array.IndexOf(species,prey)].Split('=')[1]),500000,600,
                        minSource,minPrey,aCount,pCount,satisfaction/100,renewal/100,
                        block.plantBiomass,initial);
                }
            }
        }
        return null;
    }

    private sealed class Trial
    {
        public SimulationController Sim = new();
        public EnvironmentWorld World = new();
        public List<BlockInfo> Blocks = new();
        public List<TerrainKind> Presets = new();
        public SpeciesData Source, Target;
        public int SuccessDay, SplitEvents, MutationEvents;
        public int[] BestTraits;
        public int BestDistance = int.MaxValue;
        public List<object> Events = new();
        public List<object> QualifiedTraitProbes = new();
        private Dictionary<string, SpeciesData> Before = new();
        public Trial(SpeciesData source, SpeciesData target, TerrainKind preset, int count, int seed,
            bool includeTargetFood = false)
        {
            Source = source; Target = target;
            UnityEngine.Random.InitState(seed);
            Add(preset);
            Blocks[0].community.Add(Population(source, count));
            if (source.trophicLevel > 0)
            {
                // Explicit scenario prerequisite: prey starts at full template traits.
                var prey = source.preySpecies.Where(x => x != target && x.baseEcologicalNiche == EcologicalNiche.Land)
                    .OrderBy(x => x.trophicLevel).First();
                Blocks[0].community.Add(Population(prey, 1600));
            }
            if (includeTargetFood && target.trophicLevel > 0)
            {
                // Fixture prerequisite, not a player operation. Never insert B.
                void AddFood(SpeciesData hunter)
                {
                    if (hunter.trophicLevel == 0) return;
                    var prey = hunter.preySpecies.Where(x => x != target &&
                        x.baseEcologicalNiche == EcologicalNiche.Land)
                        .OrderBy(x => x.trophicLevel).FirstOrDefault();
                    if (prey == null) return;
                    if (!Blocks[0].community.Any(p => p.species == prey))
                        Blocks[0].community.Add(Population(prey, prey.trophicLevel == 0 ? 1600 : 80));
                    AddFood(prey);
                }
                AddFood(target);
            }
            Sim.SetBlocks(Blocks);
            Sim.SetEvolutionNetworkSpecies(Catalog.Values);
            Sim.OnLocalSplitResolved += r => {
                SplitEvents++;
                Before[r.ResultPopulationId] = Before.GetValueOrDefault(r.SourcePopulationId);
                Events.Add(new { day = r.Day, kind = "split", amount = r.Amount });
            };
            Sim.OnPresetEvolutionResolved += r => {
                string from = Before.GetValueOrDefault(r.SourcePopulationId)?.name;
                if (from == source.name && r.TargetSpecies == target && SuccessDay == 0)
                    SuccessDay = r.Day;
                Events.Add(new { day = r.Day, kind = "evolution", from,
                    to = r.TargetSpecies.name, amount = r.Amount });
            };
            Sim.OnEcologyEvent += (day, b, p, kind, detail) => {
                if (kind == "trait_mutation") MutationEvents++;
            };
        }
        public void Add(TerrainKind preset)
        {
            var d = EnvironmentRules.Defaults(preset);
            Blocks.Add(new BlockInfo {
                elevation = d.Elevation, temperature = d.Temperature, humidity = d.Humidity,
                plantBiomass = d.Recovery, habitatRecovery = d.Recovery,
                maxPlantBiomass = EnvironmentRules.PlantCapacity, waterCoverage = WaterCoverage.Land,
                community = new()
            });
            Presets.Add(preset);
            SyncTopology();
            Sim.SetBlocks(Blocks);
        }
        public void SyncTopology()
        {
            var nodes = new List<TopologyNode>();
            for (int i = 0; i < Blocks.Count; i++)
            {
                var positions = new List<GridPosition>(); var neighbors = new List<BlockInfo>();
                if (i > 0) { positions.Add(new(i-1, 0)); neighbors.Add(Blocks[i-1]); }
                if (i+1 < Blocks.Count) { positions.Add(new(i+1, 0)); neighbors.Add(Blocks[i+1]); }
                Blocks[i].SetNeighbors(neighbors);
                nodes.Add(new(new(i, 0), Blocks[i].elevation, Presets[i], positions));
            }
            World.SynchronizeTopology(nodes);
            Sim.NotifyEnvironmentChanged();
        }
        public void Climate(int axis, int amount)
        {
            if (!World.TrySetClimate(new(0, 0), (EnvironmentAttribute)axis, amount, out var error))
                throw new Exception(error);
            Sim.NotifyEnvironmentChanged();
        }
        public void Plant(int amount)
        {
            if (!World.TrySetRecovery(new(0, 0), amount, out var error)) throw new Exception(error);
            Blocks[0].plantBiomass = (float)EnvironmentRules.MultiplyStock(
                Blocks[0].plantBiomass, Blocks[0].maxPlantBiomass, amount / 100000d);
            Sim.NotifyEnvironmentChanged();
        }
        public void Step(int day)
        {
            Before.Clear();
            foreach (var p in Blocks.SelectMany(b => b.community)) Before[p.PopulationId] = p.species;
            World.AdvanceToDay(day);
            for (int i = 0; i < Blocks.Count; i++)
            {
                World.TryRead(new(i, 0), out var snapshot);
                Blocks[i].temperature = EnvironmentRules.Round(snapshot.Temperature.Value);
                Blocks[i].humidity = EnvironmentRules.Round(snapshot.Humidity.Value);
                Blocks[i].habitatRecovery = EnvironmentRules.Round(snapshot.Recovery.Value);
            }
            Sim.SimulateDay(day);
            foreach (var block in Blocks)
            foreach (var p in block.community.Where(p => p.species == Source))
            {
                int distance = Traits(p).Zip(Traits(Target), (a,b) => Math.Max(0, Math.Abs(a-b)-5)).Sum();
                if (distance < BestDistance) { BestDistance = distance; BestTraits = Traits(p); }
                if(distance==0 && QualifiedTraitProbes.Count<100)
                {
                    bool whole=(bool)typeof(SimulationController).GetMethod("IsLocalSplitPair",
                        BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{block,p});
                    int amount=whole?p.speciesAmount:Math.Clamp(UnityEngine.Mathf.RoundToInt(p.speciesAmount*.1f),10,100);
                    var candidate=Population(Target,amount);var rules=new LandRuleTuning().Sanitize();
                    float demand=LandLifeRules.Demand(candidate,amount,LandLifeRules.Fitness(block,
                        candidate.fitTemperature,candidate.fitHumidity),rules);
                    float today=0,renewable=0;
                    if(Target.trophicLevel==0)
                        LandLifeRules.ForecastEvolutionPlantSupport(block,p,candidate,
                            Source.trophicLevel==0?p.speciesAmount-amount:p.speciesAmount,
                            rules,out demand,out today,out renewable);
                    else
                    {
                        var prey=block.community.Where(q=>FoodWeb.CanEat(candidate,q)&&q.speciesAmount>0).ToArray();
                        float preference=prey.Where(q=>q!=p || !whole).Sum(q=>LandLifeRules.HuntPreference(candidate,q,rules));
                        foreach(var q in prey)
                        {
                            int available=q==p?(whole?0:Math.Max(0,q.speciesAmount-amount)):q.speciesAmount;
                            if(available<=0||preference<=0)continue;
                            float kills=amount*LandLifeRules.HuntPreference(candidate,q,rules)/preference*
                                Math.Min(1,10f/Math.Max(1,q.size))*LandLifeRules.CaptureChance(candidate,q,rules);
                            today+=Math.Min(available,kills)*LandLifeRules.PreyEnergy(candidate,q,rules);
                        }
                        renewable=LandLifeRules.AvailableSustainablePreyFood(block,candidate,rules,
                            replaced:p,replacedCount:whole?0:p.speciesAmount-amount);
                    }
                    QualifiedTraitProbes.Add(new{day,traits=Traits(p),count=p.speciesAmount,wholeBranch=whole,
                        amount,demand,today,renewable,todayRatio=demand>0?(float?)(today/demand):null,
                        renewableRatio=demand>0?(float?)(renewable/demand):null,
                        communityCount=block.community.Count,targetPresent=block.community.Any(q=>q.species==Target),
                        effectiveDays=p.evolutionConversions.FirstOrDefault(r=>r.target==Target)?.effectiveDays??0,
                        eligible=(bool)typeof(SimulationController).GetMethod("IsEligibleEvolutionTarget",
                            BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Sim,new object[]{block,p,Target,day})});
                }
            }
        }
    }

    private static TerrainKind ClosestPreset(SpeciesData s) => Enum.GetValues<TerrainKind>()
        .OrderBy(t => { var d = EnvironmentRules.Defaults(t); return
            Math.Abs(d.Temperature-s.baseFitTemperature)+Math.Abs(d.Humidity-s.baseFitHumidity); }).First();

    private static void AllDirectionOperations()
    {
        var rows = new List<object>();
        foreach (var edge in Edges)
        foreach (var (source, target) in new[] { edge, (edge.to, edge.from) })
        {
            int before = rows.Count;
            var single = new List<(string label, Action<Trial,int> apply)> { ("baseline", (_,_)=>{}) };
            foreach (int a in ClimateAmounts)
            {
                int amount=a;
                single.Add(($"temperature {a}", (x,d)=>{if(d==1)x.Climate(0,amount);}));
                single.Add(($"humidity {a}", (x,d)=>{if(d==1)x.Climate(1,amount);}));
                single.Add(($"plant {a*1000}", (x,d)=>{if(d==1)x.Plant(amount*1000);}));
            }
            foreach(int elevation in new[]{0,1,2})
            {
                int z=elevation;
                if(Math.Abs(z-EnvironmentRules.Defaults(ClosestPreset(source)).Elevation)!=1)continue;
                single.Add(($"elevation {z}", (x,d)=>{if(d==1){x.Blocks[0].elevation=z;x.SyncTopology();}}));
            }
            foreach(var terrain in Enum.GetValues<TerrainKind>())
            {
                var t=terrain;
                single.Add(($"new tile {t}", (x,d)=>{if(d==1)x.Add(t);}));
            }
            foreach(int count in new[]{80,160,640})
            foreach(var plan in single) Run(plan.label,"single",plan.apply,ClosestPreset(source),count,600);

            // A small, explicitly bounded screen. Climate decisions only use
            // legal tools/cancellation, and real two-slot eviction is retained.
            foreach(var preset in new[]{ClosestPreset(source),ClosestPreset(target),TerrainKind.Rainforest}.Distinct())
            foreach(int count in new[]{20,160,640})
            foreach(int policy in Enumerable.Range(0,8))
            {
                int mode=policy;
                Run($"climate feedback; policy {mode}","combined",(x,d)=>{
                    if(d==1 && mode>=4) x.Add(ClosestPreset(target));
                    if((mode%4==3 && d<151))return;
                    if(d%30!=1)return;
                    if(mode%4==1 || mode%4==2) x.Plant(mode%4==1?50000:-25000);
                    if(mode%4==1 || mode%4==2)
                        ClimateToward(x, (d/30)%2, target);
                    else { ClimateToward(x,0,target);ClimateToward(x,1,target); }
                },preset,count,1200);
            }
            var records=rows.Skip(before).ToArray();
            Console.WriteLine($"Evolution screen {source.name}→{target.name}: {records.Length} trials completed.");

            void Run(string label,string stage,Action<Trial,int> action,TerrainKind preset,int count,int limit)
            {
                var trial=new Trial(source,target,preset,count,1, true);
                var initial=trial.Blocks[0].community.Select(p=>new{species=p.species.name,count=p.speciesAmount}).ToArray();
                var trace=new List<object>();int days=0;
                for(int day=1;day<=limit;day++)
                {
                    action(trial,day);trial.Step(day);days=day;
                    if(day==1 || day%50==0 || trial.SuccessDay>0)
                        trace.Add(new{day,environment=new{trial.Blocks[0].temperature,trial.Blocks[0].humidity,
                            trial.Blocks[0].habitatRecovery,trial.Blocks[0].plantBiomass},
                            populations=trial.Blocks.SelectMany((b,i)=>b.community.Select(p=>new{
                            block=i,species=p.species.name,count=p.speciesAmount,traits=Traits(p),
                            satisfaction=p.actualEnergySatisfactionToday})).ToArray()});
                    if(trial.SuccessDay>0 || !trial.Blocks.Any(b=>b.community.Any(p=>p.species==source&&p.speciesAmount>0)))break;
                }
                rows.Add(new{from=source.name,to=target.name,stage,operation=label,preset=preset.ToString(),initial,
                    seed=1,days,successDay=trial.SuccessDay,bestDistanceOutsideTolerance=trial.BestDistance,
                    bestTraits=trial.BestTraits,splits=trial.SplitEvents,mutations=trial.MutationEvents,
                    qualifiedTraitProbes=trial.QualifiedTraitProbes,trace,events=trial.Events});
            }
        }
        Save("land-direction-screen.json",rows);
    }

    private static void ClimateToward(Trial trial,int axis,SpeciesData target)
    {
        trial.World.TryRead(new(0,0),out var snapshot);
        double wanted=axis==0?target.baseFitTemperature:target.baseFitHumidity;
        double def=snapshot.Defaults.Get((EnvironmentAttribute)axis);
        double current=axis==0?snapshot.Temperature.Value:snapshot.Humidity.Value;
        if(Math.Abs(current-wanted)<=3)return;
        int amount=ClimateAmounts.OrderBy(a=>Math.Abs(Math.Clamp(def+a,0,100)-wanted)).First();
        if(Math.Abs(def-wanted)<Math.Abs(Math.Clamp(def+amount,0,100)-wanted))
            trial.World.TryCancelClimate(new(0,0),(EnvironmentAttribute)axis,out _);
        else trial.Climate(axis,amount);
    }

    private static void RobustRetest()
    {
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(Out,"land-direction-screen.json")));
        var successful=json.RootElement.EnumerateArray().Where(r=>r.GetProperty("successDay").GetInt32()>0)
            .GroupBy(r=>(r.GetProperty("from").GetString(),r.GetProperty("to").GetString()));
        var rows=new List<object>();
        foreach(var group in successful)
        {
            var best=group.OrderBy(r=>r.GetProperty("successDay").GetInt32()).First();
            var source=Catalog[group.Key.Item1];var target=Catalog[group.Key.Item2];
            int original=best.GetProperty("initial").EnumerateArray().First(r=>r.GetProperty("species").GetString()==source.name)
                .GetProperty("count").GetInt32();
            var preset=Enum.Parse<TerrainKind>(best.GetProperty("preset").GetString());
            string label=best.GetProperty("operation").GetString(),stage=best.GetProperty("stage").GetString();
            int before=rows.Count;
            foreach(int seed in Enumerable.Range(1,10))
            foreach(double scale in new[]{.8,1,1.2})
            foreach(int delay in new[]{0,5,10})
            {
                int count=(int)Math.Round(original*scale);
                var trial=new Trial(source,target,preset,count,seed,true);int minAfter=int.MaxValue,days=0;
                for(int day=1;day<=1200;day++)
                {
                    int d=day-delay;
                    if(d>0)
                    {
                        if(stage=="single" && d==1)
                        {
                            if(label.StartsWith("new tile "))trial.Add(Enum.Parse<TerrainKind>(label.Substring(9)));
                            else if(label.StartsWith("plant "))trial.Plant(int.Parse(label.Substring(6)));
                            else if(label.StartsWith("temperature "))trial.Climate(0,int.Parse(label.Substring(12)));
                            else if(label.StartsWith("humidity "))trial.Climate(1,int.Parse(label.Substring(9)));
                        }
                        if(stage=="combined")
                        {
                            int mode=int.Parse(label.Split(' ').Last());
                            if(d==1&&mode>=4)trial.Add(ClosestPreset(target));
                            if(!(mode%4==3&&d<151)&&d%30==1)
                            {
                                if(mode%4==1||mode%4==2)trial.Plant(mode%4==1?50000:-25000);
                                if(mode%4==1||mode%4==2)ClimateToward(trial,(d/30)%2,target);
                                else{ClimateToward(trial,0,target);ClimateToward(trial,1,target);}
                            }
                        }
                    }
                    trial.Step(day);days=day;
                    if(trial.SuccessDay>0)
                    {
                        int n=trial.Blocks.Sum(b=>b.community.Where(p=>p.species==target).Sum(p=>p.speciesAmount));
                        minAfter=Math.Min(minAfter,n);
                        if(day>=trial.SuccessDay+100)break;
                    }
                    if(trial.SuccessDay==0&&!trial.Blocks.Any(b=>b.community.Any(p=>p.species==source&&p.speciesAmount>0)))break;
                }
                rows.Add(new{from=source.name,to=target.name,preset=preset.ToString(),operation=label,stage,
                    seed,countScale=scale,operationDelay=delay,initialSource=count,days,successDay=trial.SuccessDay,
                    targetMinimumFirst100Days=trial.SuccessDay>0?(int?)minAfter:null,
                    observed100Days=trial.SuccessDay>0&&days>=trial.SuccessDay+100,
                    bestTraits=trial.BestTraits,bestDistanceOutsideTolerance=trial.BestDistance});
            }
            Console.WriteLine($"Robust retest {source.name}→{target.name}: {rows.Count-before} cases.");
        }
        Save("land-direction-robustness.json",rows);
    }

    private static void GateDiagnostics()
    {
        var rows=new List<object>();
        foreach(var spec in new[]{(a:"鹿",b:"灵感菇狸",preset:TerrainKind.Rainforest,count:20,mode:5),
            (a:"鹿驯",b:"雪狼",preset:TerrainKind.Tundra,count:20,mode:1)})
        foreach(bool pauseSourceSelection in new[]{false,true})
        {
            var source=Catalog[spec.a];var target=Catalog[spec.b];
            var trial=new Trial(source,target,spec.preset,spec.count,1,true);
            PopulationData frozen=null;int freezeDay=0;var trace=new List<object>();
            for(int day=1;day<=600;day++)
            {
                int mode=spec.mode;
                if(day==1&&mode>=4)trial.Add(ClosestPreset(target));
                if(day%30==1){trial.Plant(50000);ClimateToward(trial,(day/30)%2,target);}
                if(frozen!=null) frozen.mutationDaysElapsed=-1000000;
                trial.Step(day);
                foreach(var block in trial.Blocks)
                foreach(var p in block.community.Where(p=>p.species==source))
                {
                    int effective=p.evolutionConversions.FirstOrDefault(r=>r.target==target)?.effectiveDays??0;
                    if(effective>0 && frozen==null && pauseSourceSelection){frozen=p;freezeDay=day;}
                    if(effective>0 || (day>=25&&day<=40&&source.name=="鹿驯") ||
                        (day>=415&&day<=445&&source.name=="鹿"))
                        trace.Add(new{day,traits=Traits(p),count=p.speciesAmount,effective,
                            communityCount=block.community.Count});
                }
                if(trial.SuccessDay>0||!trial.Blocks.Any(b=>b.community.Any(p=>p.species==source&&p.speciesAmount>0)))break;
            }
            rows.Add(new{from=spec.a,to=spec.b,pauseSourceSelection,
                classification=pauseSourceSelection?"synthetic diagnostic; not a legal player recipe":"legal baseline",
                freezeDay,successDay=trial.SuccessDay,trace,probes=trial.QualifiedTraitProbes,events=trial.Events});
        }
        var survival=new Trial(Catalog["鸡"],Catalog["灵感菇狸"],TerrainKind.Desert,640,1,true);
        survival.Add(TerrainKind.Rainforest);var journal=new List<object>();
        for(int day=1;day<=500;day++)
        {
            survival.Step(day);
            if(survival.SuccessDay>0)
                journal.Add(new{day,populations=survival.Blocks.SelectMany((b,i)=>b.community.Select(p=>new{
                    block=i,species=p.species.name,count=p.speciesAmount,traits=Traits(p),
                    satisfaction=p.actualEnergySatisfactionToday,huntingEnergy=p.huntingEnergyToday,
                    energyReserve=p.energyReserve,births=p.birthsToday,deaths=p.deathsToday,
                    preyRenewal=LandLifeRules.Average(p.preyNetRenewalHistory)})).ToArray()});
            if(survival.SuccessDay>0&&day>=survival.SuccessDay+100)break;
        }
        Save("land-gate-diagnostics.json",new{gateExperiments=rows,chickenMushroom=new{
            successDay=survival.SuccessDay,journal,events=survival.Events}});
        var reserveTests=new List<object>();
        foreach(float fraction in new[]{.1f,.5f,1f})
        {
            var experiment=new Trial(Catalog["鸡"],Catalog["灵感菇狸"],TerrainKind.Desert,640,1,true);
            experiment.Add(TerrainKind.Rainforest);int min=int.MaxValue,end=0;
            var observations=new List<object>();
            for(int day=1;day<=900;day++)
            {
                if(experiment.SuccessDay>0 && day==experiment.SuccessDay+1)
                {
                    var rules=experiment.Sim.ReadLandRules();rules.reserveHuntFraction=fraction;
                    experiment.Sim.ApplyLandRules(rules);
                }
                experiment.Step(day);
                if(experiment.SuccessDay==0)continue;
                var hunters=experiment.Blocks.SelectMany(b=>b.community).Where(p=>p.species==Catalog["灵感菇狸"]).ToArray();
                end=hunters.Sum(p=>p.speciesAmount);min=Math.Min(min,end);
                observations.Add(new{day,amount=end,hunters=hunters.Select(p=>new{count=p.speciesAmount,
                    traits=Traits(p),satisfaction=p.actualEnergySatisfactionToday,huntingEnergy=p.huntingEnergyToday,
                    energyReserve=p.energyReserve,births=p.birthsToday,deaths=p.deathsToday}).ToArray()});
                if(day>=experiment.SuccessDay+100)break;
            }
            reserveTests.Add(new{classification=fraction==.1f?"current production baseline":"proposed numeric change only in diagnostic instance",
                reserveHuntFraction=fraction,successDay=experiment.SuccessDay,targetMinimumFirst100Days=min,
                targetCountAt100Days=end,observations});
        }
        Save("predator-reserve-diagnostics.json",reserveTests);
    }

    private static void MushroomCauseDiagnostics()
    {
        var experiments=new List<object>();
        foreach(bool freezeNewPredatorTraits in new[]{false,true})
        {
            var trial=new Trial(Catalog["鸡"],Catalog["灵感菇狸"],TerrainKind.Desert,640,1,true);
            trial.Add(TerrainKind.Rainforest);var days=new List<object>();
            for(int day=1;day<=500;day++)
            {
                var before=new List<object>();
                var rules=trial.Sim.ReadLandRules();
                foreach(var block in trial.Blocks)
                foreach(var hunter in block.community.Where(p=>p.species==Catalog["灵感菇狸"]))
                {
                    if(freezeNewPredatorTraits)hunter.mutationDaysElapsed=-1000000;
                    float fitness=LandLifeRules.Fitness(block,hunter.fitTemperature,hunter.fitHumidity);
                    float demand=LandLifeRules.Demand(hunter,hunter.speciesAmount,fitness,rules);
                    float adult=hunter.speciesAmount*LandLifeRules.AdultNeed(hunter.size,hunter.movementAbility,rules);
                    float targetEnergy=Math.Max(0,demand+adult*rules.reserveHuntFraction-hunter.energyReserve);
                    var prey=block.community.Where(p=>p.speciesAmount>0&&FoodWeb.CanEat(hunter,p)).ToArray();
                    float preference=prey.Sum(p=>LandLifeRules.HuntPreference(hunter,p,rules));
                    before.Add(new{count=hunter.speciesAmount,traits=Traits(hunter),demand,
                        reserve=hunter.energyReserve,targetEnergy,
                        prey=prey.Select(p=>new{species=p.species.name,count=p.speciesAmount,traits=Traits(p),
                            preyEnergy=LandLifeRules.PreyEnergy(hunter,p,rules),
                            expectedAttempts=preference>0?hunter.speciesAmount*LandLifeRules.HuntPreference(hunter,p,rules)/preference*
                                Math.Min(1,10f/Math.Max(1,p.size))*LandLifeRules.CaptureChance(hunter,p,rules):0,
                            wholePreyClaim=preference>0?LandLifeRules.WholePreyClaim(
                                hunter.speciesAmount*LandLifeRules.HuntPreference(hunter,p,rules)/preference*
                                Math.Min(1,10f/Math.Max(1,p.size))*LandLifeRules.CaptureChance(hunter,p,rules),
                                targetEnergy,LandLifeRules.PreyEnergy(hunter,p,rules)):0,
                            remainder=p.predationRemainder,renewal=LandLifeRules.Average(p.preyNetRenewalHistory)}).ToArray()});
                }
                trial.Step(day);
                if(trial.SuccessDay==0)continue;
                days.Add(new{day,before,after=trial.Blocks.SelectMany((b,i)=>b.community.Select(p=>new{
                    block=i,species=p.species.name,count=p.speciesAmount,traits=Traits(p),satisfaction=p.actualEnergySatisfactionToday,
                    demand=p.energyNeed*p.populationBeforePredation,huntingEnergy=p.huntingEnergyToday,
                    energyReserve=p.energyReserve,births=p.birthsToday,deaths=p.deathsToday,
                    killedByPredators=p.predationDeathsToday})).ToArray()});
                if(day>=trial.SuccessDay+600)break;
            }
            experiments.Add(new{freezeNewPredatorTraits,
                classification=freezeNewPredatorTraits?"synthetic diagnostic, only new predator trait selection paused":"production baseline",
                initial="640 template chickens in dry desert; adjacent rainforest; seed 1",
                successDay=trial.SuccessDay,days,events=trial.Events});
        }
        Save("mushroom-cause-diagnostics.json",experiments);
    }

    private static void SingleOperations()
    {
        var rows = new List<object>();
        // First edge in both directions, with real preset start values and legal operations.
        foreach (var pair in new[] { (Edges[0].from.name, Edges[0].to.name),
            (Edges[0].to.name, Edges[0].from.name) })
        foreach (int count in new[] { 80, 160, 640 })
        {
            var source = Catalog[pair.Item1]; var target = Catalog[pair.Item2];
            var preset = Enum.GetValues<TerrainKind>().OrderBy(t => {
                var d = EnvironmentRules.Defaults(t);
                return Math.Abs(d.Temperature - source.baseFitTemperature) +
                    Math.Abs(d.Humidity - source.baseFitHumidity);
            }).First();
            var plans = new List<(string label, Action<Trial> apply)> { ("baseline", _ => {}) };
            foreach (int amount in ClimateAmounts)
            {
                int a = amount;
                plans.Add(($"temperature {a}", x => x.Climate(0, a)));
                plans.Add(($"humidity {a}", x => x.Climate(1, a)));
                plans.Add(($"plant {a * 1000}", x => x.Plant(a * 1000)));
            }
            foreach (int amount in new[] { -1, 1 })
            {
                int a = amount;
                if (EnvironmentRules.Defaults(preset).Elevation + a >= 0 &&
                    EnvironmentRules.Defaults(preset).Elevation + a <= 2)
                    plans.Add(($"elevation {a}", x => { x.Blocks[0].elevation += a; x.SyncTopology(); }));
            }
            foreach (TerrainKind terrain in Enum.GetValues<TerrainKind>())
            {
                TerrainKind t = terrain;
                plans.Add(($"new tile {t}", x => x.Add(t)));
            }
            foreach (var plan in plans)
            {
                var trial = new Trial(source, target, preset, count, 1);
                plan.apply(trial);
                int days = 0;
                for (int day = 1; day <= 600; day++)
                {
                    trial.Step(day); days = day;
                    if (trial.SuccessDay > 0 || !trial.Blocks.Any(b => b.community.Any(p =>
                        p.species == source && p.speciesAmount > 0))) break;
                }
                rows.Add(new {
                    from = source.name, to = target.name, initialCount = count,
                    preset = preset.ToString(), preyPrerequisite = source.trophicLevel > 0
                        ? "1600 " + source.preySpecies.First(x => x.baseEcologicalNiche == EcologicalNiche.Land).name : "none",
                    operation = plan.label, days, successDay = trial.SuccessDay,
                    bestDistanceOutsideTolerance = trial.BestDistance, bestTraits = trial.BestTraits,
                    splits = trial.SplitEvents, mutations = trial.MutationEvents,
                    finalPopulations = trial.Blocks.SelectMany((b, i) => b.community.Select(p => new {
                        block = i, species = p.species?.name, count = p.speciesAmount, traits = Traits(p)
                    })).ToArray(), events = trial.Events
                });
            }
        }
        Save("first-edge-single-operations.json", rows);
        Console.WriteLine($"First edge: {rows.Count} single-operation runs, <=600 days each.");
    }
}
