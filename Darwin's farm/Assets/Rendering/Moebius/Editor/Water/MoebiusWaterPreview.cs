using System;
using UnityEditor;
using UnityEngine;

namespace Darwin.Rendering.Editor
{
    /// <summary>Continuously redraw loaded water scenes in Edit Mode, independent of tool windows.</summary>
    [InitializeOnLoad]
    public static class MoebiusWaterPreview
    {
        const string Preference = "Darwin.Moebius.Water.AnimatePreview";
        static double nextScan, nextFrame;
        static bool hasWater;
        public static bool Enabled
        {
            get => SessionState.GetBool(Preference, true);
            set { SessionState.SetBool(Preference, value); nextScan = nextFrame = 0; }
        }

        static MoebiusWaterPreview() { EditorApplication.update += Update; }
        static void Update()
        {
            if (Application.isBatchMode || !Enabled || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            double now = EditorApplication.timeSinceStartup;
            if (now >= nextScan)
            {
                nextScan = now + 1;
                hasWater = false;
                foreach (var renderer in Resources.FindObjectsOfTypeAll<MeshRenderer>())
                {
                    if (EditorUtility.IsPersistent(renderer) || !renderer.enabled || !renderer.gameObject.activeInHierarchy || !renderer.gameObject.scene.isLoaded) continue;
                    foreach (var material in renderer.sharedMaterials)
                        if (material && material.shader && material.shader.name == "Darwin/Moebius Water") { hasWater = true; break; }
                    if (hasWater) break;
                }
            }
            if (!hasWater || now < nextFrame) return;
            // Advance the deadline rather than resetting it from each late callback:
            // a slightly early update does not repeatedly turn 60 Hz into 30 Hz.
            nextFrame = Math.Max(nextFrame + 1.0 / 60, now);
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }
}
