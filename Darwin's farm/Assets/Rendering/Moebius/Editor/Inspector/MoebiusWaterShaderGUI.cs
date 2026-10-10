using UnityEditor;
using UnityEngine;

namespace Darwin.Rendering.Editor
{
    public sealed class MoebiusWaterShaderGUI : ShaderGUI
    {
        MaterialEditor editor;
        MaterialProperty[] properties;
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] materialProperties)
        {
            editor = materialEditor; properties = materialProperties;
            Group("Water Appearance", "_WaterColor", "_EnableVariation", "_ColorVariation", "_VariationScale");
            Group("Flow", "_EnableFlow", "_FlowDirection", "_FlowSpeed", "_FlowScale", "_FlowDistortion", "_FlowStrength");
            Group("Waves", "_EnableWaves", "_WaveHeight", "_WaveLength", "_WaveSpeed", "_WaveDirection");
            Group("Ripples", "_EnableRipples", "_RippleColor", "_RippleStrength", "_RippleDensity", "_RippleLength", "_RippleWidth", "_RippleSpeed", "_RippleDistortion", "_RippleWobbleStrength", "_RippleWobbleSpeed");
            Group("Shoreline Foam", "_EnableFoam", "_FoamColor", "_FoamWidth", "_FoamIntensity", "_FoamSpeed", "_FoamNoiseScale", "_FoamIrregularity",
                "_EnableFoamInk", "_FoamInkColor", "_FoamInkStrength", "_FoamInkWidth", "_EnableFoamPatches", "_FoamPatchStrength", "_EnableShoreMotion", "_ShoreMotionStrength");
            Group("Shoreline Mapping", "_EnableShoreMask", "_ShorelineMask", "_ShoreWorldRect", "_ShoreDistanceRange", "_ShoreWaveFade");
            Group("Cast Shadows", "_ReceiveWaterShadows", "_WaterShadowColor", "_WaterShadowStrength", "_WaterShadowDensity");
            EditorGUILayout.HelpBox("Select the water surface, then use Tools > Moebius > Water Setup > Update Shoreline. Actual land, rocks and Terrain are detected automatically; open water-mesh edges do not create foam. No shore list is needed. Update again after changing water placement or ground geometry.", MessageType.Info);
            if (GUILayout.Button("Open Water Setup")) MoebiusWaterSetup.Open();
            Group("Advanced", "_AnimationPhase", "_DebugView");
            editor.EnableInstancingField();
            EditorGUILayout.HelpBox("Use a subdivided horizontal mesh. This is opaque illustration water: no refraction, reflection or PBR lighting. Use the existing Moebius renderer for silhouette ink.", MessageType.Info);
        }
        void Group(string label, params string[] names)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            foreach (string name in names)
            {
                var property = FindProperty(name, properties);
                if (property.propertyType == UnityEngine.Rendering.ShaderPropertyType.Texture)
                    editor.TexturePropertySingleLine(new GUIContent(property.displayName), property);
                else editor.ShaderProperty(property, property.displayName);
            }
        }
    }
}
