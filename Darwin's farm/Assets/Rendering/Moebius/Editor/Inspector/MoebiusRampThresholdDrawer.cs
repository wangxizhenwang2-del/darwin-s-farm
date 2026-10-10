using UnityEditor;
using UnityEngine;

// Property drawer only for the two new ramp thresholds; the existing material inspector stays intact.
public sealed class MoebiusRampThresholdDrawer : MaterialPropertyDrawer
{
    readonly bool isLight;
    const float MinimumGap = .001f;
    public MoebiusRampThresholdDrawer(float lightThreshold) { isLight = lightThreshold > .5f; }

    public override void OnGUI(Rect position, MaterialProperty property, string label, MaterialEditor editor)
    {
        EditorGUI.showMixedValue = property.hasMixedValue;
        EditorGUI.BeginChangeCheck();
        float value = EditorGUI.Slider(position, label, property.floatValue, 0, 1);
        if(EditorGUI.EndChangeCheck())
        {
            editor.RegisterPropertyChangeUndo(label);
            foreach(Object target in property.targets)
            {
                var material = (Material)target;
                material.SetFloat(isLight ? "_RampLightThreshold" : "_RampShadowThreshold", value);
                Normalize(material);
                EditorUtility.SetDirty(material);
            }
        }
        EditorGUI.showMixedValue = false;
    }

    public static void Normalize(Material material)
    {
        float shadow = Mathf.Clamp(material.GetFloat("_RampShadowThreshold"), 0, 1-MinimumGap);
        float light = Mathf.Clamp(material.GetFloat("_RampLightThreshold"), shadow+MinimumGap, 1);
        material.SetFloat("_RampShadowThreshold",shadow);
        material.SetFloat("_RampLightThreshold",light);
    }
}
