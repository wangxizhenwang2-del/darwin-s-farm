using System;

// Pure V6.1 movement math. The controller supplies the measured sustainable
// opportunity A and adds local survival changes exactly once.
internal static class LandMovementOpportunity
{
    internal const float Lambda = 2.5f;

    internal static float BonusPlus(int movement, float opportunity) =>
        movement >= 100 ? 0f : Lambda * Math.Max(0f, opportunity) / 100f *
            (1f - movement / 100f);

    internal static float BonusMinus(int movement, float opportunity) =>
        movement <= 0 ? 0f : -Lambda * Math.Max(0f, opportunity) / 100f *
            (1f - (movement - 1f) / 100f);

    internal static float AnalyticPlus(int movement, float opportunity) =>
        BonusPlus(movement, opportunity) - 0.4f *
            (2f * movement + 1f) / 10000f;

    internal static int StandardMigrationAmount(int population)
    {
        if (population < 95) return 0;
        return population >= 1000 ? 100 : (population + 5) / 10;
    }
}
