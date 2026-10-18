using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public sealed class DarwinAudioCategory : MonoBehaviour
{
    public enum Kind { Music, Effects }
    [SerializeField] private Kind kind;
    private AudioSource source;
    private float baseVolume;
    private void Awake(){source=GetComponent<AudioSource>();baseVolume=source.volume;}
    public void Apply(float music,float effects)
    {
        if(source==null){source=GetComponent<AudioSource>();baseVolume=source.volume;}
        source.volume=baseVolume*(kind==Kind.Music?music:effects);
    }
}
