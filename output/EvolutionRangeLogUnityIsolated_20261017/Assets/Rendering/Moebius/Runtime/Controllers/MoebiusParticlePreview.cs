using UnityEngine;

namespace Darwin.Rendering
{
    /// <summary>Editor preview for native particle rigs. Runtime emission remains owned by Unity.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Darwin/Moebius Particle Preview")]
    public sealed class MoebiusParticlePreview : MonoBehaviour
    {
        [Tooltip("Animate this rig in the Scene view without entering Play Mode.")]
        public bool animateEditModePreview = true;
        [Min(0), Tooltip("Initial simulation time so the effect is visible when the scene opens.")]
        public float previewWarmupSeconds = 2;
        public ParticleSystem[] particleSystems = new ParticleSystem[0];

        #if UNITY_EDITOR
        double lastEditorTime, nextEditorFrame;
        bool needsWarmup = true;

        void OnEnable()
        {
            ResetClock();
            UnityEditor.EditorApplication.update += PreviewUpdate;
            UnityEditor.EditorApplication.playModeStateChanged += PlayModeChanged;
        }
        void OnDisable()
        {
            UnityEditor.EditorApplication.update -= PreviewUpdate;
            UnityEditor.EditorApplication.playModeStateChanged -= PlayModeChanged;
            lastEditorTime = 0;
        }
        void OnValidate()
        {
            previewWarmupSeconds = Mathf.Max(0, previewWarmupSeconds);
            ResetClock();
        }
        void ResetClock()
        {
            lastEditorTime = 0;
            nextEditorFrame = 0;
            needsWarmup = true;
        }
        void PlayModeChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingEditMode)
            {
                // Discard preview time before native Play On Awake starts the real cycle.
                foreach (var system in particleSystems)
                    if (system) system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (state == UnityEditor.PlayModeStateChange.EnteredEditMode) ResetClock();
        }
        void PreviewUpdate()
        {
            if (Application.isBatchMode || !this || !isActiveAndEnabled || !gameObject.scene.isLoaded ||
                UnityEditor.EditorUtility.IsPersistent(this) || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode ||
                UnityEditor.EditorApplication.isCompiling || !animateEditModePreview)
            {
                lastEditorTime = 0;
                return;
            }
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            if (lastEditorTime == 0)
            {
                lastEditorTime = now;
                nextEditorFrame = now + 1.0 / 60;
                return;
            }
            if (now < nextEditorFrame) return;
            double delta = now - lastEditorTime;
            lastEditorTime = now;
            nextEditorFrame = System.Math.Max(nextEditorFrame + 1.0 / 60, now);
            if (needsWarmup) Physics.SyncTransforms();
            foreach (var system in particleSystems)
            {
                if (!system || !system.gameObject.activeInHierarchy) continue;
                if (needsWarmup) system.Simulate(previewWarmupSeconds, false, true, true);
                else system.Simulate(Mathf.Min((float)delta, .1f), false, false, false);
            }
            needsWarmup = false;
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
        #endif
    }
}
