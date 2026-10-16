using UnityEngine;

namespace Darwin.Rendering
{
    /// <summary>Shared, continuous wind for native particle systems. No per-particle CPU updates.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Darwin/Moebius Wind Controller")]
    public sealed class MoebiusWindController : MonoBehaviour
    {
        [Header("Shared Wind")]
        [Tooltip("World-space XZ direction. A zero vector safely falls back to +X.")]
        public Vector2 windDirection = new Vector2(1, 0.3f);
        [Min(0)] public float baseWindSpeed = 0.85f;
        [Range(0, 3)] public float gustStrength = 0.85f;
        [Range(0.01f, 1)] public float gustFrequency = 0.11f;
        [Range(0, 1)] public float turbulenceStrength = 0.12f;
        [Min(1)] public int randomSeed = 2718;

        [Header("Visibility And Density")]
        [Range(0, 3)] public float globalVFXIntensity = 1;
        [Range(0, 3)] public float dustIntensity = 1;
        [Range(0, 3)] public float debrisIntensity = 1;
        [Range(0, 3)] public float windStreakIntensity = 1;
        [Min(0)] public float ambientDustRate = 3.2f;
        [Min(0)] public float gustDustRate = 2.2f;
        [Min(0)] public float debrisRate = 0.85f;
        [Min(0)] public float windStreakRate = 0.42f;

        [Header("Palette And Size (Metres)")]
        public Color dustColor = new Color(0.79f, 0.78f, 0.53f, 1);
        public Color debrisColor = new Color(0.56f, 0.48f, 0.24f, 1);
        public Color windLineColor = new Color(0.98f, 0.99f, 0.85f, 1);
        public Vector2 dustSizeRange = new Vector2(0.95f, 1.85f);
        public Vector2 debrisSizeRange = new Vector2(0.22f, 0.38f);
        public Vector2 windLengthRange = new Vector2(2.5f, 4.1f);

        [Header("Assigned Particle Systems")]
        public ParticleSystem ambientDust;
        public ParticleSystem gustDust;
        public ParticleSystem floatingDebris;
        public ParticleSystem windStreaks;

