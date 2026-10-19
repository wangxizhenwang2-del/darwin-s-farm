using System.Collections.Generic;
using UnityEngine;

// Player operation panel. All writes go through PlayerEconomyController.
public sealed class EconomyOperationsView : MonoBehaviour
{
    private MapGridManager grid;
    private PlayerEconomyController operations;
    private BioenergyAccount account;
    private TilePaletteUI palette;
    private SpeciesMapsUI speciesMaps;
    private readonly List<MapTileInstance> tiles = new List<MapTileInstance>();
    private readonly List<MapTileDefinition> waterShapes = new List<MapTileDefinition>();
    private Vector2 scroll;
    private Vector2Int sourceCoordinate, targetCoordinate;
    private bool hasSource, hasTarget, open;
    private int populationIndex, firstTrait, secondTrait = 1, firstDelta = 2, secondDelta = 2;
    private string notice;
    private static EconomyOperationsView active;
    private Rect toggleRect, panelRect;
    private static readonly int[] Deltas = { -50, -25, 25, 50 };
    private static readonly string[] Traits = { "适温", "适湿", "运动", "繁殖", "体型" };
    private static readonly string[] DeltaNames = { "−50", "−25", "+25", "+50" };
    private string Fee(int amount) => account.CostsEnabled ? amount.ToString() : "0";
    private static string Openings(int mask)
    {
        string result = "";
        if ((mask & 1) != 0) result += "北";
        if ((mask & 2) != 0) result += "东";
        if ((mask & 4) != 0) result += "南";
        if ((mask & 8) != 0) result += "西";
        return result;
    }

    private void Awake()
    {
        grid = GetComponent<MapGridManager>();
        operations = GetComponent<PlayerEconomyController>();
        account = GetComponent<BioenergyAccount>();
        palette = FindFirstObjectByType<TilePaletteUI>();
        speciesMaps = FindFirstObjectByType<SpeciesMapsUI>();
    }
    private void OnEnable() => active = this;
    private void OnDisable() { if (active == this) active = null; }

    public static bool IsPointerOverUi(Vector2 screenPosition)
    {
        if (active == null || (active.speciesMaps != null && active.speciesMaps.IsOpen)) return false;
        Vector2 gui = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        return active.toggleRect.Contains(gui) || (active.open && active.panelRect.Contains(gui));
    }

