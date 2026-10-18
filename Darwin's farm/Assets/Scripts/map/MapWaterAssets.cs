using System;
using UnityEngine;
[CreateAssetMenu(menuName = "Map/Water Assets")]
public sealed class MapWaterAssets : ScriptableObject
{
    [Serializable] public struct Shape { [Range(1, 15)] public int openings; public GameObject prefab; }
    public Shape[] shapes = Array.Empty<Shape>();
    public GameObject fullWaterPrefab;
    public Material bankMaterial, foundationMaterial, impactMaterial;
    public GameObject waterfallPrefab;
    public GameObject Resolve(MapTileDefinition definition, out int rotation)
    {
        rotation = 0;
        if (definition.waterVisualPrefab) return definition.waterVisualPrefab;
        if (definition.initialWaterCoverage == WaterCoverage.Lake || definition.initialWaterCoverage == WaterCoverage.Sea) return fullWaterPrefab;
        int desired = WaterBodyTopology.OpeningMask(definition, 0);
        foreach (Shape shape in shapes)
            for (int r = 0, mask = shape.openings; r < 4; r++, mask = ((mask << 1) & 15) | (mask >> 3))
                if (mask == desired && shape.prefab) { rotation = r; return shape.prefab; }
        return null;
    }
}
