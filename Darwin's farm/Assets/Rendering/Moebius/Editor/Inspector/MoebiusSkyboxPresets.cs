using UnityEditor;
using UnityEngine;

namespace Darwin.Rendering.Editor
{
    /// <summary>The single clear-day sky material and its creation workflow.</summary>
    public static class MoebiusSkyboxPresets
    {
        public const string ShaderName = "Darwin/Moebius Skybox";

        [MenuItem("Assets/Create/Rendering/Moebius Clear Sky Material", priority = 220)]
        static void CreateMaterial()
        {
            var shader = Shader.Find(ShaderName);
            if (!shader) { Debug.LogError("Moebius Skybox shader is not available."); return; }
            var material = new Material(shader) { name = "Moebius Clear Sky" };
            ApplyClearSky(material);
            ProjectWindowUtil.CreateAsset(material, "Moebius Clear Sky.mat");
        }

        public static void ApplyClearSky(Material material)
        {
            if (!material || material.shader.name != ShaderName) return;
            var defaults = new Material(material.shader);
            material.CopyPropertiesFromMaterial(defaults);
            Object.DestroyImmediate(defaults);
            EditorUtility.SetDirty(material);
        }
    }
}
