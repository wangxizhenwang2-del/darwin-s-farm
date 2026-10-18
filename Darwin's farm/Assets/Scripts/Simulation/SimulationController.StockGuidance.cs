using UnityEngine;

public partial class SimulationController
{
    // The physical stock edit has already succeeded when this is called.
    // Same-day edits are settled once, from their first pre-edit stock to
    // their last post-edit stock, before the following day's mutation draw.
    internal void RecordActualStockGuidance(BlockInfo block, float before,
        float after, int tier, int day)
    {
        if (block == null || block.community == null ||
            block.waterCoverage != WaterCoverage.Land ||
            Mathf.Abs(after - before) < .0001f) return;
        StockGuidancePending pending = block.stockGuidancePending;
        if (pending == null)
            block.stockGuidancePending = pending = new StockGuidancePending();
        if (pending.populationIds == null)
            pending.populationIds = new System.Collections.Generic.List<string>();
        if (pending.active && pending.day != day)
            FinalizeStockGuidance(block, day);
        if (!pending.active)
        {
            pending.active = true;
            pending.day = day;
            pending.startStock = before;
            pending.tier = Mathf.Abs(tier);
            pending.populationIds.Clear();
        }
        pending.endStock = after;
        pending.tier = Mathf.Max(pending.tier, Mathf.Abs(tier));
        foreach (PopulationData population in block.community)
            if (population != null && population.speciesAmount > 0 &&
                population.trophicLevel == 0 &&
                population.ecologicalNiche == EcologicalNiche.Land &&
                !pending.populationIds.Contains(population.PopulationId))
                pending.populationIds.Add(population.PopulationId);
    }

    private void FinalizeStockGuidance(BlockInfo block, int day)
    {
        if (block == null || block.stockGuidancePending == null) return;
        StockGuidancePending pending = block.stockGuidancePending;
        if (!pending.active || pending.day >= day) return;
        pending.active = false;
        if (pending.populationIds == null)
            pending.populationIds = new System.Collections.Generic.List<string>();
        float delta = pending.endStock - pending.startStock;
        if (Mathf.Abs(delta) < .0001f) return;
        int direction = delta > 0f ? 1 : -1;
        StockGuidanceTransaction transaction = block.stockGuidance;
        if (transaction == null)
            block.stockGuidance = transaction = new StockGuidanceTransaction();
        bool newRound = transaction.direction != direction ||
            pending.day > transaction.lastDay;
        if (newRound)
        {
            transaction.serial++;
            transaction.key = StableTileKey(block) + ":stock:" +
                transaction.serial;
            transaction.direction = direction;
            transaction.firstDay = pending.day + 1;
            transaction.lastDay = pending.day +
                (pending.tier >= 2 ? 170 : 120);
            transaction.cap = pending.tier >= 2 ? 55 : 30;
            transaction.startStock = pending.startStock;
            transaction.totalDelta = delta;
            transaction.memory = 1f;
            transaction.memoryDay = pending.day;
            transaction.pmax = pending.tier >= 2 ? .98f : .85f;
            transaction.rho = pending.tier >= 2 ? .993f : .985f;
            if (transaction.claims == null)
                transaction.claims = new System.Collections.Generic.List<StockGuidanceClaim>();
            else transaction.claims.Clear();
        }
        else
        {
            transaction.totalDelta += delta;
            if (pending.tier >= 2)
            {
                transaction.cap = 55;
                transaction.pmax = .98f;
                transaction.rho = .993f;
            }
        }
        // Only populations present when a real button transaction occurred
        // are enrolled. A later immigrant does not collect an older command.
        foreach (PopulationData population in block.community)
        {
            if (population == null || population.speciesAmount <= 0 ||
                population.trophicLevel != 0 ||
                !pending.populationIds.Contains(population.PopulationId)) continue;
            StockGuidancePulse pulse = population.stockGuidance;
            if (pulse == null)
                population.stockGuidance = pulse = new StockGuidancePulse();
            if (pulse.transactionKey == transaction.key) continue;
            pulse.transactionKey = transaction.key;
            pulse.claimKey = population.PopulationId;
            pulse.used = 0;
            transaction.claims.Add(new StockGuidanceClaim
            {
                key = pulse.claimKey, used = 0
            });
        }
    }

    private StockGuidanceClaim StockClaim(BlockInfo block,
        PopulationData population)
    {
        StockGuidanceTransaction transaction = block.stockGuidance;
        StockGuidancePulse pulse = population.stockGuidance;
        if (transaction == null || pulse == null ||
            pulse.transactionKey != transaction.key ||
            transaction.claims == null) return null;
        foreach (StockGuidanceClaim claim in transaction.claims)
            if (claim != null && claim.key == pulse.claimKey)
            {
                claim.used = Mathf.Max(claim.used, pulse.used);
                pulse.used = claim.used;
                return claim;
            }
        return null;
    }

    private float StockGuidanceProbability(BlockInfo block,
        PopulationData population, int day)
    {
        FinalizeStockGuidance(block, day);
        if (block == null || population == null ||
            population.trophicLevel != 0 || population.speciesAmount <= 0)
            return 0f;
        StockGuidanceTransaction transaction = block.stockGuidance;
        if (transaction == null || transaction.direction == 0 ||
            day < transaction.firstDay || day > transaction.lastDay ||
            StockClaim(block, population) == null ||
            population.stockGuidance.used >= transaction.cap ||
            TraitLimit(3, population.size + transaction.direction) ==
                population.size) return 0f;
        int elapsed = Mathf.Max(0, day - transaction.memoryDay);
        if (elapsed > 0)
        {
            float real = Mathf.Clamp01(transaction.direction *
                (block.plantBiomass - transaction.startStock) /
                Mathf.Max(.0001f, Mathf.Abs(transaction.totalDelta)));
            transaction.memory = Mathf.Max(real, transaction.memory *
                Mathf.Pow(transaction.rho, elapsed));
            transaction.memoryDay = day;
        }
        int transactionDay = transaction.firstDay - 1;
        return transaction.pmax * transaction.memory *
            Mathf.Max(0f, 1f - (day - transactionDay) /
                (float)(transaction.lastDay - transactionDay));
    }
}
