using System.Collections.Generic;
using UnityEngine;

// Added to a population's moving Leader when its species has a Wwise event.
[RequireComponent(typeof(AkGameObj))]
public sealed class DarwinWwiseAnimalEmitter : MonoBehaviour
{
    private static readonly HashSet<DarwinWwiseAnimalEmitter> Playing = new HashSet<DarwinWwiseAnimalEmitter>();
    private const int MaximumVoices = 8;
    private const float StereoSpeakerAngleDegrees = 30f;
    private const float MaximumStereoPan = 0.65f;
    private const float MinimumInterval = 5f;
    private const float MaximumInterval = 10f;

    private string soundBank;
    private string playEvent;
    private bool bankLoaded;
    private bool failedEventLogged;
    private uint playingId;
    private float nextPlayAt;
    private float lastVolume = -1f;
    private AkAudioListener listener;
    private Camera viewCamera;
    private GameObject voiceObject;

    private void Awake()
    {
        voiceObject = new GameObject("Wwise Animal Voice");
        voiceObject.AddComponent<AkGameObj>();
    }

    public void Bind(string bank, string eventName)
    {
        if (soundBank == bank && playEvent == eventName) return;
        StopVoice();
        UnloadBank();
        soundBank = bank;
        playEvent = eventName;
        failedEventLogged = false;
        nextPlayAt = Time.time + Random.Range(0f, MinimumInterval);
    }

    private void Update()
    {
        if (string.IsNullOrWhiteSpace(playEvent) || string.IsNullOrWhiteSpace(soundBank) ||
            !AkUnitySoundEngine.IsInitialized()) return;

        if (listener == null) listener = FindFirstObjectByType<AkAudioListener>();
        if (viewCamera == null) viewCamera = Camera.main;
        if (listener == null || viewCamera == null) return;

        // The authored animal events are spatialized. Place their Wwise object
        // beside the listener so world height and range cannot attenuate them;
        // screen X still supplies a restrained left/right position.
        if (voiceObject.transform.parent != listener.transform)
            voiceObject.transform.SetParent(listener.transform, false);
        Vector3 viewport = viewCamera.WorldToViewportPoint(transform.position);
        // Map the whole screen evenly from L65 through centre to R65.
        // Convert the target pan to an angle at a fixed distance so changing
        // X does not reintroduce world-distance attenuation.
        float stereoPan = Mathf.Clamp((viewport.x - 0.5f) * 2f, -1f, 1f) * MaximumStereoPan;
        float angle = stereoPan * StereoSpeakerAngleDegrees * Mathf.Deg2Rad;
        voiceObject.transform.localPosition = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

        if (!bankLoaded)
        {
            AkBankManager.LoadBank(soundBank, false, false);
            bankLoaded = true;
        }

        float volume = DarwinWwiseAmbience.EffectsVolume;
        if (Mathf.Abs(volume - lastVolume) >= 0.01f)
        {
            AkUnitySoundEngine.SetGameObjectOutputBusVolume(voiceObject, listener.gameObject, volume);
            lastVolume = volume;
        }

        if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f ||
            viewport.y < 0f || viewport.y > 1f || volume <= 0.01f ||
            playingId != 0 || Playing.Count >= MaximumVoices ||
            Time.time < nextPlayAt) return;

        playingId = AkUnitySoundEngine.PostEvent(playEvent, voiceObject,
            (uint)AkCallbackType.AK_EndOfEvent, OnEventFinished, null);
        if (playingId != AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
            Playing.Add(this);
        else
        {
            if (!failedEventLogged)
                Debug.LogError("Wwise animal event failed: " + playEvent + " (bank " + soundBank + ").", this);
            failedEventLogged = true;
            playingId = 0;
        }
        nextPlayAt = Time.time + Random.Range(MinimumInterval, MaximumInterval);
    }

    private void OnEventFinished(object cookie, AkCallbackType type, AkCallbackInfo info)
    {
        playingId = 0;
        Playing.Remove(this);
    }

    private void StopVoice()
    {
        if (playingId != 0 && AkUnitySoundEngine.IsInitialized())
            AkUnitySoundEngine.StopPlayingID(playingId);
        playingId = 0;
        Playing.Remove(this);
    }

    private void UnloadBank()
    {
        if (bankLoaded && AkUnitySoundEngine.IsInitialized())
            AkBankManager.UnloadBank(soundBank);
        bankLoaded = false;
    }

    private void OnDisable()
    {
        StopVoice();
        UnloadBank();
    }

    private void OnDestroy()
    {
        if (voiceObject != null) Destroy(voiceObject);
    }
}
