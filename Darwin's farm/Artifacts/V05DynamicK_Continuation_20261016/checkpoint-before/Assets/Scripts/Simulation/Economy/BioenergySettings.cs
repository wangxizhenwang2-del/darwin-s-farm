using UnityEngine;

// Shared tuning data for all player-facing economy calculations.
[CreateAssetMenu(menuName = "Darwin's Farm/Economy Settings")]
public sealed class BioenergySettings : ScriptableObject
{
    private static BioenergySettings current;
    public static BioenergySettings Current
    {
        get
        {
            if (current == null)
            {
                current = Resources.Load<BioenergySettings>("BioenergySettings");
                if (current == null)
                {
                    Debug.LogError("Missing Resources/BioenergySettings asset; using V4.4 defaults in memory.");
                    current = CreateInstance<BioenergySettings>();
                }
            }
            return current;
        }
    }

    public long initialBalance = 2000;
    public int dailyIncomeMultiplier = 1;
    public int harvestIncomeMultiplier = 10;
    [Range(0, 100)] public int harvestPercent = 50;
    public int minimumHarvestCount = 50;
    public int herbivoreUnlockReward = 200;
    public int levelOneUnlockReward = 500;
    public int levelTwoUnlockReward = 1000;
    public int fallbackHerbivoreValue = 10;
    public int fallbackLevelOneValue = 20;
    public int fallbackLevelTwoValue = 40;

    public int expandTileCost = 600;
    public int reshapeWaterCost = 500;
    public int demolishTileCost = 200;
    public int migrateCost = 1000;
    public int directedEvolutionCost = 1000;
    public int storageCost = 5000;

    public int climateSmallCost = 220;
    public int climateLargeCost = 480;
    public int plantIncreaseSmallCost = 260;
    public int plantIncreaseLargeCost = 560;
    public int plantDecreaseSmallCost = 420;
    public int plantDecreaseLargeCost = 960;
    public int elevationCost = 500;
    public int monsoonCost = 1000;

    public int FallbackSpeciesValue(int level) =>
        level <= 0 ? fallbackHerbivoreValue :
        level == 1 ? fallbackLevelOneValue : fallbackLevelTwoValue;

    public int UnlockReward(int level) =>
        level <= 0 ? herbivoreUnlockReward :
        level == 1 ? levelOneUnlockReward : levelTwoUnlockReward;

    private void OnValidate()
    {
        initialBalance = System.Math.Max(0, initialBalance);
        dailyIncomeMultiplier = Mathf.Max(0, dailyIncomeMultiplier);
        harvestIncomeMultiplier = Mathf.Max(0, harvestIncomeMultiplier);
        harvestPercent = Mathf.Clamp(harvestPercent, 0, 100);
        minimumHarvestCount = Mathf.Max(0, minimumHarvestCount);
        herbivoreUnlockReward = Mathf.Max(0, herbivoreUnlockReward);
        levelOneUnlockReward = Mathf.Max(0, levelOneUnlockReward);
        levelTwoUnlockReward = Mathf.Max(0, levelTwoUnlockReward);
        fallbackHerbivoreValue = Mathf.Clamp(fallbackHerbivoreValue, 10, 100);
        fallbackLevelOneValue = Mathf.Clamp(fallbackLevelOneValue, 10, 100);
        fallbackLevelTwoValue = Mathf.Clamp(fallbackLevelTwoValue, 10, 100);
        expandTileCost = Mathf.Max(0, expandTileCost);
        reshapeWaterCost = Mathf.Max(0, reshapeWaterCost);
        demolishTileCost = Mathf.Max(0, demolishTileCost);
        migrateCost = Mathf.Max(0, migrateCost);
        directedEvolutionCost = Mathf.Max(0, directedEvolutionCost);
        storageCost = Mathf.Max(0, storageCost);
        climateSmallCost = Mathf.Max(0, climateSmallCost);
        climateLargeCost = Mathf.Max(0, climateLargeCost);
        plantIncreaseSmallCost = Mathf.Max(0, plantIncreaseSmallCost);
        plantIncreaseLargeCost = Mathf.Max(0, plantIncreaseLargeCost);
        plantDecreaseSmallCost = Mathf.Max(0, plantDecreaseSmallCost);
        plantDecreaseLargeCost = Mathf.Max(0, plantDecreaseLargeCost);
        elevationCost = Mathf.Max(0, elevationCost);
        monsoonCost = Mathf.Max(0, monsoonCost);
    }
}
