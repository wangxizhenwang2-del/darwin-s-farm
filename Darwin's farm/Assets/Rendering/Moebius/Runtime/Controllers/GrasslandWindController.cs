using UnityEngine;

namespace Darwin.Rendering
{
    /// <summary>Continuous shared breeze; Unity owns particle motion, rotation and lifetime.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Darwin/Grassland Wind Controller")]
    public sealed class GrasslandWindController : MonoBehaviour
    {
        [Header("Shared Grassland Wind")]
        [Tooltip("World-space XZ direction. Zero falls back to +X.")]
        public Vector2 windDirection = new Vector2(1, .3f);
        [Min(0)] public float baseWindSpeed = .38f;
        [Range(0, 3)] public float gustStrength = .5f;
        [Range(.01f, 1)] public float gustFrequency = .08f;
        [Range(0, 1)] public float turbulenceStrength = .22f;
        [Min(1)] public int randomSeed = 7319;

        [Header("Density And Visibility")]
        [Tooltip("Multiplies all emission rates. Below 1 also fades opacity; 0 hides existing particles.")]
        [Range(0, 3)] public float globalVFXIntensity = 1;
        [Range(0, 3)] public float motesIntensity = 1;
        [Range(0, 3)] public float leavesIntensity = 1;
        [Range(0, 3)] public float windWispsIntensity = 1;
        [Min(0)] public float motesRate = 1.7f;
        [Min(0)] public float leavesRate = .28f;
        [Min(0)] public float windWispsRate = .06f;

        [Header("Palette And Size (Metres)")]
        public Color motesColor = new Color(.87f, .89f, .66f, 1);
        public Color leavesColor = new Color(.61f, .68f, .32f, 1);
        public Color windWispsColor = new Color(.78f, .86f, .72f, 1);
        public Vector2 motesSizeRange = new Vector2(.11f, .23f);
        public Vector2 leavesSizeRange = new Vector2(.19f, .34f);
        public Vector2 windLengthRange = new Vector2(1.6f, 2.8f);

        [Header("Assigned Particle Systems")]
        public ParticleSystem floatingMotes;
        public ParticleSystem fallingLeaves;
        public ParticleSystem windWisps;
        [Header("Diorama Bounds")]
        public Bounds localBounds = new Bounds(Vector3.zero, new Vector3(12, 7, 12));
        [Min(.01f)] public float boundaryFade = .7f;
        public bool animateEditModePreview = true;

        public Vector3 CurrentWind { get; private set; }
        public float CurrentGust { get; private set; }
        ParticleSystem[] systems;
        ParticleSystemRenderer[] renderers;
        MaterialPropertyBlock[] blocks;
        bool settingsDirty = true;
        int appliedSeed;
        static readonly int TintID = Shader.PropertyToID("_Tint");
        static readonly int BoundsID = Shader.PropertyToID("_BoundsXZ");
        static readonly int BoundaryID = Shader.PropertyToID("_BoundaryFade");

