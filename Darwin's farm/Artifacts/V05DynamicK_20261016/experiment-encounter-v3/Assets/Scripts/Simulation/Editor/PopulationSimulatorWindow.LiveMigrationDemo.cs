using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class PopulationSimulatorWindow
{
    [SerializeField] private int liveMigrationPopulationIndex;
    [SerializeField] private int liveMigrationTargetIndex;

    private void DrawLiveMigrationDemo(BlockInfo source)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("迁徙观察（Play 模式测试）", EditorStyles.boldLabel);
        var populations = new List<PopulationData>();
        var names = new List<string>();
        if (source.community != null)
            foreach (PopulationData population in source.community)
                if (population != null && population.speciesAmount > 0 &&
                    population.ecologicalNiche == EcologicalNiche.Land)
                {
                    populations.Add(population);
                    names.Add((population.species != null ? population.species.SpeciesName :
                        population.lineageName) + " · " + population.speciesAmount);
                }

        var targets = new List<MapTileInstance>();
        var targetNames = new List<string>();
        if (source.Neighbors != null)
            foreach (MapTileInstance tile in liveGrid.GetPlacedTiles())
            {
                if (tile == null || tile.Block == null ||
                    tile.Block.waterCoverage != WaterCoverage.Land) continue;
                foreach (BlockInfo neighbor in source.Neighbors)
                    if (tile.Block == neighbor)
                    {
                        targets.Add(tile);
                        targetNames.Add("地块 " + tile.Coordinate);
                        break;
                    }
            }

        if (populations.Count == 0 || targets.Count == 0)
        {
            EditorGUILayout.LabelField(populations.Count == 0 ?
                "当前地块没有陆生种群。" : "当前地块没有相邻陆地目标。",
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            return;
        }

        liveMigrationPopulationIndex = Mathf.Clamp(liveMigrationPopulationIndex, 0,
            populations.Count - 1);
        liveMigrationTargetIndex = Mathf.Clamp(liveMigrationTargetIndex, 0,
            targets.Count - 1);
        liveMigrationPopulationIndex = EditorGUILayout.Popup("源种群",
            liveMigrationPopulationIndex, names.ToArray());
        liveMigrationTargetIndex = EditorGUILayout.Popup("目标地块",
            liveMigrationTargetIndex, targetNames.ToArray());
        PopulationData selected = populations[liveMigrationPopulationIndex];
        BlockInfo target = targets[liveMigrationTargetIndex].Block;
        float sourceFitness = SimulationController.CalculateFitness(source, selected);
        float targetFitness = SimulationController.CalculateFitness(target, selected);
        float pressure = liveSimulation.CalculateOverloadPressure(selected);
        EditorGUILayout.LabelField($"源地适应度 {sourceFitness:F2} · 目标地 {targetFitness:F2} · 超载压力 {pressure:F2}",
            EditorStyles.miniLabel);
        if (targetFitness <= sourceFitness && pressure <= 0f)
            EditorGUILayout.HelpBox("此目标地更不适宜且源地未超载，自动迁徙不会选择它。",
                MessageType.Info);
        bool eligible = selected.speciesAmount >= 10 &&
            liveTime.currentDay >= selected.nextMigrationDay &&
            source.waterCoverage == WaterCoverage.Land;
        using (new EditorGUI.DisabledScope(!eligible))
            if (GUILayout.Button("手动迁徙一次，观察动画"))
            {
                int before = selected.speciesAmount;
                liveSimulation.MovePopulation(source, target, selected,
                    liveTime.currentDay);
                int moved = before - selected.speciesAmount;
                liveMessage = moved > 0 ? $"已迁徙 {moved} 只到 {targets[liveMigrationTargetIndex].Coordinate}；请观察 Game 视图。" :
                    "迁徙未执行，请检查生态位与目标地块。";
                if (moved > 0)
                {
                    LogLive(source, liveMessage);
                    WriteExportAction("simulator_window", "manual_migration",
                        liveCoordinate, "population=" + selected.PopulationId +
                        ";amount=" + moved + ";to=" + targets[liveMigrationTargetIndex].Coordinate);
                }
            }
        if (!eligible)
            EditorGUILayout.LabelField(selected.speciesAmount < 10 ? "数量不足 10，只能在达到门槛后迁徙。" :
                $"冷却中，最早 Day {selected.nextMigrationDay} 可再次迁徙。",
                EditorStyles.miniLabel);
        EditorGUILayout.LabelField("此按钮使用模拟器现有的手动迁徙规则；自然迁徙仍按日程与概率判定。",
            EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
    }
}
