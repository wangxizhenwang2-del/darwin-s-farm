// Copy this file to Assets/Editor/ in the Unity project when import is needed.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class DarwinAtlasImporter
{
    [Serializable] private class Network
    {
        public int schemaVersion;
        public Species[] species;
        public Link[] evolutionLinks;
        public Link[] foodLinks;
    }

    [Serializable] private class Species
    {
        public string id, name, description, image, baseEcologicalNiche;
        public int trophicLevel, baseMovementAbility, baseHabitatNiche,
            baseFitTemperature, baseFitHumidity, baseSize, baseFertility;
    }

    [Serializable] private class Link { public string from, to; }

    [MenuItem("Tools/Darwin Atlas/Import bioweb.json")]
    private static void Import()
    {
        string file = EditorUtility.OpenFilePanel("Select bioweb.json", "", "json");
        if (string.IsNullOrEmpty(file)) return;
        string json = File.ReadAllText(file);
        Network network = JsonUtility.FromJson<Network>(json);
        if (network == null || network.schemaVersion != 1 || network.species == null)
        {
            Debug.LogError("Darwin Atlas: unsupported or invalid JSON.");
            return;
        }

        const string folder = "Assets/DarwinAtlas/Species";
        EnsureFolder("Assets", "DarwinAtlas");
        EnsureFolder("Assets/DarwinAtlas", "Species");
        var byId = new Dictionary<string, SpeciesData>();
        foreach (Species item in network.species)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.id) || byId.ContainsKey(item.id))
            {
                Debug.LogError("Darwin Atlas: empty or duplicate species ID.");
                return;
            }
            string assetPath = folder + "/" + SafeFileName(item.id) + ".asset";
            SpeciesData asset = AssetDatabase.LoadAssetAtPath<SpeciesData>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<SpeciesData>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("speciesName").stringValue = item.name ?? "";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            asset.trophicLevel = Mathf.Clamp(item.trophicLevel, 0, 2);
            asset.baseMovementAbility = Mathf.Clamp(item.baseMovementAbility, 0, 100);
            asset.baseHabitatNiche = Mathf.Clamp(item.baseHabitatNiche, 0, 100);
            asset.baseFitTemperature = Mathf.Clamp(item.baseFitTemperature, 0, 100);
            asset.baseFitHumidity = Mathf.Clamp(item.baseFitHumidity, 0, 100);
            asset.baseSize = Mathf.Clamp(item.baseSize, 1, 100);
            asset.baseFertility = Mathf.Clamp(item.baseFertility, 0, 100);
            EcologicalNiche niche;
            asset.baseEcologicalNiche = Enum.TryParse(item.baseEcologicalNiche, out niche) ? niche : EcologicalNiche.Land;
            asset.evolutionTargets.Clear();
            byId.Add(item.id, asset);
            EditorUtility.SetDirty(asset);
        }

        // Each line is an undirected relationship: either species can evolve
        // into the other when its current traits fit the target species.
        if (network.evolutionLinks != null)
            foreach (Link link in network.evolutionLinks)
                if (link != null && byId.TryGetValue(link.from, out SpeciesData first)
                    && byId.TryGetValue(link.to, out SpeciesData second) && first != second)
                {
                    AddEvolutionTarget(first, second);
                    AddEvolutionTarget(second, first);
                }

        EnsureFolder("Assets", "Resources");
        EnsureFolder("Assets/Resources", "DarwinAtlas");
        File.WriteAllText("Assets/Resources/DarwinAtlas/bioweb.json", json);
        AssetDatabase.Refresh();
        AssetDatabase.SaveAssets();
        Debug.Log($"Darwin Atlas: imported {byId.Count} species. Food links are preserved in Resources/DarwinAtlas/bioweb.json but are not consumed by the current simulation.");
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            AssetDatabase.CreateFolder(parent, name);
    }

    private static void AddEvolutionTarget(SpeciesData source, SpeciesData target)
    {
        if (source.evolutionTargets.Contains(target)) return;
        source.evolutionTargets.Add(target);
        EditorUtility.SetDirty(source);
    }

    private static string SafeFileName(string id)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
        return id.Replace('/', '_').Replace('\\', '_');
    }
}
