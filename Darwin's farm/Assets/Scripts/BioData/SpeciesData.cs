using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Darwin's Farm/Species")]
public class SpeciesData: ScriptableObject
{
    private static readonly System.Random ClueHighlightRandom = new System.Random();
    [SerializeField] private string speciesName;
    [SerializeField] private Sprite sprite;
    public Sprite Sprite => sprite;

    [Header("Species presentation (shared by all populations)")]
    [SerializeField] private string speciesId;
    [SerializeField] private Sprite portrait;
    [SerializeField] private PopulationAnimationSet populationAnimation;
    [Header("Wwise animal audio (leave empty until SB_Animals is delivered)")]
    [SerializeField] private string animalPlayEvent;
    [SerializeField] private string animalSoundBank = "SB_Animals";
    public string AnimalPlayEvent => animalPlayEvent;
    public string AnimalSoundBank => animalSoundBank;
    [SerializeField, TextArea(4, 12)] private string description;
    [SerializeField] private string[] traitClueAnchors = new string[5];
    public string SpeciesId => speciesId;
    public Sprite Portrait => portrait != null ? portrait : sprite;
    public PopulationAnimationSet PopulationAnimation => populationAnimation;
    public string Description => string.IsNullOrWhiteSpace(description) ? "暂无物种介绍" : description;
    public string EcologicalTrivia => ExtractEcologicalTrivia(Description);
    public static string ExtractEcologicalTrivia(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "暂无物种介绍";
        string normalized = value.Replace("\r\n", "\n").Trim();
        int separator = normalized.IndexOf("\n\n", StringComparison.Ordinal);
        return separator < 0 ? normalized : normalized.Substring(0, separator);
    }
    public string BreedingClue
    {
        get
        {
            string value = Description;
            int separator = value.IndexOf("\n\n", StringComparison.Ordinal);
            return separator < 0 ? value : value.Substring(separator + 2);
        }
    }
    public string FormattedDescription => Description.Replace(BreedingClue, FormattedClueNarrative);

    private string FormattedClueNarrative
    {
        get
        {
            string clue = BreedingClue;
            if (traitClueAnchors == null) return clue;
            var candidates = new List<int>();
            for (int trait = 0; trait < Mathf.Min(5, traitClueAnchors.Length); trait++)
            {
                string anchor = traitClueAnchors[trait];
                if (HasRevealedTrait(trait) || string.IsNullOrEmpty(anchor)) continue;
                if (clue.IndexOf(anchor, StringComparison.Ordinal) >= 0) candidates.Add(trait);
            }
            int count = Mathf.Min(2, candidates.Count);
            for (int i = 0; i < count; i++)
            {
                int pick = ClueHighlightRandom.Next(i, candidates.Count);
                int selected = candidates[pick];
                candidates[pick] = candidates[i];
                candidates[i] = selected;
            }
            for (int i = 0; i < count; i++)
            {
                string anchor = traitClueAnchors[candidates[i]];
                int position = clue.IndexOf(anchor, StringComparison.Ordinal);
                clue = clue.Insert(position + anchor.Length, "</color>")
                    .Insert(position, "<color=#C3342D>");
            }
            return clue;
        }
    }

    public string FormattedBreedingClue
    {
        get
        {
            string[] names = { "温度", "湿度", "运动", "繁殖", "体型" };
            int[] traits = { 0, 1, 2, 4, 3 };
            int[] values = { baseFitTemperature, baseFitHumidity, baseMovementAbility,
                baseFertility, baseSize };
            var stats = new System.Text.StringBuilder();
            for (int i = 0; i < traits.Length; i++)
            {
                if (i == 2) stats.Append('\n');
                else if (i > 0) stats.Append("   ");
                stats.Append(names[i]).Append('：');
                if (HasRevealedTrait(traits[i]))
                    stats.Append("<color=#C3342D>").Append(values[i]).Append("</color>");
                else stats.Append("？？？");
            }
            return FormattedClueNarrative + "\n\n" + stats;
        }
    }

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
    [Range(1, 100)] public int bioenergyValue = 1;
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
    [NonSerialized] private int revealedTraitMask;
    public bool HasEverUnlocked => hasEverUnlocked;
    public bool IsExtinct => isExtinct;
    public int RevealedTraitMask => revealedTraitMask;
    public bool HasRevealedTrait(int trait) => trait >= 0 && trait < 5 && (revealedTraitMask & (1 << trait)) != 0;
    public void RevealTrait(int trait)
    {
        if (trait >= 0 && trait < 5) revealedTraitMask |= 1 << trait;
    }
    public string SpeciesName => speciesName;

    // One-based column (x), row (y); independent layouts for the two maps.
    public Vector2Int evolutionGridPos;
    public Vector2Int foodGridPos;

    internal void SetLivingPopulation(bool living)
    {
        if (living) hasEverUnlocked = true;
        isExtinct = hasEverUnlocked && !living;
    }

    public void RestoreProgress(bool unlocked, bool extinct)
        => RestoreProgress(unlocked, extinct, 0);

    public void RestoreProgress(bool unlocked, bool extinct, int traitMask)
    {
        hasEverUnlocked = unlocked;
        isExtinct = unlocked && extinct;
        revealedTraitMask = traitMask & 31;
    }
}
