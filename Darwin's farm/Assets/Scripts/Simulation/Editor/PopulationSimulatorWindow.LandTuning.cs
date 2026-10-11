using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public partial class PopulationSimulatorWindow
{
    [SerializeField] private LandRuleTuning landRuleTuning = new LandRuleTuning();
    [SerializeField] private bool showLandTuning;
    private static readonly FieldInfo[] LandTuningFields =
        typeof(LandRuleTuning).GetFields(BindingFlags.Public | BindingFlags.Instance);

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
            string label = ObjectNames.NicifyVariableName(field.Name);
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
