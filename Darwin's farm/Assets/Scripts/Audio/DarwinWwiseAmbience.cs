using UnityEngine;

// One ambience voice follows the tile below the centre of the view.
[DefaultExecutionOrder(100)]
public sealed class DarwinWwiseAmbience : MonoBehaviour
{
    private const string BankName = "SB_Ambience";
    private const string GainParameter = "Tile_Gain";
    private const float MaximumGain = 100f;

    [SerializeField] private Camera viewCamera;
    [SerializeField] private MapGridManager grid;
    [SerializeField] private AkAudioListener groundListener;
    [SerializeField, Min(0.01f)] private float candidateTime = 0.2f;
    [SerializeField, Min(0.01f)] private float fadeTime = 0.4f;
    [SerializeField, Min(0f)] private float switchCooldown = 0.15f;
    [SerializeField, Min(0f)] private float fullVolumeRadius = 4.5f;
    [SerializeField, Min(0.1f)] private float silentRadius = 7.5f;
    [SerializeField, Min(0.1f)] private float orthographicNearSize = 10f;
    [SerializeField, Min(0.1f)] private float orthographicSilentSize = 70f;
    [SerializeField, Range(1f, 179f)] private float perspectiveNearFov = 25f;
    [SerializeField, Range(1f, 179f)] private float perspectiveSilentFov = 75f;
    [SerializeField, Range(0f, 100f)] private float enterGainThreshold = 8f;
    [SerializeField, Range(0f, 100f)] private float exitGainThreshold = 3f;
    [SerializeField, Min(1f)] private float maxGainFallPerSecond = 120f;

    private GameObject emitter;
    private bool bankLoaded;
    private bool warnedBank;
    private string activeEvent;
    private string candidateEvent;
    private float candidateSince;
    private float cooldownUntil;
    private float gain;
    private float lastOutputVolume = -1f;
    private Vector3 lastListenerPosition;
    private bool hasListenerPosition;

    public static float MasterVolume { get; private set; } = 1f;
    public static float EffectsVolume { get; private set; } = 1f;

    public static void SetMasterVolume(float volume)
    {
        MasterVolume = Mathf.Clamp01(volume);
    }

    public static void SetEffectsVolume(float volume)
    {
        EffectsVolume = Mathf.Clamp01(volume);
    }

    private void Awake()
    {
        if (viewCamera == null) viewCamera = GetComponent<Camera>();
        if (grid == null) grid = FindFirstObjectByType<MapGridManager>();
        if (groundListener == null)
        {
            var listener = new GameObject("Wwise Ground Listener");
            listener.transform.SetParent(transform, false);
            groundListener = listener.AddComponent<AkAudioListener>();
        }
        emitter = new GameObject("Wwise Tile Ambience");
        emitter.transform.SetParent(transform, false);
        emitter.AddComponent<AkGameObj>();
        groundListener.isDefaultListener = true;
    }

