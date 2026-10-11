using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public partial class PopulationSimulatorWindow
{
    [SerializeField] private LandRuleTuning landRuleTuning = new LandRuleTuning();
    [SerializeField] private bool showLandTuning;
    private static readonly FieldInfo[] LandTuningFields =
        typeof(LandRuleTuning).GetFields(BindingFlags.Public | BindingFlags.Instance);
    private static readonly Dictionary<string, string> LandTuningLabels =
        new Dictionary<string, string>
        {
            { nameof(LandRuleTuning.coldDeathRate), "寒冷死亡系数" },
            { nameof(LandRuleTuning.movementFoodCost), "运动耗食系数" },
            { nameof(LandRuleTuning.youngLinearCost), "幼体线性耗食" },
            { nameof(LandRuleTuning.youngQuadraticCost), "幼体平方耗食" },
            { nameof(LandRuleTuning.competitionPressure), "同食物竞争强度" },
            { nameof(LandRuleTuning.oppositeStrategyCompetition), "异策略互压系数" },
            { nameof(LandRuleTuning.movementForagingGain), "运动觅食增益" },
            { nameof(LandRuleTuning.minimumPlantReserveFraction), "植物最低库存比例" },
            { nameof(LandRuleTuning.dailyPlantStockDrawFraction), "每日库存最大消耗比例" },
            { nameof(LandRuleTuning.densityBirthDampingStart), "出生减速起点：可持续 K 比例" },
            { nameof(LandRuleTuning.baseCaptureChance), "基础捕获率" },
            { nameof(LandRuleTuning.chaseScale), "追逃能力尺度" },
            { nameof(LandRuleTuning.preyEnergyPerSize), "每体型猎物能量" },
            { nameof(LandRuleTuning.reserveHuntFraction), "额外储备捕猎比例" },
            { nameof(LandRuleTuning.reserveDays), "捕食者储备天数" },
            { nameof(LandRuleTuning.splitMinimumFoodSatisfaction), "分支最低吃饱率" },
            { nameof(LandRuleTuning.splitMinimumRenewalPressure), "分化门槛：当前种群 / 环境 K" },
            { nameof(LandRuleTuning.splitMaximumRenewalLoad), "分支后最高补充负载" },
            { nameof(LandRuleTuning.splitHeadroomUse), "分化时大体型占用剩余 K 比例" },
            { nameof(LandRuleTuning.splitStockDays), "分支最低库存天数" },
            { nameof(LandRuleTuning.splitCooldownDays), "原地分支冷却天数" },
            { nameof(LandRuleTuning.evolutionTodaySatisfaction), "转化当日最低吃饱率" },
            { nameof(LandRuleTuning.evolutionRenewalSatisfaction), "转化最低持续补充率" },
            { nameof(LandRuleTuning.evolutionPreyRenewalCoverage), "转化猎物补充覆盖率" },
            { nameof(LandRuleTuning.evolutionRequiredDays), "转化连续合格天数" },
            { nameof(LandRuleTuning.evolutionRequiredDaysAtFour), "四群转化合格天数" },
            { nameof(LandRuleTuning.evolutionTraitTolerance), "转化单项性状容差" },
            { nameof(LandRuleTuning.evolutionCooldownDays), "物种转化冷却天数" },
            { nameof(LandRuleTuning.neighborAttractionThreshold), "邻地吸引门槛" },
            { nameof(LandRuleTuning.attractionMovementGain), "邻地运动增益" },
            { nameof(LandRuleTuning.flatMigrationThreshold), "平地迁徙运动门槛" },
            { nameof(LandRuleTuning.slopeMigrationThreshold), "坡地迁徙运动门槛" },
            { nameof(LandRuleTuning.flatAttractionCeiling), "平地吸引运动上限" },
            { nameof(LandRuleTuning.slopeAttractionCeiling), "坡地吸引运动上限" },
            { nameof(LandRuleTuning.fourEntryMigrationFactor), "四群迁入概率倍率" }
        };

    private void DrawLandTuning()
    {
        if (landRuleTuning == null) landRuleTuning = new LandRuleTuning();
        showLandTuning = EditorGUILayout.Foldout(showLandTuning,
            "陆地规则详细参数", true);
        if (!showLandTuning) return;
        EditorGUI.indentLevel++;
        EditorGUILayout.HelpBox("出生率和饥饿死亡率使用上方设置；此处其余参数应用于下一模拟日。", MessageType.None);
        foreach (FieldInfo field in LandTuningFields)
        {
            if (field.Name == nameof(LandRuleTuning.birthRate) ||
                field.Name == nameof(LandRuleTuning.starvationDeathRate)) continue;
            string label = LandTuningLabels.TryGetValue(field.Name, out string translated)
                ? translated : ObjectNames.NicifyVariableName(field.Name);
            if (field.FieldType == typeof(float))
            {
                float value = EditorGUILayout.FloatField(label,
                    (float)field.GetValue(landRuleTuning));
                RangeAttribute range = Attribute.GetCustomAttribute(field,
                    typeof(RangeAttribute)) as RangeAttribute;
                MinAttribute minimum = Attribute.GetCustomAttribute(field,
                    typeof(MinAttribute)) as MinAttribute;
                if (range != null) value = Mathf.Clamp(value, range.min, range.max);
                else if (minimum != null) value = Mathf.Max(minimum.min, value);
                field.SetValue(landRuleTuning, value);
            }
            else if (field.FieldType == typeof(int))
            {
                int value = EditorGUILayout.IntField(label,
                    (int)field.GetValue(landRuleTuning));
                MinAttribute minimum = Attribute.GetCustomAttribute(field,
                    typeof(MinAttribute)) as MinAttribute;
                if (minimum != null) value = Mathf.Max(Mathf.CeilToInt(minimum.min), value);
                field.SetValue(landRuleTuning, value);
            }
        }
        EditorGUI.indentLevel--;
    }

    private void ApplyLandTuning()
    {
        if (controller == null) return;
        var tuning = (landRuleTuning ?? new LandRuleTuning()).Copy();
        tuning.birthRate = reproductionScale;
        tuning.starvationDeathRate = maxStarvationDeathRate;
        controller.ApplyLandRules(tuning);
    }
}
