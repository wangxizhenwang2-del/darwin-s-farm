using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class BioenergySettingsWindow : EditorWindow
{
    private const string SettingsPath = "Assets/Resources/BioenergySettings.asset";
    private static readonly (string field, string label)[] AccountFields =
    {
        ("initialBalance", "开局生物能"),
        ("dailyIncomeMultiplier", "日收入倍率"),
        ("harvestIncomeMultiplier", "收割收入倍率"),
        ("harvestPercent", "收割比例（%）"),
        ("minimumHarvestCount", "最低收割数量"),
        ("herbivoreUnlockReward", "食草物种首次解锁奖励"),
        ("levelOneUnlockReward", "一级捕食者首次解锁奖励"),
        ("levelTwoUnlockReward", "二级捕食者首次解锁奖励"),
    };
    private static readonly (string field, string label)[] ActionFields =
    {
        ("expandTileCost", "扩张地块"),
        ("reshapeWaterCost", "水道改造"),
        ("demolishTileCost", "拆除地块"),
        ("migrateCost", "整群迁移"),
        ("directedEvolutionCost", "定向进化"),
        ("storageCost", "建造生态仓并复制"),
    };
    private static readonly (string field, string label)[] TechnologyFields =
    {
        ("climateSmallCost", "温度／湿度 ±25"),
        ("climateLargeCost", "温度／湿度 ±50"),
        ("plantIncreaseSmallCost", "植物增加低档"),
        ("plantIncreaseLargeCost", "植物增加高档"),
        ("plantDecreaseSmallCost", "植物减少低档"),
        ("plantDecreaseLargeCost", "植物减少高档"),
        ("elevationCost", "海拔 ±1"),
        ("monsoonCost", "季风锚点"),
    };
    private static readonly (string field, string label)[] FallbackFields =
    {
        ("fallbackHerbivoreValue", "食草种群备用价值 V"),
        ("fallbackLevelOneValue", "一级捕食者备用价值 V"),
        ("fallbackLevelTwoValue", "二级捕食者备用价值 V"),
    };

    private readonly List<SpeciesData> species = new List<SpeciesData>();
    private BioenergySettings settings;
    private Vector2 scroll;

    [MenuItem("Darwin's Farm/经济系统数值")]
    private static void Open() => GetWindow<BioenergySettingsWindow>("经济系统数值");

    private void OnEnable()
    {
        minSize = new Vector2(410, 450);
        settings = AssetDatabase.LoadAssetAtPath<BioenergySettings>(SettingsPath);
        RefreshSpecies();
    }

    private void RefreshSpecies()
    {
        species.Clear();
        foreach (string guid in AssetDatabase.FindAssets("t:SpeciesData", new[] { "Assets" }))
        {
            SpeciesData entry = AssetDatabase.LoadAssetAtPath<SpeciesData>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (entry != null) species.Add(entry);
        }
        species.Sort((a, b) => string.Compare(a.SpeciesName, b.SpeciesName,
            StringComparison.Ordinal));
    }

    private void OnGUI()
    {
        if (settings == null)
        {
            EditorGUILayout.HelpBox("找不到 Assets/Resources/BioenergySettings.asset。", MessageType.Error);
            if (GUILayout.Button("重新读取")) OnEnable();
            return;
        }
        EditorGUILayout.HelpBox("修改后自动保存到项目资产。费用和收益会在下一次计算时使用新数值；开局余额在新开局时生效。", MessageType.Info);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        var serialized = new SerializedObject(settings);
        serialized.Update();
        EditorGUI.BeginChangeCheck();
        Section(serialized, "账户与收益", AccountFields);
        Section(serialized, "地图和种群操作费用", ActionFields);
        Section(serialized, "环境科技费用", TechnologyFields);
        Section(serialized, "无物种资产种群的备用价值", FallbackFields);
        if (EditorGUI.EndChangeCheck())
        {
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("物种价值 V", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("直接编辑 SpeciesData 资产的生物能价值；范围 10～100。新增物种后点击刷新。", MessageType.None);
        if (GUILayout.Button("刷新物种列表")) RefreshSpecies();
        foreach (SpeciesData entry in species)
        {
            var item = new SerializedObject(entry);
            item.Update();
            EditorGUI.BeginChangeCheck();
            string label = string.IsNullOrEmpty(entry.SpeciesName) ? entry.name : entry.SpeciesName;
            EditorGUILayout.PropertyField(item.FindProperty("bioenergyValue"),
                new GUIContent(label));
            if (EditorGUI.EndChangeCheck())
            {
                item.ApplyModifiedProperties();
                EditorUtility.SetDirty(entry);
                AssetDatabase.SaveAssets();
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private static void Section(SerializedObject target, string title,
        (string field, string label)[] fields)
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        foreach (var field in fields)
            EditorGUILayout.PropertyField(target.FindProperty(field.field),
                new GUIContent(field.label));
    }
}
