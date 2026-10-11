using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Darwin's Farm/Species")]
public class SpeciesData: ScriptableObject
{
    [SerializeField] private string speciesName;
    [SerializeField] private Sprite sprite;
    public Sprite Sprite => sprite;

    [Header("Species presentation (shared by all populations)")]
    [SerializeField] private string speciesId;
    [SerializeField] private Sprite portrait;
    [SerializeField, TextArea(4, 12)] private string description;
    public string SpeciesId => speciesId;
    public Sprite Portrait => portrait != null ? portrait : sprite;
    public string Description => string.IsNullOrWhiteSpace(description) ? "暂无物种介绍" : description;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(speciesId)) speciesId = Guid.NewGuid().ToString("N");
        // Asset queries must run on the editor thread, after serialization finishes.
        UnityEditor.EditorApplication.delayCall += CheckPresentationId;
    }

    private void CheckPresentationId()
    {
        if (this == null) return;
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:SpeciesData"))
        {
            var other = UnityEditor.AssetDatabase.LoadAssetAtPath<SpeciesData>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (other != null && other != this && other.speciesId == speciesId)
                Debug.LogWarning("Duplicate Species ID: " + speciesId + " (" + name + ", " + other.name + ")", this);
        }
    }
#endif

    // 只有列出的目标才会触发预设物种的进化。
    public List<SpeciesData> evolutionTargets = new List<SpeciesData>();
    // Empty means the legacy adjacent-trophic-level food web. Test bioweb
    // assets list their exact prey, including level-two hunters of herbivores.
    public List<SpeciesData> preySpecies = new List<SpeciesData>();

    [Range(0, 2)] public int trophicLevel; //0 食草，1、2 分别捕食低一级，最多三级食物链
    [Range(1, 100)] public int bioenergyValue = 10;
    [SerializeField] private string economyId;
    public string EconomyId => string.IsNullOrWhiteSpace(economyId) ? speciesName : economyId;
    [Range(0, 100)] public int baseMovementAbility;
    [Range(0, 100)] public int baseHabitatNiche = 50;
    [Range(0, 100)] public int baseFitTemperature = 50;
    [Range(0, 100)] public int baseFitHumidity = 50;
    [Range(1, 100)] public int baseSize = 10;
    [Range(0, 100)] public int baseFertility = 50;
    public EcologicalNiche baseEcologicalNiche = EcologicalNiche.Land;

    // 一局游戏内的状态；未来存档系统可读取并恢复，不写回物种资产。
    [NonSerialized] private bool hasEverUnlocked;
    [NonSerialized] private bool isExtinct;
    public bool HasEverUnlocked => hasEverUnlocked;
    public bool IsExtinct => isExtinct;
    public string SpeciesName => speciesName;

    internal void SetLivingPopulation(bool living)
    {
        if (living) hasEverUnlocked = true;
        isExtinct = hasEverUnlocked && !living;
    }

    public void RestoreProgress(bool unlocked, bool extinct)
    {
        hasEverUnlocked = unlocked;
        isExtinct = unlocked && extinct;
    }
}
