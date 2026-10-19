using System.Collections.Generic;
using UnityEngine;

// Added to a population's moving Leader when its species has a Wwise event.
[RequireComponent(typeof(AkGameObj))]
public sealed class DarwinWwiseAnimalEmitter : MonoBehaviour
{
    private static readonly HashSet<DarwinWwiseAnimalEmitter> Playing = new HashSet<DarwinWwiseAnimalEmitter>();
    private const int MaximumVoices = 8;
    private const float MaximumDistance = 24f;
    private const float MinimumInterval = 5f;
    private const float MaximumInterval = 10f;

    private string soundBank;
    private string playEvent;
    private bool bankLoaded;
    private uint playingId;
    private float nextPlayAt;
    private float lastVolume = -1f;
    private AkAudioListener listener;
    private Camera viewCamera;

    public void Bind(string bank, string eventName)
    {
        if (soundBank == bank && playEvent == eventName) return;
        StopVoice();
        UnloadBank();
        soundBank = bank;
        playEvent = eventName;
        nextPlayAt = Time.time + Random.Range(0f, MinimumInterval);
    }

    private void Update()
    {
        if (string.IsNullOrWhiteSpace(playEvent) || string.IsNullOrWhiteSpace(soundBank) ||
            !AkUnitySoundEngine.IsInitialized()) return;

        if (listener == null) listener = FindFirstObjectByType<AkAudioListener>();
        if (viewCamera == null) viewCamera = Camera.main;
        if (listener == null || viewCamera == null) return;

        if (!bankLoaded)
        {
            AkBankManager.LoadBank(soundBank, false, false);
            bankLoaded = true;
        }

        float distance = Vector3.Distance(listener.transform.position, transform.position);
        float distanceWeight = 1f - Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(MaximumDistance * 0.5f, MaximumDistance, distance));
        float zoomWeight = viewCamera.orthographic
            ? Mathf.InverseLerp(70f, 10f, viewCamera.orthographicSize)
            : Mathf.InverseLerp(75f, 25f, viewCamera.fieldOfView);
        float volume = distanceWeight * zoomWeight * DarwinWwiseAmbience.EffectsVolume;
        if (Mathf.Abs(volume - lastVolume) >= 0.01f)
        {
            AkUnitySoundEngine.SetGameObjectOutputBusVolume(gameObject, listener.gameObject, volume);
            lastVolume = volume;
        }

        if (volume <= 0.01f || playingId != 0 || Playing.Count >= MaximumVoices ||
            Time.time < nextPlayAt) return;

        playingId = AkUnitySoundEngine.PostEvent(playEvent, gameObject,
            (uint)AkCallbackType.AK_EndOfEvent, OnEventFinished, null);
        if (playingId != AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
            Playing.Add(this);
        else
            playingId = 0;
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
}
