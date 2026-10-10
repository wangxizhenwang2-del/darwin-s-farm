using System;

namespace DarwinFarm.Environment
{
    // Five player-facing tools. Prices are deliberately absent until
    // the economy system is implemented; every current deployment costs zero.
    public enum EnvironmentTechnologyKind
    {
        TemperatureChange, HumidityChange, PlantChange, ElevationChange, MonsoonAnchor
    }

    public readonly struct EnvironmentTechnologyChoice
    {
        public readonly EnvironmentTechnologyKind Kind;
        public readonly int Tier;
        public readonly int TemperatureOffset;
        public readonly int HumidityOffset;
        public EnvironmentTechnologyChoice(EnvironmentTechnologyKind kind, int tier = 1,
            int temperatureOffset = 20, int humidityOffset = 20)
        { Kind = kind; Tier = tier; TemperatureOffset = temperatureOffset; HumidityOffset = humidityOffset; }
    }

    public static class EnvironmentTechnologyCatalog
    {
        public static readonly EnvironmentTechnologyKind[] All =
        {
            EnvironmentTechnologyKind.TemperatureChange,
            EnvironmentTechnologyKind.HumidityChange,
            EnvironmentTechnologyKind.PlantChange,
            EnvironmentTechnologyKind.ElevationChange,
            EnvironmentTechnologyKind.MonsoonAnchor
        };

        public static string Name(EnvironmentTechnologyKind kind)
        {
            switch (kind)
            {
                case EnvironmentTechnologyKind.TemperatureChange: return "温度变化工具";
                case EnvironmentTechnologyKind.HumidityChange: return "湿度变化工具";
                case EnvironmentTechnologyKind.PlantChange: return "植物变化工具";
                case EnvironmentTechnologyKind.ElevationChange: return "海拔变化工具";
                case EnvironmentTechnologyKind.MonsoonAnchor: return "季风工具";
                default: return "未知科技";
            }
        }

        public static bool Validate(EnvironmentTechnologyChoice choice)
        {
            if (!Enum.IsDefined(typeof(EnvironmentTechnologyKind), choice.Kind)) return false;
            if (choice.Kind == EnvironmentTechnologyKind.MonsoonAnchor)
                return choice.Tier == 1 &&
                    Math.Abs(choice.TemperatureOffset) == 20 &&
                    Math.Abs(choice.HumidityOffset) == 20;
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
        public static string MonsoonPatternName(EnvironmentTechnologyChoice choice)
        {
            if (choice.TemperatureOffset < 0)
                return choice.HumidityOffset > 0 ? "冷湿" : "干冷";
            return choice.HumidityOffset > 0 ? "暖湿" : "干热";
        }
    }
}
