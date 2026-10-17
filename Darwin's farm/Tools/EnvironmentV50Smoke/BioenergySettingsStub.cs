internal sealed class BioenergySettings
{
    internal static BioenergySettings Current { get; } = new BioenergySettings();
    internal int climateSmallCost = 220, climateLargeCost = 480;
    internal int plantIncreaseSmallCost = 260, plantIncreaseLargeCost = 560;
    internal int plantDecreaseSmallCost = 420, plantDecreaseLargeCost = 960;
    internal int elevationCost = 500;
}
