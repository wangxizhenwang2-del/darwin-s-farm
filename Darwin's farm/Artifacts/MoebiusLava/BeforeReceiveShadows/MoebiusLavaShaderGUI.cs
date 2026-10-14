using UnityEditor;
using UnityEngine;

namespace Darwin.Rendering.Editor
{
    public enum MoebiusLavaDebugView { Composite,Crust,Molten,Heat,Flow,Hatching,Stipple,Contour }
    /// <summary>Artist controls for the lava material; no scene generation or runtime updates.</summary>
    public sealed class MoebiusLavaShaderGUI : ShaderGUI
    {
        public override void OnGUI(MaterialEditor editor,MaterialProperty[] properties)
        {
            Group(editor,properties,"Crust","_CrustColor","_SecondaryCrustColor","_CrustCoverage","_CrustScale","_CrustDistortion","_CrackWidth","_CrackSoftness");
            Group(editor,properties,"Lava Flow","_FlowDirection","_LavaFlowSpeed","_FlowDistortion","_FlowDetailScale","_CrustDriftSpeed","_CrustDeformationStrength");
            Group(editor,properties,"Color and Heat","_CoolLavaColor","_WarmLavaColor","_HotLavaColor","_HeatContrast","_ColorBanding","_HotRegionThreshold","_EnableEmission","_EmissionIntensity");
            Group(editor,properties,"Moebius Styling","_InkColor","_ContourWidth","_ContourStrength","_UseHatching","_HatchStrength","_HatchScale","_HatchRotation","_HatchLineWidth","_HatchIrregularity","_HatchCrossStrength","_StippleStrength","_StippleScale","_NPRLightingInfluence");
            Group(editor,properties,"Artist Masks","_UseControlMap","_ControlMap","_UseVertexColor");
            Group(editor,properties,"Advanced","_AnimationPhase","_DebugView");
            EditorGUILayout.HelpBox("Opaque lava uses world XZ coordinates: island size, crack width and flow speed are in world metres. Intended for pools, rivers and gently sloped lava surfaces. Steep vertical faces stretch the projection. Control Map uses raw UV0; white preserves the effect. Bloom is optional. Geometry is unchanged; island separation is a shading effect.",MessageType.Info);
            bool preview=EditorGUILayout.Toggle("Animate Edit Mode Preview",MoebiusWaterPreview.Enabled);
            if(preview!=MoebiusWaterPreview.Enabled)MoebiusWaterPreview.Enabled=preview;
            editor.EnableInstancingField();
        }
        static void Group(MaterialEditor editor,MaterialProperty[] properties,string title,params string[] names)
        {
            EditorGUILayout.Space(8);EditorGUILayout.LabelField(title,EditorStyles.boldLabel);
            foreach(string name in names)
            {
                var property=FindProperty(name,properties);
                if(property.propertyType==UnityEngine.Rendering.ShaderPropertyType.Texture)
                    editor.TexturePropertySingleLine(new GUIContent(property.displayName),property);
                else editor.ShaderProperty(property,property.displayName);
            }
        }
    }
}
