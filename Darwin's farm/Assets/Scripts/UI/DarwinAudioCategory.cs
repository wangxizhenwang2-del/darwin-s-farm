using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public sealed class DarwinAudioCategory : MonoBehaviour
{
    public enum Kind { Music, Effects }
    [SerializeField] private Kind kind;
    private static float musicVolume = 1f;
    private static float effectsVolume = 1f;
    private AudioSource source;
    private float baseVolume;
    private void Awake()
    {
        source = GetComponent<AudioSource>();
        baseVolume = source.volume;
        ApplyCurrent();
    }
    private void OnEnable() => ApplyCurrent();

    public static void SetCategoryVolumes(float music, float effects)
    {
        musicVolume = Mathf.Clamp01(music);
        effectsVolume = Mathf.Clamp01(effects);
        foreach (var category in FindObjectsByType<DarwinAudioCategory>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            category.ApplyCurrent();
    }

    private void ApplyCurrent()
    {
        if (source == null)
        {
            source = GetComponent<AudioSource>();
            baseVolume = source.volume;
        }
        source.volume = baseVolume * (kind == Kind.Music ? musicVolume : effectsVolume);
    }
}
