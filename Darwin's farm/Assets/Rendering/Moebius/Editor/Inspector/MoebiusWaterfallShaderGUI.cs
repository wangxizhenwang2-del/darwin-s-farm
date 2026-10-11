using UnityEditor;
using UnityEngine;

namespace Darwin.Rendering.Editor
{
    public sealed class MoebiusWaterfallShaderGUI : ShaderGUI
    {
        public override void OnGUI(MaterialEditor editor,MaterialProperty[] properties)
        {
            bool body=((Material)editor.target).shader.name=="Darwin/Moebius Waterfall";
            bool splash=((Material)editor.target).shader.name=="Darwin/Moebius Waterfall Splash";
            if(body)
            {
                Group(editor,properties,"Colors","_BaseColor","_ShadowColor","_HighlightColor","_FlowLineColor","_InkLineColor");
                Group(editor,properties,"Flow","_FlowSpeed","_FlowScale","_DistortionStrength","_DistortionScale","_BandStrength");
                Group(editor,properties,"Lines","_VerticalLineDensity","_VerticalLineWidth","_FlowLineStrength","_InkLineStrength");
                Group(editor,properties,"Sheet Shape","_SheetSize","_EdgeBreakupStrength","_EdgeBreakupScale","_SheetWobbleStrength","_SheetWobbleSpeed");
                EditorGUILayout.HelpBox("Use a subdivided local XY sheet with UV V increasing upwards. Sheet Size describes its local width and height. Positive flow speed moves marks downwards. Preserve generous mesh bounds for vertex motion.",MessageType.Info);
            }
            else if(splash)
            {
                Group(editor,properties,"Colors","_SplashColorA","_SplashColorB","_LayerBlendStrength","_InternalPatternStrength");
                Group(editor,properties,"Motion","_SplashMotionRole","_PulseSpeed","_PulseAmplitude","_WobbleStrength","_WobbleSpeed","_DistortionScale");
                Group(editor,properties,"Ink","_InkLineColor","_SplashInkWidth","_SplashInkStrength");
                EditorGUILayout.HelpBox("Use asymmetric closed impact shapes: rising accents behind a low, wide main mass and a shallow horizontal foam fan. Match Motion Role to each mesh and vary Animation Phase. The existing Moebius renderer supplies outlines.",MessageType.Info);
            }
            else
            {
                Group(editor,properties,"Legacy Mesh Fill","_SurfaceColor");
                DrawImpact(editor,properties);
                EditorGUILayout.HelpBox("The sample uses impact directly in Moebius Water. This legacy cutout mesh material is retained for existing custom setups.",MessageType.Info);
            }
            if(body||splash)Group(editor,properties,"Receive Shadows","_ReceiveWaterShadows","_WaterShadowColor","_WaterShadowStrength","_WaterShadowDensity");
            Group(editor,properties,"Advanced","_AnimationPhase","_DebugView");
            editor.EnableInstancingField();
            EditorGUILayout.Space();
            bool preview=EditorGUILayout.Toggle("Animate Edit Mode Preview",MoebiusWaterPreview.Enabled);
            if(preview!=MoebiusWaterPreview.Enabled)MoebiusWaterPreview.Enabled=preview;
            if(GUILayout.Button("Create Waterfall in Scene"))MoebiusWaterfallSetup.CreateInScene();
        }
        public static void DrawImpact(MaterialEditor editor,MaterialProperty[] properties)
        {
            Group(editor,properties,"Waterfall Impact","_EnableWaterfallImpact");
            var toggle=FindProperty("_EnableWaterfallImpact",properties);
            if(toggle.floatValue<.5f&&!toggle.hasMixedValue)return;
            Group(editor,properties,"Impact Turbulence","_ImpactRadius","_ImpactTurbulence","_ImpactNoiseScale","_ImpactFoamDensity","_ImpactFoamSpeed","_ImpactFoamColor","_ImpactFoamInkStrength");
            Group(editor,properties,"Ripple Propagation","_ImpactRippleSpeed","_ImpactSpawnInterval","_ImpactLifetime","_ImpactMaxDistance","_ImpactRippleCount");
            Group(editor,properties,"Ripple Shape","_ImpactRippleWidth","_ImpactDistortion","_ImpactRippleNoiseScale","_ImpactArcBreakup","_ImpactArcVariation");
            Group(editor,properties,"Ripple Appearance","_ImpactRippleColor","_ImpactRippleIntensity","_ImpactDistanceFade","_ImpactLifetimeFade");
            Group(editor,properties,"Impact Placement","_ImpactPointA","_ImpactPointB","_ImpactFlowDirection","_ImpactPhase");
            EditorGUILayout.HelpBox("Bind once to track the falling sheet in Edit and Play Mode. The receiving water's Impact Binding component controls the live endpoints; material endpoints are the saved fallback. Lifetime is limited to Count × Spawn Interval.",MessageType.Info);
            if(GUILayout.Button("Bind Waterfall Impact"))MoebiusWaterfallImpactSetup.Open();
        }
        static void Group(MaterialEditor editor,MaterialProperty[] properties,string title,params string[] names)
        {
            EditorGUILayout.Space(8);EditorGUILayout.LabelField(title,EditorStyles.boldLabel);
            foreach(string name in names){var property=FindProperty(name,properties);editor.ShaderProperty(property,property.displayName);}
        }
    }
}
