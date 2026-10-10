using System;

namespace DarwinFarm.Environment
{
    public enum TerrainKind { Forest, Rainforest, Grassland, Desert, Tundra, Highland, SnowMountain, Volcano }
    public enum TerrainConnection { Grass, Sand, Rock }
    public enum EnvironmentAttribute { Temperature, Humidity, Recovery }
    public enum EffectPhase { None, Entering, Holding, Returning, Automatic }
    public enum MonsoonExitReason { Manual, SourceChanged, Overlap, Replaced }

    public readonly struct GridPosition : IEquatable<GridPosition>, IComparable<GridPosition>
    {
        public readonly int X, Y;
        public GridPosition(int x, int y) { X = x; Y = y; }
        public bool Equals(GridPosition other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridPosition other && Equals(other);
        public override int GetHashCode() => unchecked(X * 397 ^ Y);
        public int CompareTo(GridPosition other) => X != other.X ? X.CompareTo(other.X) : Y.CompareTo(other.Y);
        public override string ToString() => "(" + X + ", " + Y + ")";
    }

    public readonly struct TerrainDefaults
    {
        public readonly int Elevation, Temperature, Humidity, Recovery;
        public readonly TerrainConnection Connection;
        public string ElevationDefinition => Elevation == 0 ? "低" : Elevation == 1 ? "中" : "高";
        public string TemperatureDefinition => Temperature <= 34 ? "很冷" : Temperature <= 49 ? "偏冷" :
            Temperature <= 65 ? "中" : Temperature <= 82 ? "偏热" : "很热";
        public string HumidityDefinition => Humidity <= 34 ? "很干" : Humidity <= 49 ? "偏干" :
            Humidity <= 65 ? "中" : Humidity <= 82 ? "偏湿" : "很湿";
        // These labels describe the agreed default values, not terrain classification boundaries.
        public string RecoveryDefinition => Recovery <= 50000 ? "少" : Recovery < 100000 ? "偏少" :
            Recovery == 100000 ? "中" : Recovery < 150000 ? "偏多" : "多";
        public TerrainDefaults(int elevation, int temperature, int humidity, int recovery, TerrainConnection connection)
        { Elevation = elevation; Temperature = temperature; Humidity = humidity; Recovery = recovery; Connection = connection; }
        public double Get(EnvironmentAttribute attribute) => attribute == EnvironmentAttribute.Temperature ? Temperature :
            attribute == EnvironmentAttribute.Humidity ? Humidity : Recovery;
    }

    // Pure domain rules. No Unity, rendering, map objects or economy implementation.
    public static class EnvironmentRules
    {
        public const int PlantCapacity = 1000000, WaterPlantCapacity = 1500000;
        public const int WaterDefaultRecovery = 75000, MaximumRecovery = 150000;
        public const int TransitionDays = 25, ClimateHoldDays = 150, ClimateReturnDays = 150;
        public const int RecoveryHoldDays = 50, RecoveryReturnDays = 50;
        // Existing baseline profiles also drive return targets; their elevation is
        // only a placement preset, not an intrinsic property of a terrain kind.
        public static TerrainDefaults Defaults(TerrainKind kind)
        {
            switch (kind)
            {
                case TerrainKind.Forest: return new TerrainDefaults(1, 58, 50, 125000, TerrainConnection.Grass);
                case TerrainKind.Rainforest: return new TerrainDefaults(0, 75, 83, 125000, TerrainConnection.Grass);
                case TerrainKind.Grassland: return new TerrainDefaults(0, 68, 68, 100000, TerrainConnection.Grass);
                case TerrainKind.Desert: return new TerrainDefaults(0, 68, 17, 50000, TerrainConnection.Sand);
                case TerrainKind.Tundra: return new TerrainDefaults(1, 17, 68, 75000, TerrainConnection.Grass);
                case TerrainKind.Highland: return new TerrainDefaults(2, 50, 50, 100000, TerrainConnection.Grass);
                case TerrainKind.SnowMountain: return new TerrainDefaults(2, 17, 50, 50000, TerrainConnection.Rock);
                case TerrainKind.Volcano: return new TerrainDefaults(2, 83, 50, 50000, TerrainConnection.Rock);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static TerrainKind Classify(int elevation, int temperature, int humidity, int recovery)
        {
            if (elevation < 0 || elevation > 2 || temperature < 0 || temperature > 100 ||
                humidity < 0 || humidity > 100 || recovery < 0 || recovery > MaximumRecovery)
                throw new ArgumentOutOfRangeException("Environment values exceed their legal ranges.");
            if (elevation == 2)
            {
                if (temperature <= 34)
                {
                    if (recovery <= 50000) return TerrainKind.SnowMountain;
                    if (humidity >= 35 && recovery <= 100000) return TerrainKind.Tundra;
                    return TerrainKind.Highland;
                }
                return temperature >= 66 && recovery <= 50000 ? TerrainKind.Volcano : TerrainKind.Highland;
            }
            if (temperature <= 34)
            {
                if (elevation == 0) return temperature <= 14 || humidity <= 34 ? TerrainKind.Desert : TerrainKind.Forest;
                if (humidity <= 34) return TerrainKind.Highland;
                return recovery <= 100000 ? TerrainKind.Tundra : TerrainKind.Forest;
            }
            if (humidity <= 34) return recovery <= 50000 ? TerrainKind.Desert : TerrainKind.Grassland;
            if (recovery <= 100000)
            {
                if (elevation == 1 && temperature <= 65 && humidity <= 65 && recovery > 50000) return TerrainKind.Highland;
                return TerrainKind.Grassland;
            }
            if (temperature <= 49 || temperature <= 65 && humidity <= 65) return TerrainKind.Forest;
            return TerrainKind.Rainforest;
        }

        public static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
        public static double Clamp(EnvironmentAttribute attribute, double value) => Math.Max(0,
            Math.Min(attribute == EnvironmentAttribute.Recovery ? MaximumRecovery : 100, value));
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static bool IsClimateStrength(double value) => IsFinite(value) && (Math.Abs(value) == 25 || Math.Abs(value) == 50);
        public static bool IsMonsoonStrength(double value) => IsFinite(value) && Math.Abs(value) == 20;
        public static bool IsRecoveryStrength(double value) => IsFinite(value) && (Math.Abs(value) == 25000 || Math.Abs(value) == 50000);
        public static double PotentialRecovery(TerrainDefaults baseline, double temperature, double humidity)
        {
            double moisture = baseline.Humidity == 0 ? 1 : Math.Min(1.5, humidity / baseline.Humidity);
            double warmth = Math.Max(0, 1 - Math.Abs(temperature - baseline.Temperature) / 100);
            return Clamp(EnvironmentAttribute.Recovery, baseline.Recovery * moisture * warmth);
        }
        public static double ChangeStock(double stock, double signedFraction)
            => ChangeStock(stock, PlantCapacity, signedFraction);
        public static double ChangeStock(double stock, double capacity, double signedFraction)
        {
            if (!IsFinite(capacity) || capacity <= 0 || !IsFinite(stock) || stock < 0 || stock > capacity ||
                !IsFinite(signedFraction) || (Math.Abs(signedFraction) != .25 && Math.Abs(signedFraction) != .5))
                throw new ArgumentOutOfRangeException(nameof(signedFraction));
            return Math.Max(0, Math.Min(capacity, stock + signedFraction * (signedFraction > 0 ? capacity : stock)));
        }

        // The combined plant tool always acts on the stock that exists now.
        public static double MultiplyStock(double stock, double capacity, double signedFraction)
        {
            if (!IsFinite(stock) || !IsFinite(capacity) || capacity <= 0 || stock < 0 || stock > capacity ||
                !IsFinite(signedFraction) || (Math.Abs(signedFraction) != .25 && Math.Abs(signedFraction) != .5))
                throw new ArgumentOutOfRangeException(nameof(signedFraction));
            return Math.Max(0, Math.Min(capacity, stock * (1 + signedFraction)));
        }
    }
}
