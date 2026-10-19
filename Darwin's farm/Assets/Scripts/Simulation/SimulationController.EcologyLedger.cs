using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class EcologySpeciesDailyAccount
{
    public string speciesId;
    public int start, paidBirths, naturalDeaths, predationDeaths;
    public int formalIn, formalOut, endangeredRemoved, end;
    public int Residual => end - (start + paidBirths - naturalDeaths -
        predationDeaths + formalIn - formalOut - endangeredRemoved);
}

public sealed class DailyEcologyLedger
{
    public int day;
    public string rulesVersion, sourceGraphHash;
    public int seed;
    public bool enableAirEcology, enableWaterEcology;
    public bool enableEconomy, enableRandomEvents;
    public bool enableNeighborMovementFormula;
    public double worldMassStart, worldMassEnd, actualPlantGrowth;
    public double namedDissipation, massResidual;
    public string modelHash;
    public IReadOnlyList<EcologySpeciesDailyAccount> species;
}

public partial class SimulationController
{
    [SerializeField] private bool enableEcologyLedgerDiagnostics = false;
    public bool EnableEcologyLedgerDiagnostics
    {
        get => enableEcologyLedgerDiagnostics;
        set => enableEcologyLedgerDiagnostics = value;
    }
    public DailyEcologyLedger LastEcologyLedger { get; private set; }
    public event Action<DailyEcologyLedger> OnDailyEcologyLedger;

    private readonly Dictionary<string, EcologySpeciesDailyAccount>
        workingSpeciesLedger = new Dictionary<string,
            EcologySpeciesDailyAccount>(StringComparer.Ordinal);
    private double workingWorldMassStart;

    private static string LedgerSpeciesId(PopulationData population) =>
        population.species != null ? population.species.SpeciesId :
        !string.IsNullOrEmpty(population.speciesId) ? population.speciesId :
        population.lineageName ?? string.Empty;

    private EcologySpeciesDailyAccount SpeciesAccount(string key)
    {
        key = key ?? string.Empty;
        if (!workingSpeciesLedger.TryGetValue(key,
            out EcologySpeciesDailyAccount account))
        {
            account = new EcologySpeciesDailyAccount { speciesId = key };
            workingSpeciesLedger.Add(key, account);
        }
        return account;
    }

    private double ReadWorldMaterial()
    {
        double total = 0d;
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null) continue;
            total += Math.Max(0d, block.plantBiomass) +
                Math.Max(0d, block.detritusBiomass);
            if (block.community == null) continue;
            foreach (PopulationData population in block.community)
            {
                if (population == null) continue;
                double body = population.bodyBiomass;
                // Match the land loop's pending initialization without
                // changing model state merely to read the opening balance.
                if ((population.ecologicalNiche == (block.waterCoverage ==
                        WaterCoverage.Land ? EcologicalNiche.Land :
                        EcologicalNiche.Water) ||
                     EnableAirEcology &&
                     population.ecologicalNiche == EcologicalNiche.Air) &&
                    !population.bodyBiomassInitialized &&
                    population.speciesAmount > 0)
                    body = population.speciesAmount *
                        LandLifeRules.BodyMass(population.size, LandRules);
                total += Math.Max(0d, body) +
                    Math.Max(0d, population.energyReserve);
            }
        }
        return total;
    }

    private void BeginEcologyLedger()
    {
        workingSpeciesLedger.Clear();
        // Water plants grow in OnBeforeDaySimulated; land plants grow later in
        // SimulateBlock. Move the former growth back to the opening ledger.
        double priorWaterMass = 0d;
        if (EnableWaterEcology)
            foreach (BlockInfo block in blockInfos)
                if (block != null && HabitatTopology.IsWater(block))
                    priorWaterMass += Math.Max(0d, block.plantGrowthToday);
        workingWorldMassStart = ReadWorldMaterial() - priorWaterMass;
        foreach (BlockInfo block in blockInfos)
            if (block != null && block.community != null)
                foreach (PopulationData population in block.community)
                    if (population != null && population.speciesAmount > 0)
                        SpeciesAccount(LedgerSpeciesId(population)).start +=
                            population.speciesAmount;
    }

    // Called before evolution and migration, while every life-phase counter
    // still belongs to the population that earned it.
    private void CaptureLifeFlows()
    {
        foreach (BlockInfo block in blockInfos)
            if (block != null && (block.waterCoverage == WaterCoverage.Land ||
                    EnableWaterEcology && HabitatTopology.IsWater(block)) &&
                block.community != null)
                foreach (PopulationData population in block.community)
                {
                    if (population == null ||
                        population.ecologicalNiche != (block.waterCoverage ==
                            WaterCoverage.Land ? EcologicalNiche.Land :
                            EcologicalNiche.Water) &&
                        !(EnableAirEcology &&
                          population.ecologicalNiche == EcologicalNiche.Air))
                        continue;
                    EcologySpeciesDailyAccount account = SpeciesAccount(
                        LedgerSpeciesId(population));
                    account.paidBirths += Mathf.Max(0, population.birthsToday);
                    account.predationDeaths += Mathf.Max(0,
                        population.predationDeathsToday);
                    account.naturalDeaths += Mathf.Max(0,
                        population.deathsToday - population.predationDeathsToday);
                }
    }

    private void RecordFormalTransfer(PopulationData source,
        SpeciesData target, int amount)
    {
        SpeciesAccount(LedgerSpeciesId(source)).formalOut += amount;
        SpeciesAccount(target != null ? target.SpeciesId : "").formalIn += amount;
    }

    private void RecordEndangeredRemoval(PopulationData population)
    {
        SpeciesAccount(LedgerSpeciesId(population)).endangeredRemoved +=
            Mathf.Max(0, population.speciesAmount);
    }

    private void FinishEcologyLedger(int day)
    {
        double growth = 0d, sinks = 0d;
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null) continue;
            if (block.waterCoverage == WaterCoverage.Land ||
                EnableWaterEcology && HabitatTopology.IsWater(block))
            {
                growth += Math.Max(0d, block.plantGrowthToday);
                sinks += Math.Max(0d, block.accountedBiomassSinkToday);
            }
            if (block.community == null) continue;
            foreach (PopulationData population in block.community)
                if (population != null && population.speciesAmount > 0)
                    SpeciesAccount(LedgerSpeciesId(population)).end +=
                        population.speciesAmount;
        }
        var accounts = new List<EcologySpeciesDailyAccount>(
            workingSpeciesLedger.Values);
        accounts.Sort((a, b) => string.CompareOrdinal(a.speciesId, b.speciesId));
        double end = ReadWorldMaterial();
        LastEcologyLedger = new DailyEcologyLedger
        {
            day = day, rulesVersion = "V6.1",
            sourceGraphHash = V61SourceGraphHash, seed = SimulationSeed,
            enableAirEcology = EnableAirEcology,
            enableWaterEcology = EnableWaterEcology,
            enableEconomy = EnableEconomy,
            enableRandomEvents = EnableRandomEvents,
            enableNeighborMovementFormula = EnableNeighborMovementFormula,
            worldMassStart = workingWorldMassStart,
            worldMassEnd = end, actualPlantGrowth = growth,
            namedDissipation = sinks,
            massResidual = end - workingWorldMassStart - growth + sinks,
            modelHash = enableEcologyLedgerDiagnostics ? CanonicalModelHash() : null,
            species = accounts.AsReadOnly()
        };
        OnDailyEcologyLedger?.Invoke(LastEcologyLedger);
    }
}
