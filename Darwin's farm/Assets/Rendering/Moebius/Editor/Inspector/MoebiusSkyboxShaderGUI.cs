using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Darwin.Rendering.Editor
{
    public sealed class MoebiusSkyboxShaderGUI : ShaderGUI
    {
        MaterialEditor editor;
        MaterialProperty[] properties;

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] materialProperties)
        {
            editor=materialEditor; properties=materialProperties;
            Group("Sky Colors", "_ZenithColor", "_MidColor", "_LowerColor", "_HorizonColor", "_BelowColor");
            Group("Horizon and Transitions", "_HorizonHeight", "_BelowFade", "_LowerTransition", "_MidTransition", "_ZenithTransition", "_TransitionSoftness");
            Group("Cloud Composition", "_EnableClouds", "_CloudDensity", "_CloudScale", "_CloudShapeVariation", "_CloudCoverage", "_CloudRotation");
            Vector2Property("_CloudElevationRange", "Elevation Range (Min / Max Degrees)");
            Group("Cloud Appearance", "_CloudColor", "_CloudShadeColor", "_CloudShadeStrength", "_CloudEdgeSoftness", "_CloudInkColor", "_CloudInkStrength", "_CloudInkWidth");
            Group("Sun", "_EnableSun", "_UseMainLightSun", "_SunDirection", "_SunColor", "_SunRadius", "_SunSoftness", "_SunBrightness", "_SunInkColor", "_SunInkStrength", "_SunInkWidth", "_SunHazeStrength", "_SunHazeHeight", "_SunHazeWidth", "_HaloColor", "_HaloRadius", "_HaloStrength", "_SunHorizonInfluence");
            Group("Illustration Style", "_Exposure", "_Saturation", "_GradientSteps", "_PigmentStrength", "_PigmentScale", "_PaperStrength");
            Group("Animation", "_CloudSpeed", "_AnimationPhase");
            Vector2Property("_CloudWindDirection", "Wind Direction (Azimuth / Elevation)");
            EditorGUILayout.HelpBox("Cloud movement uses continuous GPU time. Enable Scene View > Effects > Always Refresh when you want continuous animation while editing.",MessageType.Info);
            Group("Environment Integration", "_FogColorMatch");
            EditorGUILayout.HelpBox("Fog Color Match reads Unity's scene fog color; it does not enable fog or change object shaders. Exposure and saturation affect the final sky color. Ambient lighting and reflection probes are configured separately in Window > Rendering > Lighting.",MessageType.Info);
            EditorGUILayout.Space(8);
            if(GUILayout.Button("Reset Clear Sky Defaults"))
            {
                Undo.RecordObjects(editor.targets,"Reset Moebius Clear Sky");
                foreach(var target in editor.targets) MoebiusSkyboxPresets.ApplyClearSky((Material)target);
            }
            using(new EditorGUI.DisabledScope(editor.targets.Length!=1 || Application.isPlaying || !SceneManager.GetActiveScene().IsValid()))
            {
                if(GUILayout.Button("Apply to Active Scene and Main Camera"))
                {
                    Undo.RecordObject(Unsupported.GetSerializedAssetInterfaceSingleton("RenderSettings"),"Assign Moebius Skybox");
                    RenderSettings.skybox=(Material)editor.target;
                    var camera=Camera.main;
                    if(camera)
                    {
                        Undo.RecordObject(camera,"Enable Camera Skybox"); camera.clearFlags=CameraClearFlags.Skybox;
                        EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
                    }
                    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                    SceneView.RepaintAll();
                }
            }
            EditorGUILayout.HelpBox("Applying the sky changes only the scene skybox and Main Camera background mode. Existing light, ambient, fog and reflection settings are preserved. Rebuild environment lighting / reflection probes explicitly if you want them to use the new sky.",MessageType.Info);
        }

        void Group(string label,params string[] names)
        {
            EditorGUILayout.Space(8); EditorGUILayout.LabelField(label,EditorStyles.boldLabel);
            foreach(string name in names) { var p=FindProperty(name,properties); editor.ShaderProperty(p,p.displayName); }
        }
        void Vector2Property(string name,string label)
        {
            var p=FindProperty(name,properties);
            EditorGUI.showMixedValue=p.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            Vector2 value=EditorGUILayout.Vector2Field(label,new Vector2(p.vectorValue.x,p.vectorValue.y));
            if(EditorGUI.EndChangeCheck()) p.vectorValue=new Vector4(value.x,value.y,0,0);
            EditorGUI.showMixedValue=false;
        }
    }
}
