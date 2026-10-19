using System;
using System.Collections.Generic;
using UnityEngine;

// Gameplay-to-audio contract. Wwise content owns the actual ducking and music transitions.
public static class DarwinAudioDispatch
{
    public enum MusicPhase { First, Second }

    public const int SecondPhaseSpeciesCount = 8;

    public static event Action<bool> MenuDuckingChanged;
    public static event Action<bool> GameplayChanged;
    public static event Action<MusicPhase> MusicPhaseChanged;
    public static event Action<float> MusicVolumeChanged;

    private static readonly HashSet<string> OpenMenus = new HashSet<string>(StringComparer.Ordinal);

    public static bool IsMenuDucked => OpenMenus.Count > 0;
    public static bool IsGameplayActive { get; private set; }
    public static MusicPhase CurrentMusicPhase { get; private set; } = MusicPhase.First;
    public static int UnlockedSpeciesCount { get; private set; }
    public static float CurrentMusicVolume { get; private set; } = 1f;

    // Each menu uses a stable, unique key. Closing one menu cannot unduck another.
    public static void SetMenuOpen(string menuKey, bool open)
    {
        if (string.IsNullOrEmpty(menuKey)) throw new ArgumentException("Menu key is required.", nameof(menuKey));
        bool wasDucked = IsMenuDucked;
        if (open) OpenMenus.Add(menuKey);
        else OpenMenus.Remove(menuKey);
        if (wasDucked != IsMenuDucked) MenuDuckingChanged?.Invoke(IsMenuDucked);
    }

    public static void SetGameplayActive(bool active)
    {
        if (IsGameplayActive == active) return;
        IsGameplayActive = active;
        GameplayChanged?.Invoke(active);
    }

    // HasEverUnlocked includes extinct species and is restored from the save file.
    public static void RefreshUnlockedSpecies(SaveAssetCatalog catalog)
    {
        if (catalog == null || catalog.species == null) return;
        var unique = new HashSet<SpeciesData>();
        foreach (var entry in catalog.species)
            if (entry.asset != null && entry.asset.HasEverUnlocked) unique.Add(entry.asset);
        UnlockedSpeciesCount = unique.Count;
        MusicPhase next = UnlockedSpeciesCount >= SecondPhaseSpeciesCount
            ? MusicPhase.Second : MusicPhase.First;
        if (next == CurrentMusicPhase) return;
        CurrentMusicPhase = next;
        MusicPhaseChanged?.Invoke(next);
    }

    public static void SetMusicVolume(float volume)
    {
        float clamped = Mathf.Clamp01(volume);
        if (Mathf.Approximately(clamped, CurrentMusicVolume)) return;
        CurrentMusicVolume = clamped;
        MusicVolumeChanged?.Invoke(clamped);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        OpenMenus.Clear();
        IsGameplayActive = false;
        UnlockedSpeciesCount = 0;
        CurrentMusicPhase = MusicPhase.First;
        CurrentMusicVolume = 1f;
        MenuDuckingChanged = null;
        GameplayChanged = null;
        MusicPhaseChanged = null;
        MusicVolumeChanged = null;
    }
}
