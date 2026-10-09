using System;

namespace DarwinFarm.Environment
{
    // The ten V4.3 player technologies. Prices are deliberately absent until
    // the economy system is implemented; every current deployment costs zero.
    public enum EnvironmentTechnologyKind
    {
        HeatInjection, RadiativeCooling, CloudSeeding, VaporRecovery,
        GrowthCatalyst, EcologicalSeeding, EcologicalSuppression,
        CrustUplift, StrataSubsidence, MonsoonAnchor
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
            EnvironmentTechnologyKind.HeatInjection,
            EnvironmentTechnologyKind.RadiativeCooling,
            EnvironmentTechnologyKind.CloudSeeding,
            EnvironmentTechnologyKind.VaporRecovery,
            EnvironmentTechnologyKind.GrowthCatalyst,
            EnvironmentTechnologyKind.EcologicalSeeding,
            EnvironmentTechnologyKind.EcologicalSuppression,
            EnvironmentTechnologyKind.CrustUplift,
            EnvironmentTechnologyKind.StrataSubsidence,
            EnvironmentTechnologyKind.MonsoonAnchor
        };

        public static string Name(EnvironmentTechnologyKind kind)
        {
            switch (kind)
            {
                case EnvironmentTechnologyKind.HeatInjection: return "热通量注入阵列";
                case EnvironmentTechnologyKind.RadiativeCooling: return "辐射散热幕";
                case EnvironmentTechnologyKind.CloudSeeding: return "云核播撒器";
                case EnvironmentTechnologyKind.VaporRecovery: return "水汽回收阵列";
                case EnvironmentTechnologyKind.GrowthCatalyst: return "生长催化脉冲";
                case EnvironmentTechnologyKind.EcologicalSeeding: return "生态播种舱";
                case EnvironmentTechnologyKind.EcologicalSuppression: return "生态抑制装置";
                case EnvironmentTechnologyKind.CrustUplift: return "地壳抬升器";
                case EnvironmentTechnologyKind.StrataSubsidence: return "地层沉降器";
                case EnvironmentTechnologyKind.MonsoonAnchor: return "季风锚";
                default: return "未知科技";
            }
        }

        public static bool Validate(EnvironmentTechnologyChoice choice)
        {
            if (!Enum.IsDefined(typeof(EnvironmentTechnologyKind), choice.Kind)) return false;
            if (choice.Kind == EnvironmentTechnologyKind.MonsoonAnchor)
                return choice.Tier == 1 &&
                    EnvironmentRules.IsMonsoonStrength(choice.TemperatureOffset) &&
                    EnvironmentRules.IsMonsoonStrength(choice.HumidityOffset);
            if (choice.Kind == EnvironmentTechnologyKind.CrustUplift ||
                choice.Kind == EnvironmentTechnologyKind.StrataSubsidence)
                return choice.Tier == 1;
            return choice.Tier == 1 || choice.Tier == 2;
        }

        public static int SignedClimate(EnvironmentTechnologyChoice choice)
        {
            int amount = choice.Tier == 1 ? 25 : 50;
            return choice.Kind == EnvironmentTechnologyKind.RadiativeCooling ||
                choice.Kind == EnvironmentTechnologyKind.VaporRecovery ? -amount : amount;
        }
        public static int GrowthAmount(EnvironmentTechnologyChoice choice) =>
            choice.Tier == 1 ? 25000 : 50000;
        public static float Fraction(EnvironmentTechnologyChoice choice) =>
            choice.Tier == 1 ? .25f : .5f;
    }
}
