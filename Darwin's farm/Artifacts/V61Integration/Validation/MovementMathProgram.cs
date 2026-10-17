using System;

internal static class MovementMathProgram
{
    private static void Check(bool yes, string name)
    { if (!yes) throw new Exception(name); Console.WriteLine("PASS " + name); }
    private static int EndPoint(float advantage, float coefficient)
    {
        int m = 10;
        while (m < 100 && LandMovementOpportunity.AnalyticPlus(
            m, advantage * coefficient) > 0f) m++;
        return m;
    }
    private static void Main()
    {
        int[] sizes = { 11, 50, 90, 94, 95, 99, 100, 105, 1000, 1001 };
        int[] expected = { 0, 0, 0, 0, 10, 10, 10, 11, 100, 100 };
        for (int i = 0; i < sizes.Length; i++)
            Check(LandMovementOpportunity.StandardMigrationAmount(sizes[i]) ==
                expected[i], "migration N=" + sizes[i]);
        float[] advantages = { 0f, .1f, .3f, .5f };
        int[,] endpoints = { {10,10,10}, {24,14,10}, {49,32,10}, {61,44,10} };
        float[] coefficients = { 1f, .5f, 0f };
        for (int i = 0; i < advantages.Length; i++)
            for (int j = 0; j < coefficients.Length; j++)
                Check(EndPoint(advantages[i], coefficients[j]) == endpoints[i,j],
                    "analytic endpoint " + advantages[i] + " / " + coefficients[j]);
        for (int m = 0; m < 99; m++)
        {
            Check(LandMovementOpportunity.BonusPlus(m, .3f) >=
                LandMovementOpportunity.BonusPlus(m + 1, .3f),
                "diminishing bonus m=" + m);
            Check(LandMovementOpportunity.BonusPlus(m, .3f) >=
                LandMovementOpportunity.BonusPlus(m, .15f),
                "advantage monotonic m=" + m);
        }
        Check(LandMovementOpportunity.BonusPlus(100, .5f) == 0f,
            "movement upper boundary");
        Check(!LandMovementOpportunity.AcceptNaturalOpportunity(.005f, .1f),
            "high density opportunity can miss its mutation interval");
        Check(LandMovementOpportunity.AcceptNaturalOpportunity(.5f, .1f),
            "low density opportunity can enter the shared slot");
        Check(!LandMovementOpportunity.CanOfferNeighborSlot(5, false),
            "five distinct residents block a new species opportunity");
        Check(LandMovementOpportunity.CanOfferNeighborSlot(5, true),
            "five residents still allow same species migration merge");
    }
}