        void OnEnable()
        {
            Cache();
            settingsDirty = true;
            #if UNITY_EDITOR
            lastEditorTime = 0; needsWarmup = true;
            UnityEditor.EditorApplication.update += PreviewUpdate;
            #endif
        }
        void OnDisable()
        {
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= PreviewUpdate;
            #endif
        }
        void OnValidate()
        {
            baseWindSpeed = Mathf.Max(0, baseWindSpeed);
            gustFrequency = Mathf.Clamp(gustFrequency, .01f, 1);
            boundaryFade = Mathf.Max(.01f, boundaryFade);
            randomSeed = Mathf.Max(1, randomSeed);
            ClampSize(ref motesSizeRange); ClampSize(ref leavesSizeRange); ClampSize(ref windLengthRange);
            settingsDirty = true;
        }
        static void ClampSize(ref Vector2 size)
        { size.x = Mathf.Max(.01f, size.x); size.y = Mathf.Max(size.x, size.y); }
        void Cache()
        {
            systems = new[] { floatingMotes, fallingLeaves, windWisps };
            renderers = new ParticleSystemRenderer[3]; blocks = new MaterialPropertyBlock[3];
            for (int i = 0; i < 3; i++)
            { if (systems[i]) renderers[i] = systems[i].GetComponent<ParticleSystemRenderer>(); blocks[i] = new MaterialPropertyBlock(); }
            appliedSeed = 0;
        }
        [ContextMenu("Refresh Grassland Wind Settings")]
        public void RefreshSettings()
        { Cache(); settingsDirty = true; UpdateWind(Application.IsPlaying(gameObject) ? Time.time : 0); }
        void ApplySettings()
        {
            if (systems[0] != floatingMotes || systems[1] != fallingLeaves || systems[2] != windWisps) Cache();
            for (int i = 0; i < 3; i++)
            {
                var ps = systems[i]; if (!ps) continue;
                if (appliedSeed != randomSeed)
                {
                    bool playing = ps.isPlaying;
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.useAutoRandomSeed = false; ps.randomSeed = unchecked((uint)randomSeed + (uint)(i * 137 + 1));
                    if (playing && Application.IsPlaying(gameObject)) ps.Play(false);
                }
                var main = ps.main;
                if (i == 2) main.startSizeX = new ParticleSystem.MinMaxCurve(windLengthRange.x, windLengthRange.y);
                else
                {
                    Vector2 range = i == 0 ? motesSizeRange : leavesSizeRange;
                    main.startSize = new ParticleSystem.MinMaxCurve(range.x, range.y);
                }
            }
            appliedSeed = randomSeed; settingsDirty = false;
        }
        void Update() { if (Application.IsPlaying(gameObject)) UpdateWind(Time.time); }
        /// <summary>Deterministic continuous wind; no per-particle CPU loop or allocations.</summary>
        public void UpdateWind(float time)
        {
            if (systems == null) Cache();
            if (settingsDirty) ApplySettings();
            float seed = randomSeed * .0137f;
            CurrentGust = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.28f, .73f,
                Mathf.PerlinNoise(seed + time * gustFrequency, seed + 17.3f)));
            Vector2 direction = windDirection.sqrMagnitude > .0001f ? windDirection.normalized : Vector2.right;
            float sway = (Mathf.PerlinNoise(seed + 31, time * .045f) - .5f) * turbulenceStrength;
            direction = (direction + new Vector2(-direction.y, direction.x) * sway).normalized;
            CurrentWind = new Vector3(direction.x, 0, direction.y) * Mathf.Max(0, baseWindSpeed) * (1 + gustStrength * CurrentGust);
            Bounds bounds = new Bounds(transform.TransformPoint(localBounds.center), Vector3.zero);
            for (int c = 0; c < 8; c++) bounds.Encapsulate(transform.TransformPoint(localBounds.center +
                Vector3.Scale(localBounds.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
            for (int i = 0; i < 3; i++)
            {
                var ps = systems[i]; if (!ps) continue;
                float influence = i == 0 ? .28f : i == 1 ? .65f : .8f;
                var velocity = ps.velocityOverLifetime;
                velocity.x = SpeedRange(CurrentWind.x * influence); velocity.z = SpeedRange(CurrentWind.z * influence);
                var noise = ps.noise; noise.strengthMultiplier = turbulenceStrength * (i == 2 ? .07f : i == 1 ? .45f : .3f);
                float intensity = i == 0 ? motesIntensity : i == 1 ? leavesIntensity : windWispsIntensity;
                float rate = i == 0 ? motesRate : i == 1 ? leavesRate : windWispsRate;
                var emission = ps.emission;
                emission.rateOverTimeMultiplier = Mathf.Max(0, rate * intensity * globalVFXIntensity * (i == 2 ? .65f + .35f * CurrentGust : 1));
                if (i == 2)
                { var main = ps.main; float angle = -Mathf.Atan2(direction.y, direction.x); main.startRotationY = new ParticleSystem.MinMaxCurve(angle - .08f, angle + .08f); }
                if (!renderers[i]) continue;
                Color tint = i == 0 ? motesColor : i == 1 ? leavesColor : windWispsColor;
                tint.a *= Mathf.Clamp01(globalVFXIntensity) * Mathf.Clamp01(intensity);
                blocks[i].SetColor(TintID, tint);
                blocks[i].SetVector(BoundsID, new Vector4(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z));
                blocks[i].SetFloat(BoundaryID, boundaryFade); renderers[i].SetPropertyBlock(blocks[i]);
            }
        }
        static ParticleSystem.MinMaxCurve SpeedRange(float speed)
        { float a = speed * .8f, b = speed * 1.12f; return new ParticleSystem.MinMaxCurve(Mathf.Min(a, b), Mathf.Max(a, b)); }
        #if UNITY_EDITOR
        double lastEditorTime;
        bool needsWarmup;
        void PreviewUpdate()
        {
            if (Application.isBatchMode || !this || !isActiveAndEnabled || !gameObject.scene.IsValid() ||
                UnityEditor.EditorUtility.IsPersistent(this) || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode ||
                UnityEditor.EditorApplication.isCompiling || !animateEditModePreview)
            { lastEditorTime = 0; return; }
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            if (lastEditorTime == 0) lastEditorTime = now;
            double delta = now - lastEditorTime; if (delta < 1.0 / 60) return;
            lastEditorTime = now; UpdateWind((float)now);
            for (int i = 0; i < 3; i++) if (systems[i]) systems[i].Simulate(needsWarmup ? 6 : Mathf.Min((float)delta, .1f), false, needsWarmup, false);
            needsWarmup = false;
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
        #endif
    }
}
