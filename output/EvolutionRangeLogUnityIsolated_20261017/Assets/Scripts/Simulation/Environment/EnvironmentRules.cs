using System;

namespace DarwinFarm.Environment
{
    public enum TerrainKind { Forest, Rainforest, Grassland, Desert, Tundra, Highland, SnowMountain, Volcano }
    // The visual terrain is shared; these 27 states own the environmental defaults.
    public enum HabitatState
    {
        Z0ColdDry, Z0ColdMiddle, Z0ColdWet, Z0MiddleDry, Z0MiddleMiddle, Z0MiddleWet,
        Z0HotDry, Z0HotMiddle, Z0HotWet,
        Z1ColdDry, Z1ColdMiddle, Z1ColdWet, Z1MiddleDry, Z1MiddleMiddle, Z1MiddleWet,
        Z1HotDry, Z1HotMiddle, Z1HotWet,
        Z2ColdDry, Z2ColdMiddle, Z2ColdWet, Z2MiddleDry, Z2MiddleMiddle, Z2MiddleWet,
        Z2HotDry, Z2HotMiddle, Z2HotWet
    }
    public enum TerrainConnection { Grass, Sand, Rock }
    public enum EnvironmentAttribute { Temperature, Humidity, Recovery }
    public enum EffectPhase { None, Entering, Holding, Returning, Automatic }

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
        public string TemperatureDefinition => Temperature <= 40 ? "冷" : Temperature < 60 ? "中" : "热";
        public string HumidityDefinition => Humidity <= 40 ? "干" : Humidity < 60 ? "中" : "湿";
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
        public const int TransitionDays = 25, ClimateHoldDays = 150, ClimateReturnDays = 100;
        public const int RecoveryHoldDays = 50, RecoveryReturnDays = 50;
        public const int LandRecoveryTransitionDays = 25;
        public const int LandRecoveryHoldDays = 150;
        public const int LandRecoveryReturnDays = 100;
        public readonly struct HabitatProfile
        {
            public readonly string Title;
            public readonly TerrainKind Visual;
            public readonly TerrainDefaults Defaults;
            public HabitatProfile(string title, TerrainKind visual, int elevation,
                int temperature, int humidity, int recovery)
            {
                Title = title; Visual = visual;
                TerrainConnection connection = visual == TerrainKind.Desert ? TerrainConnection.Sand :
                    visual == TerrainKind.SnowMountain || visual == TerrainKind.Volcano
                        ? TerrainConnection.Rock : TerrainConnection.Grass;
                Defaults = new TerrainDefaults(elevation, temperature, humidity, recovery, connection);
            }
        }

        // Row major: elevation, temperature band, humidity band.
        private static readonly HabitatProfile[] habitats =
        {
            new HabitatProfile("干冷的荒漠", TerrainKind.Desert, 0, 35, 35, 50000),
            new HabitatProfile("寒冷的草原", TerrainKind.Grassland, 0, 35, 55, 70000),
            new HabitatProfile("阴冷的森林", TerrainKind.Forest, 0, 35, 75, 90000),
            new HabitatProfile("干燥的荒漠", TerrainKind.Desert, 0, 55, 35, 50000),
            new HabitatProfile("宜人的草原", TerrainKind.Grassland, 0, 55, 55, 100000),
            new HabitatProfile("湿润的雨林", TerrainKind.Rainforest, 0, 55, 75, 120000),
            new HabitatProfile("干热的荒漠", TerrainKind.Desert, 0, 75, 35, 50000),
            new HabitatProfile("炎热的草原", TerrainKind.Grassland, 0, 75, 55, 90000),
            new HabitatProfile("阴热的雨林", TerrainKind.Rainforest, 0, 75, 75, 130000),
            new HabitatProfile("干冷的苔原", TerrainKind.Tundra, 1, 30, 30, 60000),
            new HabitatProfile("寒冷的苔原", TerrainKind.Tundra, 1, 30, 50, 70000),
            new HabitatProfile("湿冷的森林", TerrainKind.Forest, 1, 30, 70, 80000),
            new HabitatProfile("干燥的山地", TerrainKind.Highland, 1, 50, 30, 60000),
            new HabitatProfile("稀疏的草原", TerrainKind.Grassland, 1, 50, 50, 80000),
            new HabitatProfile("湿润的森林", TerrainKind.Forest, 1, 50, 70, 100000),
            new HabitatProfile("炙热的荒漠", TerrainKind.Desert, 1, 70, 30, 40000),
            new HabitatProfile("炎热的山地", TerrainKind.Highland, 1, 70, 50, 80000),
            new HabitatProfile("湿热的雨林", TerrainKind.Rainforest, 1, 70, 70, 120000),
            new HabitatProfile("冰冻的苔原", TerrainKind.Tundra, 2, 25, 25, 50000),
            new HabitatProfile("寒冷的雪山", TerrainKind.SnowMountain, 2, 25, 45, 40000),
            new HabitatProfile("湿冷的雪山", TerrainKind.SnowMountain, 2, 25, 65, 40000),
            new HabitatProfile("干燥的苔原", TerrainKind.Tundra, 2, 45, 25, 60000),
            new HabitatProfile("宜人的山地", TerrainKind.Highland, 2, 45, 45, 90000),
            new HabitatProfile("湿润的苔原", TerrainKind.Tundra, 2, 45, 65, 70000),
            new HabitatProfile("干热的火山", TerrainKind.Volcano, 2, 65, 25, 40000),
            new HabitatProfile("炎热的火山", TerrainKind.Volcano, 2, 65, 45, 40000),
            new HabitatProfile("湿热的山地", TerrainKind.Highland, 2, 65, 65, 90000)
        };

