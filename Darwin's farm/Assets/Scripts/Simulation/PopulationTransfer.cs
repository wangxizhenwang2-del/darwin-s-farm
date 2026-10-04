using UnityEngine;

// 种群搬家时的身份、复制与合并规则。这里不决定何时迁徙。
internal static class PopulationTransfer
{
    public static PopulationData CopyForMove(PopulationData source, int amount)
    {
        // 不复制河岸位置；迁入者落在新栖息地，留下的个体仍在原河岸。
        return new PopulationData
        {
            species = source.species,
            lineageName = source.lineageName,
            speciesId = source.speciesId,
            ecologicalNiche = source.ecologicalNiche,
            speciesAmount = amount,
            movementAbility = source.movementAbility,
            habitatNiche = source.habitatNiche,
            fitTemperature = source.fitTemperature,
            fitHumidity = source.fitHumidity,
            size = source.size,
            fertility = source.fertility,
            trophicLevel = source.trophicLevel,
            trophicLevelInitialized = true,
            temperatureMutationRemainder = source.temperatureMutationRemainder,
            humidityMutationRemainder = source.humidityMutationRemainder,
            movementMutationRemainder = source.movementMutationRemainder,
            sizeMutationRemainder = source.sizeMutationRemainder,
            fertilityMutationRemainder = source.fertilityMutationRemainder,
            trophicMutationRemainder = source.trophicMutationRemainder,
            mutationDaysElapsed = source.mutationDaysElapsed,
            nextMigrationDay = source.nextMigrationDay,
            previousMutationPopulation = amount,
            mutationPopulationInitialized = true
        };
    }

    // 迁徙和生态位分化都从这里拆分个体，储备与不足一个体的小数也按比例带走。
    public static PopulationData Split(PopulationData origin, int amount,
        BlockInfo source, int day, int cooldownDays)
    {
        float share = amount / (float)origin.speciesAmount;
        PopulationData branch = CopyForMove(origin, amount);
        branch.lastMigrationSource = source;
        origin.nextMigrationDay = Mathf.Max(origin.nextMigrationDay,
            day + cooldownDays + 1);
        branch.nextMigrationDay = origin.nextMigrationDay;
        branch.energyReserve = origin.energyReserve * share;
        branch.birthRemainder = origin.birthRemainder * share;
        branch.deathRemainder = origin.deathRemainder * share;
        origin.energyReserve -= branch.energyReserve;
        origin.birthRemainder -= branch.birthRemainder;
        origin.deathRemainder -= branch.deathRemainder;
        origin.speciesAmount -= amount;
        return branch;
    }

    public static void Merge(PopulationData resident, PopulationData arrival)
    {
        int residentAmount = resident.speciesAmount;
        int arrivalAmount = arrival.speciesAmount;
        int total = residentAmount + arrivalAmount;
        MergeTrait(ref resident.movementAbility, ref resident.movementMutationRemainder,
            arrival.movementAbility, arrival.movementMutationRemainder,
            residentAmount, arrivalAmount, total);
        resident.habitatNiche = WeightedTrait(resident.habitatNiche, residentAmount,
            arrival.habitatNiche, arrivalAmount, total);
        MergeTrait(ref resident.fitTemperature, ref resident.temperatureMutationRemainder,
            arrival.fitTemperature, arrival.temperatureMutationRemainder,
            residentAmount, arrivalAmount, total);
        MergeTrait(ref resident.fitHumidity, ref resident.humidityMutationRemainder,
            arrival.fitHumidity, arrival.humidityMutationRemainder,
            residentAmount, arrivalAmount, total);
        MergeTrait(ref resident.size, ref resident.sizeMutationRemainder,
            arrival.size, arrival.sizeMutationRemainder,
            residentAmount, arrivalAmount, total);
        MergeTrait(ref resident.fertility, ref resident.fertilityMutationRemainder,
            arrival.fertility, arrival.fertilityMutationRemainder,
            residentAmount, arrivalAmount, total);
        MergeTrait(ref resident.trophicLevel, ref resident.trophicMutationRemainder,
            arrival.trophicLevel, arrival.trophicMutationRemainder,
            residentAmount, arrivalAmount, total);
        resident.energyReserve += arrival.energyReserve;
        resident.birthRemainder += arrival.birthRemainder;
        resident.deathRemainder += arrival.deathRemainder;
        resident.nextMigrationDay = Mathf.Max(resident.nextMigrationDay,
            arrival.nextMigrationDay);
        resident.lastMigrationSource = arrival.lastMigrationSource;
        resident.speciesAmount = total;
        resident.previousMutationPopulation = total;
        resident.mutationPopulationInitialized = true;
        resident.trophicLevelInitialized = true;
    }

    private static int WeightedTrait(int resident, int residentAmount, int arrival,
        int arrivalAmount, int total)
    {
        return Mathf.RoundToInt(WeightedValue(resident, residentAmount,
            arrival, arrivalAmount, total));
    }

    private static float WeightedValue(float resident, int residentAmount, float arrival,
        int arrivalAmount, int total)
    {
        return (resident * residentAmount + arrival * arrivalAmount) / total;
    }

    private static void MergeTrait(ref int resident, ref float remainder,
        int arrival, float arrivalRemainder, int residentAmount, int arrivalAmount, int total)
    {
        float average = WeightedValue(resident + remainder, residentAmount,
            arrival + arrivalRemainder, arrivalAmount, total);
        resident = Mathf.RoundToInt(average);
        remainder = average - resident;
    }

    public static bool IsSameSpecies(PopulationData left, PopulationData right)
    {
        if (left == null || right == null) return false;
        if (!string.IsNullOrEmpty(left.speciesId) || !string.IsNullOrEmpty(right.speciesId))
            return !string.IsNullOrEmpty(left.speciesId) &&
                string.Equals(left.speciesId, right.speciesId,
                    System.StringComparison.Ordinal);
        if (left.species != null || right.species != null)
            return left.species != null && left.species == right.species;
        return !string.IsNullOrWhiteSpace(left.lineageName) &&
            string.Equals(left.lineageName, right.lineageName,
                System.StringComparison.Ordinal);
    }
}
