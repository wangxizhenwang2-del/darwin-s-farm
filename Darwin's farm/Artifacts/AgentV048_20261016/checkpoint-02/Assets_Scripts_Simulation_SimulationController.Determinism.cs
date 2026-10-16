using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public partial class SimulationController
{
    public const string ModelRandomVersion = "DF-FNV64-1";
    [SerializeField] private int simulationSeed = 1;
    public int SimulationSeed => simulationSeed;

    // A complete ecology fingerprint for same-build replay comparisons.
    // Environment effect tracks are versioned separately by the action runner.
    public string CanonicalModelHash()
    {
        var data = new StringBuilder(4096);
        Add(data, ModelRandomVersion);
        Add(data, simulationSeed.ToString(CultureInfo.InvariantCulture));
        Add(data, currentSimulationDay.ToString(CultureInfo.InvariantCulture));
        AddFields(data, ReadTuning());
        AddFields(data, ReadLandRules());
        var blocks = new List<BlockInfo>(blockInfos);
        blocks.Sort((a, b) => string.CompareOrdinal(
            StableTileKey(a), StableTileKey(b)));
        foreach (BlockInfo block in blocks)
        {
            if (block == null) continue;
            Add(data, StableTileKey(block));
            Add(data, block.temperature.ToString(CultureInfo.InvariantCulture));
            Add(data, block.humidity.ToString(CultureInfo.InvariantCulture));
            Add(data, block.elevation.ToString(CultureInfo.InvariantCulture));
            Add(data, block.habitatRecovery.ToString(CultureInfo.InvariantCulture));
            Add(data, block.plantBiomass.ToString("R", CultureInfo.InvariantCulture));
            Add(data, block.maxPlantBiomass.ToString("R", CultureInfo.InvariantCulture));
            Add(data, block.detritusBiomass.ToString("R", CultureInfo.InvariantCulture));
            Add(data, block.plantGrowthToday.ToString("R", CultureInfo.InvariantCulture));
            Add(data, block.consumedBiomassToday.ToString("R", CultureInfo.InvariantCulture));
            Add(data, block.algaeBiomass.ToString("R", CultureInfo.InvariantCulture));
            Add(data, block.maxAlgaeBiomass.ToString("R", CultureInfo.InvariantCulture));
            Add(data, block.algaeRecovery.ToString(CultureInfo.InvariantCulture));
            Add(data, ((int)block.waterCoverage).ToString(CultureInfo.InvariantCulture));
            Add(data, block.nextLocalSplitDay.ToString(CultureInfo.InvariantCulture));
            var populations = block.community == null
                ? new List<PopulationData>() : new List<PopulationData>(block.community);
            populations.Sort((a, b) => string.CompareOrdinal(
                a?.PopulationId, b?.PopulationId));
            foreach (PopulationData population in populations)
            {
                if (population == null) continue;
                Add(data, population.PopulationId);
                AddFields(data, population);
            }
        }
        using (SHA256 sha = SHA256.Create())
        {
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(data.ToString()));
            var result = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) result.Append(value.ToString("x2"));
            return result.ToString();
        }
    }

    private static void AddFields(StringBuilder data, object value)
    {
        FieldInfo[] fields = value.GetType().GetFields(BindingFlags.Instance |
            BindingFlags.Public);
        Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
        foreach (FieldInfo field in fields)
        {
            Add(data, field.Name);
            object item = field.GetValue(value);
            if (item is List<float> numbers)
            {
                Add(data, numbers.Count.ToString(CultureInfo.InvariantCulture));
                foreach (float number in numbers)
                    Add(data, number.ToString("R", CultureInfo.InvariantCulture));
            }
            else if (item is List<EvolutionConversionRecord> records)
            {
                records = new List<EvolutionConversionRecord>(records);
                records.Sort((a, b) => string.CompareOrdinal(
                    a?.target == null ? string.Empty : StableSpeciesKey(a.target),
                    b?.target == null ? string.Empty : StableSpeciesKey(b.target)));
                foreach (EvolutionConversionRecord record in records)
                {
                    if (record == null) { Add(data, string.Empty); continue; }
                    Add(data, record.target == null ? string.Empty :
                        StableSpeciesKey(record.target));
                    Add(data, record.effectiveDays.ToString(CultureInfo.InvariantCulture));
                    Add(data, record.failedDraws.ToString(CultureInfo.InvariantCulture));
                    Add(data, record.day.ToString(CultureInfo.InvariantCulture));
                }
            }
            else if (item is SpeciesData species)
                Add(data, StableSpeciesKey(species));
            else if (item is BlockInfo block)
                Add(data, StableTileKey(block));
            else if (item is float single)
                Add(data, single.ToString("R", CultureInfo.InvariantCulture));
            else if (item is double number)
                Add(data, number.ToString("R", CultureInfo.InvariantCulture));
            else if (item is IFormattable formattable)
                Add(data, formattable.ToString(null, CultureInfo.InvariantCulture));
            else Add(data, item?.ToString());
        }
    }

    private static void Add(StringBuilder data, string value)
    {
        value = value ?? string.Empty;
        data.Append(value.Length).Append(':').Append(value);
    }

    // Call after the starting populations have been placed and before Day 1.
    // Existing save IDs are otherwise preserved by SetBlocks.
    public void ConfigureDeterministicRun(int seed)
    {
        if (currentSimulationDay > 0)
            throw new InvalidOperationException("Seed must be set before Day 1.");
        simulationSeed = seed;
        var seenTiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null) continue;
            string tile = StableTileKey(block);
            if (!seenTiles.Add(tile))
                throw new InvalidOperationException("Duplicate replay tile: " + tile);
        }
        SortModelState();
        foreach (BlockInfo block in blockInfos)
        {
            if (block == null || block.community == null) continue;
            block.community.Sort((a, b) =>
            {
                int bySpecies = string.CompareOrdinal(
                    a.species == null ? a.lineageName : StableSpeciesKey(a.species),
                    b.species == null ? b.lineageName : StableSpeciesKey(b.species));
                if (bySpecies != 0) return bySpecies;
                return string.CompareOrdinal(InitialPopulationKey(a),
                    InitialPopulationKey(b));
            });
            for (int i = 1; i < block.community.Count; i++)
                if (InitialPopulationKey(block.community[i - 1]) ==
                    InitialPopulationKey(block.community[i]))
                    throw new InvalidOperationException(
                        "Identical initial groups need a single population: " +
                        StableTileKey(block));
            for (int i = 0; i < block.community.Count; i++)
                block.community[i].AssignPopulationId("seed:" + seed + ":" +
                    StableTileKey(block) + ":" + i);
        }
    }

    private static string StableTileKey(BlockInfo block) =>
        block != null ? block.name ?? string.Empty : string.Empty;

    private static string InitialPopulationKey(PopulationData population)
    {
        if (population == null) return string.Empty;
        var data = new StringBuilder();
        AddFields(data, population);
        return data.ToString();
    }

    private void SortModelState()
    {
        blockInfos.Sort((a, b) => string.CompareOrdinal(
            StableTileKey(a), StableTileKey(b)));
        foreach (BlockInfo block in blockInfos)
            if (block != null && block.community != null)
                block.community.Sort((a, b) => string.CompareOrdinal(
                    a?.PopulationId, b?.PopulationId));
    }

    // Event-keyed draws do not depend on UI frames or draws in other systems.
    // The integer mixing and 24-bit conversion are part of the replay version.
    private float ModelRandom01(int day, string phase, BlockInfo source,
        PopulationData population, BlockInfo target = null, int ordinal = 0)
    {
        ulong hash = 14695981039346656037UL;
        MixInt(ref hash, simulationSeed);
        MixInt(ref hash, day);
        MixInt(ref hash, ordinal);
        MixText(ref hash, phase);
        MixText(ref hash, StableTileKey(source));
        MixText(ref hash, population?.PopulationId);
        MixText(ref hash, StableTileKey(target));
        hash ^= hash >> 30;
        hash *= 0xbf58476d1ce4e5b9UL;
        hash ^= hash >> 27;
        hash *= 0x94d049bb133111ebUL;
        hash ^= hash >> 31;
        return (hash >> 40) * (1f / 16777216f);
    }

    private static void MixInt(ref ulong hash, int value)
    {
        uint bits = unchecked((uint)value);
        for (int i = 0; i < 4; i++)
        {
            hash = unchecked((hash ^ (bits & 255U)) * 1099511628211UL);
            bits >>= 8;
        }
    }

    private static void MixText(ref ulong hash, string value)
    {
        if (value == null) value = string.Empty;
        MixInt(ref hash, value.Length);
        foreach (char character in value)
        {
            hash = unchecked((hash ^ (character & 255U)) * 1099511628211UL);
            hash = unchecked((hash ^ (ulong)(character >> 8)) * 1099511628211UL);
        }
    }
}
