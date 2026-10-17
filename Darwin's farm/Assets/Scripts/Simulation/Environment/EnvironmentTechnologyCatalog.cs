using System;

namespace DarwinFarm.Environment
{
    // Five player-facing tools. Stock and daily recovery are separate actions.
    public enum EnvironmentTechnologyKind
    {
        TemperatureChange = 0, HumidityChange = 1, PlantStockChange = 2,
        ElevationChange = 3, PlantRecoveryChange = 5
    }

    public readonly struct EnvironmentTechnologyChoice
    {
        public readonly EnvironmentTechnologyKind Kind;
        public readonly int Tier;
        public EnvironmentTechnologyChoice(EnvironmentTechnologyKind kind, int tier = 1)
        { Kind = kind; Tier = tier; }
    }

    public static class EnvironmentTechnologyCatalog
    {
        public static int Price(EnvironmentTechnologyChoice choice)
        {
            BioenergySettings settings = BioenergySettings.Current;
            int tier = Math.Abs(choice.Tier);
            switch (choice.Kind)
            {
                case EnvironmentTechnologyKind.TemperatureChange:
                case EnvironmentTechnologyKind.HumidityChange:
                    return tier == 1 ? settings.climateSmallCost : settings.climateLargeCost;
                case EnvironmentTechnologyKind.PlantStockChange:
                case EnvironmentTechnologyKind.PlantRecoveryChange:
                    return choice.Tier > 0 ?
                        (tier == 1 ? settings.plantIncreaseSmallCost : settings.plantIncreaseLargeCost) :
                        (tier == 1 ? settings.plantDecreaseSmallCost : settings.plantDecreaseLargeCost);
                case EnvironmentTechnologyKind.ElevationChange: return settings.elevationCost;
                default: return 0;
            }
        }
        public static readonly EnvironmentTechnologyKind[] All =
        {
            EnvironmentTechnologyKind.TemperatureChange,
            EnvironmentTechnologyKind.ElevationChange,
            EnvironmentTechnologyKind.HumidityChange,
            EnvironmentTechnologyKind.PlantRecoveryChange,
            EnvironmentTechnologyKind.PlantStockChange
        };

        public static string ShortName(EnvironmentTechnologyKind kind)
        {
            switch (kind)
            {
                case EnvironmentTechnologyKind.TemperatureChange: return "温度";
                case EnvironmentTechnologyKind.ElevationChange: return "海拔";
                case EnvironmentTechnologyKind.HumidityChange: return "湿度";
                case EnvironmentTechnologyKind.PlantRecoveryChange: return "植物增量";
                case EnvironmentTechnologyKind.PlantStockChange: return "植物存量";
                default: return "未知工具";
            }
        }

        public static string Name(EnvironmentTechnologyKind kind)
        {
            switch (kind)
            {
                case EnvironmentTechnologyKind.TemperatureChange: return "温度变化工具";
                case EnvironmentTechnologyKind.HumidityChange: return "湿度变化工具";
                case EnvironmentTechnologyKind.PlantStockChange: return "植物存量工具";
                case EnvironmentTechnologyKind.PlantRecoveryChange: return "植物日增量工具";
                case EnvironmentTechnologyKind.ElevationChange: return "海拔变化工具";
                default: return "未知科技";
            }
        }

        public static bool Validate(EnvironmentTechnologyChoice choice)
        {
            if (!Enum.IsDefined(typeof(EnvironmentTechnologyKind), choice.Kind)) return false;
            if (choice.Kind == EnvironmentTechnologyKind.ElevationChange)
                return choice.Tier == 1 || choice.Tier == -1;
            return choice.Tier == 1 || choice.Tier == 2 || choice.Tier == -1 || choice.Tier == -2;
        }

        public static int SignedClimate(EnvironmentTechnologyChoice choice)
        {
            return choice.Tier * 25;
        }
        public static int GrowthAmount(EnvironmentTechnologyChoice choice) =>
            Math.Abs(choice.Tier) * 25000;
        public static float Fraction(EnvironmentTechnologyChoice choice) =>
            Math.Abs(choice.Tier) * .25f;
    }
}
