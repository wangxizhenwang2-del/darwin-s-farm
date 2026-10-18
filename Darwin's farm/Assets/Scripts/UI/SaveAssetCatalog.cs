using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Darwin's Farm/Save Asset Catalog")]
public sealed class SaveAssetCatalog : ScriptableObject
{
    [Serializable] public struct TileEntry { public string id; public MapTileDefinition asset; }
    [Serializable] public struct SpeciesEntry { public string id; public SpeciesData asset; }
    public TileEntry[] tiles;
    public SpeciesEntry[] species;

    public string TileId(MapTileDefinition asset)
    {
        foreach (var entry in tiles) if (entry.asset == asset) return entry.id;
        throw new InvalidOperationException("Unregistered map tile: " + (asset ? asset.name : "null"));
    }
    public MapTileDefinition Tile(string id)
    {
        foreach (var entry in tiles) if (entry.id == id) return entry.asset;
        throw new InvalidOperationException("Missing saved map tile: " + id);
    }
    public string SpeciesId(SpeciesData asset)
    {
        if (asset == null) return null;
        foreach (var entry in species) if (entry.asset == asset) return entry.id;
        throw new InvalidOperationException("Unregistered species: " + asset.name);
    }
    public SpeciesData Species(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var entry in species) if (entry.id == id) return entry.asset;
        throw new InvalidOperationException("Missing saved species: " + id);
    }
}
