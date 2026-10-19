using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public enum EcologicalEventType
{
    NewSpecies, LocalExtinction, GlobalExtinction, SpeciesReappeared,
    SuccessfulMigration, PopulationCrash, CapacityCrisis,
    TerrainUnlocked, WatersUnlocked
}

[Serializable]
public sealed class EcologicalEventData
{
    public string eventId, speciesId, sourceBlockId, targetBlockId, speciesName;
    public int gameDay, sequence, populationCount;
    public EcologicalEventType type;
    public string message;
    public int Priority => type == EcologicalEventType.NewSpecies || type == EcologicalEventType.GlobalExtinction ||
        type == EcologicalEventType.TerrainUnlocked || type == EcologicalEventType.WatersUnlocked ? 3 :
        type == EcologicalEventType.LocalExtinction || type == EcologicalEventType.SpeciesReappeared ? 2 : 1;
}

public sealed class EcologicalEventBus
{
    public event Action<EcologicalEventData> Published;
    public void Publish(EcologicalEventData value) => Published?.Invoke(value);
}

// Reads only committed day-end state and the simulation's committed transition
// events. It never advances the world or consumes its random sequence.