    private void LateUpdate()
    {
        if (!AkUnitySoundEngine.IsInitialized() || viewCamera == null || grid == null)
            return;

        if (!bankLoaded)
        {
            uint bankId;
            AKRESULT result = AkUnitySoundEngine.LoadBank(BankName, out bankId);
            bankLoaded = result == AKRESULT.AK_Success;
            if (!bankLoaded && !warnedBank)
            {
                Debug.LogError("Wwise: cannot load " + BankName + " (" + result + "). Check generated SoundBanks.", this);
                warnedBank = true;
            }
            if (!bankLoaded) return;
            Debug.Log("Wwise ambience bank loaded: " + BankName, this);
        }

        if (!Mathf.Approximately(lastOutputVolume, MasterVolume))
        {
            AkUnitySoundEngine.SetOutputVolume(0, MasterVolume);
            lastOutputVolume = MasterVolume;
        }

        bool hasGround = TryGetGround(out Vector3 point, out MapTileInstance tile);
        if (hasGround)
        {
            groundListener.transform.SetPositionAndRotation(point, viewCamera.transform.rotation);
            lastListenerPosition = point;
            hasListenerPosition = true;
            emitter.transform.position = point;
        }
        else if (hasListenerPosition)
        {
            // Keep a valid listener pose while the view is outside the map.
            groundListener.transform.SetPositionAndRotation(lastListenerPosition, viewCamera.transform.rotation);
        }

        float spatialGain = hasGround ? CalculateGain(point, tile) : 0f;
        string surfaceEvent = hasGround ? EventFor(tile) : null;
        // A lower exit threshold retains the current voice at the edge of its range.
        string wanted = spatialGain >= (surfaceEvent == activeEvent
            ? exitGainThreshold : enterGainThreshold) ? surfaceEvent : null;
        if (wanted != candidateEvent)
        {
            candidateEvent = wanted;
            candidateSince = Time.unscaledTime;
        }
        if (candidateEvent != activeEvent &&
            Time.unscaledTime - candidateSince >= candidateTime &&
            Time.unscaledTime >= cooldownUntil)
        {
            if (activeEvent != null)
            {
                // Fade this voice to zero before replacing it on the next frame.
                gain = Mathf.MoveTowards(gain, 0f,
                    maxGainFallPerSecond * Time.unscaledDeltaTime);
                AkUnitySoundEngine.SetRTPCValue(GainParameter, gain, emitter);
                if (gain > 0.01f) return;
                AkUnitySoundEngine.PostEvent(StopEventFor(activeEvent), emitter);
                activeEvent = null;
                cooldownUntil = Time.unscaledTime + switchCooldown;
                return;
            }
            if (candidateEvent != null)
            {
                AkUnitySoundEngine.SetRTPCValue(GainParameter, 0f, emitter);
                uint playingId = AkUnitySoundEngine.PostEvent(candidateEvent, emitter);
                if (playingId != AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
                {
                    activeEvent = candidateEvent;
                    Debug.Log("Wwise ambience started: " + activeEvent, this);
                }
                cooldownUntil = Time.unscaledTime + switchCooldown;
            }
        }

        float target = activeEvent == wanted ? spatialGain : 0f;
        float rate = target < gain ? maxGainFallPerSecond : MaximumGain / fadeTime;
        gain = Mathf.MoveTowards(gain, target, rate * Time.unscaledDeltaTime);
        if (activeEvent != null)
            AkUnitySoundEngine.SetRTPCValue(GainParameter, gain, emitter);
    }

    private bool TryGetGround(out Vector3 point, out MapTileInstance tile)
    {
        point = default;
        tile = null;
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) return false;
        Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, 1 << groundLayer,
                QueryTriggerInteraction.Ignore)) return false;
        tile = hit.collider.GetComponentInParent<MapTileInstance>();
        if (tile == null || !tile.isActiveAndEnabled) return false;
        point = hit.point;
        return true;
    }

    private static string EventFor(MapTileInstance tile)
    {
        if (tile == null) return null;
        if (tile.Block != null && tile.Block.waterCoverage != WaterCoverage.Land)
            return "Play___amb_water";
        switch (tile.Biome)
        {
            case BiomeType.Forest:
            case BiomeType.Rainforest: return "Play_Amb_Forest";
            case BiomeType.Sand:
            case BiomeType.Desert: return "Play_Amb_Desert";
            case BiomeType.Rock:
            case BiomeType.Highland: return "Play_Amb_Mountain";
            case BiomeType.SnowMountain: return "Play_Amb_SnowMountain";
            case BiomeType.Tundra: return "Play_Amb_Tundra";
            case BiomeType.Volcano: return "Play_Amb_Volcano";
            default: return "Play_Amb_Grassland";
        }
    }

    private static string StopEventFor(string playEvent)
    {
        if (playEvent == "Play___amb_water") return "Stop_Amb_Water";
        return playEvent.Replace("Play_", "Stop_");
    }

    private float CalculateGain(Vector3 point, MapTileInstance tile)
    {
        Vector3 offset = point - tile.transform.position;
        float distance = new Vector2(offset.x, offset.z).magnitude;
        float distanceWeight = 1f - Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(fullVolumeRadius, Mathf.Max(fullVolumeRadius + 0.01f, silentRadius), distance));
        float zoom = viewCamera.orthographic
            ? Mathf.InverseLerp(orthographicSilentSize, orthographicNearSize, viewCamera.orthographicSize)
            : Mathf.InverseLerp(perspectiveSilentFov, perspectiveNearFov, viewCamera.fieldOfView);
        return MaximumGain * EffectsVolume * distanceWeight * zoom;
    }

    private void OnDestroy()
    {
        if (!AkUnitySoundEngine.IsInitialized()) return;
        if (emitter != null) AkUnitySoundEngine.StopAll(emitter);
        if (bankLoaded) AkUnitySoundEngine.UnloadBank(BankName, System.IntPtr.Zero);
    }
}