        public static HabitatProfile Profile(HabitatState state)
        {
            int index = (int)state;
            if (index < 0 || index >= habitats.Length) throw new ArgumentOutOfRangeException(nameof(state));
            return habitats[index];
        }
        public static TerrainDefaults Defaults(HabitatState state) => Profile(state).Defaults;
        public static TerrainKind VisualTerrain(HabitatState state) => Profile(state).Visual;
        public static HabitatState ClassifyHabitat(int elevation, double temperature, double humidity)
        {
            if (elevation < 0 || elevation > 2 || !IsFinite(temperature) || !IsFinite(humidity) ||
                temperature < 0 || temperature > 100 || humidity < 0 || humidity > 100)
                throw new ArgumentOutOfRangeException("Environment values exceed their legal ranges.");
            int t = temperature <= 40 ? 0 : temperature < 60 ? 1 : 2;
            int h = humidity <= 40 ? 0 : humidity < 60 ? 1 : 2;
            return (HabitatState)(elevation * 9 + t * 3 + h);
        }
        public static HabitatState PlacementHabitat(TerrainKind kind)
        {
            switch (kind)
            {
                case TerrainKind.Desert: return HabitatState.Z0MiddleDry;
                case TerrainKind.Grassland: return HabitatState.Z0HotMiddle;
                case TerrainKind.Rainforest: return HabitatState.Z0HotWet;
                case TerrainKind.Forest: return HabitatState.Z1ColdWet;
                case TerrainKind.Highland: return HabitatState.Z1HotMiddle;
                case TerrainKind.Tundra: return HabitatState.Z2ColdDry;
                case TerrainKind.SnowMountain: return HabitatState.Z2ColdMiddle;
                case TerrainKind.Volcano: return HabitatState.Z2HotMiddle;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        // The eight visual presets only supply initial placement values.
        public static TerrainDefaults Defaults(TerrainKind kind)
            => Defaults(PlacementHabitat(kind));

        public static TerrainKind Classify(int elevation, int temperature, int humidity, int recovery)
        {
            if (elevation < 0 || elevation > 2 || temperature < 0 || temperature > 100 ||
                humidity < 0 || humidity > 100 || recovery < 0 || recovery > MaximumRecovery)
                throw new ArgumentOutOfRangeException("Environment values exceed their legal ranges.");
            return VisualTerrain(ClassifyHabitat(elevation, temperature, humidity));
        }

        public static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
        public static double Clamp(EnvironmentAttribute attribute, double value) => Math.Max(0,
            Math.Min(attribute == EnvironmentAttribute.Recovery ? MaximumRecovery : 100, value));
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static bool IsClimateStrength(double value) => IsFinite(value) && (Math.Abs(value) == 25 || Math.Abs(value) == 50);
        public static bool IsRecoveryStrength(double value) => IsFinite(value) && (Math.Abs(value) == 25000 || Math.Abs(value) == 50000);
        public static double ChangeStock(double stock, double signedFraction)
            => ChangeStock(stock, PlantCapacity, signedFraction);
        public static double ChangeStock(double stock, double capacity, double signedFraction)
        {
            if (!IsFinite(capacity) || capacity <= 0 || !IsFinite(stock) || stock < 0 || stock > capacity ||
                !IsFinite(signedFraction) || (Math.Abs(signedFraction) != .25 && Math.Abs(signedFraction) != .5))
                throw new ArgumentOutOfRangeException(nameof(signedFraction));
            return Math.Max(0, Math.Min(capacity, stock + signedFraction * (signedFraction > 0 ? capacity : stock)));
        }

        // The stock tool acts on the stock that exists when it is submitted.
        public static double MultiplyStock(double stock, double capacity, double signedFraction)
        {
            if (!IsFinite(stock) || !IsFinite(capacity) || capacity <= 0 || stock < 0 || stock > capacity ||
                !IsFinite(signedFraction) || (Math.Abs(signedFraction) != .25 && Math.Abs(signedFraction) != .5))
                throw new ArgumentOutOfRangeException(nameof(signedFraction));
            return Math.Max(0, Math.Min(capacity, stock * (1 + signedFraction)));
        }
    }
}
