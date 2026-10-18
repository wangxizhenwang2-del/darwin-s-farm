using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;

// Whitebox setup validates the imported ScriptableObjects against the exact
// immutable V6.1 graph. Runtime simulation continues to use existing assets.
internal static class V61BiowebValidator
{

    [Serializable] private sealed class Graph
    {
        public int schemaVersion;
        public GraphSpecies[] species;
        public Link[] evolutionLinks, foodLinks;
    }
    [Serializable] private sealed class GraphSpecies
    {
        public string id, name, baseEcologicalNiche;
        public int baseFitTemperature, baseFitHumidity;
        public int baseMovementAbility, baseSize, baseFertility, trophicLevel;
    }
    [Serializable] private sealed class Link { public string from, to; }

    private static string Id(string value) =>
        value == null ? "" : value.Replace("-", "").ToLowerInvariant();

    internal static bool TryValidate(IReadOnlyList<SpeciesData> assets,
        out string error)
    {
        error = null;
        TextAsset source = Resources.Load<TextAsset>("V61Bioweb13");
        if (source == null) { error = "V6.1 source graph is missing"; return false; }
        string hash;
        using (SHA256 sha = SHA256.Create())
            hash = BitConverter.ToString(sha.ComputeHash(source.bytes))
                .Replace("-", "").ToLowerInvariant();
        if (hash != SimulationController.V61SourceGraphHash)
        { error = "V6.1 source graph hash mismatch"; return false; }
        Graph graph;
        try { graph = JsonUtility.FromJson<Graph>(source.text); }
        catch (Exception exception)
        { error = "V6.1 graph parse failed: " + exception.Message; return false; }
        if (graph == null || graph.schemaVersion != 1 ||
            graph.species == null || graph.species.Length != 15 ||
            graph.evolutionLinks == null || graph.evolutionLinks.Length != 20 ||
            graph.foodLinks == null || graph.foodLinks.Length != 25 ||
            assets == null || assets.Count != 15)
        { error = "V6.1 graph counts are incomplete"; return false; }
        var byId = new Dictionary<string, SpeciesData>(StringComparer.Ordinal);
        foreach (SpeciesData asset in assets)
            if (asset == null || !byId.TryAdd(Id(asset.SpeciesId), asset))
            { error = "Missing or duplicate imported species ID"; return false; }
        foreach (GraphSpecies entry in graph.species)
        {
            if (entry == null || !byId.TryGetValue(Id(entry.id),
                out SpeciesData asset) ||
                asset.trophicLevel != entry.trophicLevel ||
                asset.baseEcologicalNiche.ToString() != entry.baseEcologicalNiche ||
                asset.baseMovementAbility != entry.baseMovementAbility ||
                asset.baseSize != entry.baseSize ||
                asset.baseFertility != entry.baseFertility ||
                entry.baseEcologicalNiche == "Land" &&
                (asset.baseFitTemperature != entry.baseFitTemperature ||
                 asset.baseFitHumidity != entry.baseFitHumidity))
            { error = "Imported species template differs from V6.1 graph: " +
                (entry != null ? entry.name : "null"); return false; }
        }
        var evolution = new HashSet<string>(StringComparer.Ordinal);
        foreach (Link link in graph.evolutionLinks)
        {
            evolution.Add(Id(link.from) + ">" + Id(link.to));
            evolution.Add(Id(link.to) + ">" + Id(link.from));
        }
        var food = new HashSet<string>(StringComparer.Ordinal);
        foreach (Link link in graph.foodLinks)
            food.Add(Id(link.to) + ">" + Id(link.from));
        var actualEvolution = new HashSet<string>(StringComparer.Ordinal);
        var actualFood = new HashSet<string>(StringComparer.Ordinal);
        foreach (SpeciesData predator in assets)
        {
            if (predator.evolutionTargets != null)
                foreach (SpeciesData target in predator.evolutionTargets)
                    if (target != null)
                        actualEvolution.Add(Id(predator.SpeciesId) + ">" +
                            Id(target.SpeciesId));
            if (predator.preySpecies != null)
                foreach (SpeciesData prey in predator.preySpecies)
                    if (prey != null)
                        actualFood.Add(Id(predator.SpeciesId) + ">" +
                            Id(prey.SpeciesId));
        }
        if (!evolution.SetEquals(actualEvolution) ||
            !food.SetEquals(actualFood))
        { error = "Imported evolution or food edges differ from V6.1 graph";
          return false; }
        return true;
    }
}