    private void OnGUI()
    {
        if (grid == null || operations == null || account == null) return;
        if (speciesMaps != null && speciesMaps.IsOpen) return;
        BioenergySettings settings = BioenergySettings.Current;
        toggleRect = new Rect(Screen.width - 128, 8, 120, 25);
        if (GUI.Button(toggleRect, open ? "关闭经营面板" : "经营操作")) open = !open;
        if (operations.IsGameLost)
        {
            Rect loss = new Rect(Screen.width * .5f - 160, Screen.height * .5f - 45, 320, 90);
            GUI.Box(loss, "所有野外动物灭绝，且没有生态仓备份");
            if (GUI.Button(new Rect(loss.x + 70, loss.y + 48, 180, 27), "重新开局"))
                operations.RestartGame();
        }
        if (!open) return;
        panelRect = new Rect(Screen.width - 398, 38, 390, Mathf.Min(650, Screen.height - 46));
        GUILayout.BeginArea(panelRect, GUI.skin.box);
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Label("生物能 " + account.Balance + "；日收预估 " + account.PreviewDailyIncome());
        GUILayout.Label("当前所有玩家操作免费");
        GUILayout.Label("选择来源和目标地块");
        tiles.Clear();
        foreach (MapTileInstance tile in grid.GetPlacedTiles()) tiles.Add(tile);
        tiles.Sort((a, b) => a.Coordinate.x != b.Coordinate.x ?
            a.Coordinate.x.CompareTo(b.Coordinate.x) : a.Coordinate.y.CompareTo(b.Coordinate.y));
        foreach (MapTileInstance tile in tiles)
        {
            GUILayout.BeginHorizontal();
            string label = tile.Coordinate + " " + tile.Definition.displayName;
            if (GUILayout.Button((hasSource && sourceCoordinate == tile.Coordinate ? "● " : "○ ") + label,
                GUILayout.Width(240)))
            { sourceCoordinate = tile.Coordinate; hasSource = true; populationIndex = 0; }
            if (GUILayout.Button(hasTarget && targetCoordinate == tile.Coordinate ? "目标✓" : "设为目标"))
            { targetCoordinate = tile.Coordinate; hasTarget = true; }
            GUILayout.EndHorizontal();
        }
        if (hasSource && grid.TryGetTile(sourceCoordinate, out MapTileInstance sourceTile))
        {
            BlockInfo source = sourceTile.Block;
            GUILayout.Space(6);
            GUILayout.Label("来源 " + sourceCoordinate);
            if (sourceCoordinate != Vector2Int.zero && GUILayout.Button("拆除地块 · " + Fee(settings.demolishTileCost)))
                notice = operations.TryDemolish(sourceCoordinate, out string error) ?
                    "地块已拆除" : error;
            if (source != null && source.waterCoverage != WaterCoverage.Land)
            {
                if (operations.TryPreviewWaterShape(sourceCoordinate, sourceTile.Definition,
                    sourceTile.RotationSteps + 1, out var shapePreview, out _))
                    GUILayout.Label("旋转预览：开口 " + Openings(shapePreview.OpeningsBefore) + "→" +
                        Openings(shapePreview.OpeningsAfter) + "；连通水域 " +
                        shapePreview.WaterAreaBefore + "→" + shapePreview.WaterAreaAfter +
                        " 格；涉及 " + shapePreview.ResidentGroupsInCurrentArea + " 群");
                if (GUILayout.Button("水道顺时针旋转 · " + Fee(settings.reshapeWaterCost)))
                    notice = operations.TryReshapeWater(sourceCoordinate, sourceTile.Definition,
                        sourceTile.RotationSteps + 1, out string error) ? "水道已旋转" : error;
            }
            if (source != null && source.waterCoverage != WaterCoverage.Land)
            {
                waterShapes.Clear();
                if (palette != null && palette.Definitions != null)
                    foreach (MapTileDefinition shape in palette.Definitions)
                        if (shape != null && shape.initialWaterCoverage != WaterCoverage.Land &&
                            !waterShapes.Contains(shape)) waterShapes.Add(shape);
                foreach (MapTileInstance placed in tiles)
                    if (placed.Definition != null &&
                        placed.Definition.initialWaterCoverage != WaterCoverage.Land &&
                        !waterShapes.Contains(placed.Definition)) waterShapes.Add(placed.Definition);
                foreach (MapTileDefinition shape in waterShapes)
                    if (shape != sourceTile.Definition)
                    {
                        if (operations.TryPreviewWaterShape(sourceCoordinate, shape, 0,
                            out var shapePreview, out _))
                            GUILayout.Label(shape.displayName + "：连通水域 " +
                                shapePreview.WaterAreaBefore + "→" +
                                shapePreview.WaterAreaAfter + " 格");
                        if (GUILayout.Button("改为 " + shape.displayName + " · " + Fee(settings.reshapeWaterCost)))
                        { notice = operations.TryReshapeWater(sourceCoordinate, shape, 0,
                              out string error) ? "水道形状已改变" : error; break; }
                    }
            }
            if (source != null && source.community != null && source.community.Count > 0)
            {
                populationIndex = Mathf.Clamp(populationIndex, 0, source.community.Count - 1);
                string[] options = new string[source.community.Count];
                for (int i = 0; i < options.Length; i++)
                {
                    PopulationData p = source.community[i];
                    options[i] = (p.species != null ? p.species.SpeciesName : p.lineageName) +
                        " ×" + p.speciesAmount;
                }
                populationIndex = GUILayout.SelectionGrid(populationIndex, options, 2);
                PopulationData selected = source.community[populationIndex];
                if (selected != null && selected.speciesAmount > 0)
                {
                    operations.TryPreviewHarvest(source, selected,
                        out PlayerEconomyController.HarvestPreview harvest, out _);
                    if (harvest != null) GUILayout.Label("收割 " + harvest.Harvested +
                        "，此群剩余 " + harvest.Remaining + "，收入 " + harvest.Earned +
                        "，次日日收预计 " + harvest.NextDailyIncome +
                        (harvest.SpeciesExtinct ? "；物种将于野外灭绝" : ""));
                    if (GUILayout.Button("收割种群"))
                        notice = operations.TryHarvest(source, selected, out _, out _,
                            out string error) ? "收割完成" : error;
                    if (GUILayout.Button("建造生态仓并复制 50 只 · " + Fee(settings.storageCost)))
                        notice = operations.TryStore(source, selected, out _, out string error) ?
                            "备份已保存" : error;
                    if (hasTarget && grid.TryGetTile(targetCoordinate, out MapTileInstance targetTile) &&
                        GUILayout.Button("全群迁移到目标 · " + Fee(settings.migrateCost)))
                        notice = operations.TryMigrate(source, selected, targetTile.Block,
                            out string error) ? "迁移完成" : error;
                    GUILayout.Label("定向进化（两项性状，各 ±25／±50） · " + Fee(settings.directedEvolutionCost));
                    firstTrait = GUILayout.SelectionGrid(firstTrait, Traits, 5);
                    firstDelta = GUILayout.SelectionGrid(firstDelta, DeltaNames, 4);
                    secondTrait = GUILayout.SelectionGrid(secondTrait, Traits, 5);
                    secondDelta = GUILayout.SelectionGrid(secondDelta, DeltaNames, 4);
                    if (GUILayout.Button("提交定向进化"))
                        notice = operations.TryDirectedEvolution(source, selected,
                            (DirectedTrait)firstTrait, Deltas[firstDelta],
                            (DirectedTrait)secondTrait, Deltas[secondDelta], out string error) ?
                            "性状已修改" : error;
                }
            }
        }
        GUILayout.Space(6);
        GUILayout.Label("生态仓：" + operations.Storage.Count + " 个备份");
        foreach (PlayerEconomyController.StoredPopulation entry in operations.Storage)
        {
            PopulationData copy = entry.copy;
            GUILayout.Label((copy.species != null ? copy.species.SpeciesName : copy.lineageName) +
                " ×50");
            if (hasTarget && grid.TryGetTile(targetCoordinate, out MapTileInstance tile) &&
                GUILayout.Button("免费释放到 " + targetCoordinate))
            { notice = operations.TryRelease(entry.id, tile.Block, out string error) ?
                "备份已释放，仓位消失" : error; break; }
        }
        if (!string.IsNullOrEmpty(notice)) GUILayout.Label(notice);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}