        [Header("Ground And Bounds")]
        [Tooltip("Local footprint of this VFX rig, fitted to the ground when authored.")]
        public Bounds localBounds = new Bounds(Vector3.zero, new Vector3(11.5f, 8, 11.5f));
        [Min(0.01f)] public float boundaryFade = 0.75f;
        [Tooltip("Preview native particles without entering Play Mode. Disable to pause the preview.")]
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
            CacheSystems();
            settingsDirty = true;
            #if UNITY_EDITOR
            lastEditorTime = 0;
            needsWarmup = true;
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
            gustFrequency = Mathf.Clamp(gustFrequency, 0.01f, 1);
            boundaryFade = Mathf.Max(0.01f, boundaryFade);
            randomSeed = Mathf.Max(1, randomSeed);
            ClampRange(ref dustSizeRange);
            ClampRange(ref debrisSizeRange);
            ClampRange(ref windLengthRange);
            settingsDirty = true;
        }
        static void ClampRange(ref Vector2 range)
        {
            range.x = Mathf.Max(0.01f, range.x);
            range.y = Mathf.Max(range.x, range.y);
        }
        void CacheSystems()
        {
            appliedSeed = 0;
            systems = new[] { ambientDust, gustDust, floatingDebris, windStreaks };
            renderers = new ParticleSystemRenderer[4];
            blocks = new MaterialPropertyBlock[4];
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i]) renderers[i] = systems[i].GetComponent<ParticleSystemRenderer>();
                blocks[i] = new MaterialPropertyBlock();
            }
        }
        void Update()
        {
            if (Application.IsPlaying(gameObject)) UpdateWind(Time.time);
        }
        [ContextMenu("Refresh Wind Settings")]
        public void RefreshSettings()
        {
            CacheSystems();
            settingsDirty = true;
            UpdateWind(Application.IsPlaying(gameObject) ? Time.time : 0);
        }
        void ApplySettings()
        {
            if (systems[0] != ambientDust || systems[1] != gustDust || systems[2] != floatingDebris || systems[3] != windStreaks)
                CacheSystems();
            if (appliedSeed != randomSeed)
            {
                for (int i = 0; i < systems.Length; i++)
                {
                    if (!systems[i]) continue;
                    bool playing = systems[i].isPlaying;
                    systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    systems[i].useAutoRandomSeed = false;
                    systems[i].randomSeed = unchecked((uint)randomSeed + (uint)(i * 137 + 1));
                    if (playing && Application.IsPlaying(gameObject)) systems[i].Play(false);
                }
                appliedSeed = randomSeed;
            }
            for (int i=0; i<systems.Length; i++)
            {
                if (!systems[i]) continue;
                var main = systems[i].main;
                if (i < 2)
                {
                    main.startSizeX = new ParticleSystem.MinMaxCurve(dustSizeRange.x, dustSizeRange.y);
                    main.startSizeZ = new ParticleSystem.MinMaxCurve(dustSizeRange.x * 0.4f, dustSizeRange.y * 0.42f);
                }
                else if (i==2) main.startSize = new ParticleSystem.MinMaxCurve(debrisSizeRange.x,debrisSizeRange.y);
                else main.startSizeX = new ParticleSystem.MinMaxCurve(windLengthRange.x,windLengthRange.y);
            }
            settingsDirty = false;
        }
        /// <summary>Also used by deterministic validation; particle positions remain owned by Unity.</summary>
        public void UpdateWind(float time)
        {
            if (systems == null) CacheSystems();
            if (settingsDirty) ApplySettings();
            Vector2 direction = windDirection.sqrMagnitude > 0.0001f ? windDirection.normalized : Vector2.right;
            float seed = randomSeed * 0.0137f;
            float noise = Mathf.PerlinNoise(time * gustFrequency + seed, seed + 17.3f);
            CurrentGust = Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.28f,0.73f,noise));
            float sway = (Mathf.PerlinNoise(seed + 31,time * 0.045f)-0.5f) * turbulenceStrength * 0.25f;
            direction = (direction + new Vector2(-direction.y,direction.x) * sway).normalized;
            CurrentWind = new Vector3(direction.x,0,direction.y) * Mathf.Max(0,baseWindSpeed) * (1+gustStrength*CurrentGust);
            float heading = -Mathf.Atan2(direction.y,direction.x);
            // The authored rig is axis-aligned; all eight corners keep bounds valid after rotation/scaling too.
            Bounds worldBounds = new Bounds(transform.TransformPoint(localBounds.center), Vector3.zero);
            for (int c=0; c<8; c++)
                worldBounds.Encapsulate(transform.TransformPoint(localBounds.center + Vector3.Scale(localBounds.extents,
                    new Vector3((c&1)==0?-1:1,(c&2)==0?-1:1,(c&4)==0?-1:1))));
            Vector3 min = worldBounds.min, max = worldBounds.max;
            for (int i = 0; i < systems.Length; i++)
            {
                var system = systems[i]; if (!system) continue;
                float multiplier = i==1?1.18f:i==2?0.8f:i==3?1.12f:0.72f;
                var velocity = system.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.World;
                velocity.x = SpeedRange(CurrentWind.x*multiplier);
                velocity.z = SpeedRange(CurrentWind.z*multiplier);
                var emission = system.emission;
                float effectIntensity = i<2?dustIntensity:i==2?debrisIntensity:windStreakIntensity;
                float rate = i==0?ambientDustRate:i==1?gustDustRate*Mathf.SmoothStep(0,1,CurrentGust):i==2?debrisRate:windStreakRate;
                emission.rateOverTimeMultiplier = Mathf.Max(0,rate*effectIntensity*globalVFXIntensity);
                var noiseModule = system.noise;
                noiseModule.strengthMultiplier = turbulenceStrength * (i<2?0.15f:i==2?0.7f:0.08f);
                if (i!=2)
                {
                    var main=system.main;
                    main.startRotationY = new ParticleSystem.MinMaxCurve(heading-0.06f,heading+0.06f);
                }
                if (renderers[i])
                {
                    Color color = i<2?dustColor:i==2?debrisColor:windLineColor;
                    color.a *= Mathf.Clamp01(globalVFXIntensity) * Mathf.Clamp01(effectIntensity);
                    blocks[i].SetColor(TintID,color);
                    blocks[i].SetVector(BoundsID,new Vector4(min.x,min.z,max.x,max.z));
                    blocks[i].SetFloat(BoundaryID,boundaryFade);
                    renderers[i].SetPropertyBlock(blocks[i]);
                }
            }
        }
        static ParticleSystem.MinMaxCurve SpeedRange(float speed)
        {
            float a=speed*0.82f,b=speed*1.12f;
            return new ParticleSystem.MinMaxCurve(Mathf.Min(a,b),Mathf.Max(a,b));
        }
        #if UNITY_EDITOR
        double lastEditorTime;
        bool needsWarmup;
        void PreviewUpdate()
        {
            if (Application.isBatchMode || !this || !isActiveAndEnabled || !gameObject.scene.IsValid() ||
                UnityEditor.EditorUtility.IsPersistent(this) || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode ||
                UnityEditor.EditorApplication.isCompiling || !animateEditModePreview)
            { lastEditorTime = 0; return; }
            double now=UnityEditor.EditorApplication.timeSinceStartup;
            if (lastEditorTime==0) lastEditorTime=now;
            double delta=now-lastEditorTime;
            if(delta<1.0/60.0) return;
            lastEditorTime=now;
            UpdateWind((float)now);
            float step=needsWarmup?4:Mathf.Min((float)delta,0.1f);
            for(int i=0;i<systems.Length;i++) if(systems[i]) systems[i].Simulate(step,false,needsWarmup,false);
            needsWarmup=false;
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
        #endif
    }
}
