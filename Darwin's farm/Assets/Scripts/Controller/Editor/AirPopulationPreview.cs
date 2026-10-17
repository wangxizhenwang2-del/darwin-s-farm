using System.Linq;
using UnityEditor;
using UnityEngine;

// A convenient viewing setup, independent of the destructive regression fixture.
// Population changes live only in the current Play session. Keep the normal
// game camera, projection, zoom and player-follow behavior untouched.
public static class AirPopulationPreview
{
    private const string Menu = "Tools/Darwin's Farm/Population/Preview air population (Play Mode)";

    [MenuItem(Menu)]
    public static void Show()
    {
        if (!Application.isPlaying) return;
        MapGridManager grid = Object.FindFirstObjectByType<MapGridManager>();
        if (grid == null) { Debug.LogWarning("Open WhiteBox and enter Play Mode first."); return; }
        MapTileInstance tile = grid.GetPlacedTiles().FirstOrDefault();
        if (tile == null || tile.Block == null) { Debug.LogWarning("Wait for the first map tile to load."); return; }
        PopulationData air = tile.Block.community.FirstOrDefault(p => p != null &&
            p.speciesAmount > 0 && p.ecologicalNiche == EcologicalNiche.Air);
        if (air == null)
        {
            SpeciesData species = AssetDatabase.LoadAssetAtPath<SpeciesData>(
                "Assets/BiowebV46/Species/鹿飞-a61486cd.asset");
            if (species == null) { Debug.LogError("Missing 鹿飞 species asset."); return; }
            // Only add a representative to an available population entry.
            if (tile.Block.community.Count(p => p != null && p.speciesAmount > 0) >= 5)
            {
                tile = grid.GetPlacedTiles().FirstOrDefault(t => t.Block != null &&
                    !SimulationController.HasAirPopulation(t.Block) &&
                    t.Block.community.Count(p => p != null && p.speciesAmount > 0) < 5);
                if (tile == null) { Debug.LogWarning("No free tile sky/population slot for preview."); return; }
            }
            air = new PopulationData
            {
                species = species, speciesId = species.SpeciesId, lineageName = species.SpeciesName,
                ecologicalNiche = EcologicalNiche.Air, speciesAmount = 80,
                size = species.baseSize, movementAbility = species.baseMovementAbility,
                fertility = species.baseFertility, fitTemperature = species.baseFitTemperature,
                fitHumidity = species.baseFitHumidity, habitatNiche = 100,
                trophicLevel = species.trophicLevel, trophicLevelInitialized = true
            };
            tile.Block.community.Add(air);
            grid.GetComponent<SimulationController>()?.RefreshSpeciesStatuses();
            grid.GetComponent<SimulationController>()?.NotifyPlayerCommunityChanged(tile.Block);
        }
        // Pause ecology so food pressure cannot remove/migrate the demonstration
        // while the user watches; rendering and animal motion continue normally.
        grid.GetComponent<SimulationTime>()?.Pause();
        grid.GetComponent<PopulationVisualPresenter>()?.SyncNow();
        Debug.Log($"空中预览：{air.lineageName}，{air.speciesAmount} 只，所属地块 {tile.Coordinate}。使用当前游戏视角，生态计时已暂停，飞行继续；退出 Play 恢复地图。", tile);
    }

    [MenuItem(Menu, true)]
    private static bool CanShow() => EditorApplication.isPlaying && !EditorApplication.isPaused;
}
