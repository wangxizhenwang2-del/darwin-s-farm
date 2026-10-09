// Immutable facts captured after an ecological population change is committed.
// Block references identify the locations; all counts and IDs are snapshots.
public enum PopulationTransitionKind
{
    Migration,
    NicheConversion,
    PresetEvolution
}

public enum PopulationArrivalOutcome
{
    Created,
    Merged
}

public readonly struct PopulationTransitionResult
{
    public PopulationTransitionKind Kind { get; }
    // Created means no matching target resident existed. Merged means the
    // destination resident survived and ResultPopulationId is that resident's ID.
    public PopulationArrivalOutcome Outcome { get; }
    public int Day { get; }
    public BlockInfo SourceBlock { get; }
    public BlockInfo TargetBlock { get; }
    public string SourcePopulationId { get; }
    // A whole move to an empty tile may keep the source ID; a partial split
    // creates a new ID. Merging always reports the destination resident's ID.
    public string ResultPopulationId { get; }
    public int Amount { get; }
    public int SourceCountBefore { get; }
    public int SourceCountAfter { get; }
    public int TargetCountBefore { get; }
    public int TargetCountAfter { get; }
    public EcologicalNiche ResultNiche { get; }
    public string ResultSpeciesId { get; }
    public SpeciesData TargetSpecies { get; }

    internal PopulationTransitionResult(PopulationTransitionKind kind, int day,
        BlockInfo sourceBlock, BlockInfo targetBlock, PopulationData sourcePopulation,
        PopulationData resultPopulation, int amount, int sourceCountBefore,
        int targetCountBefore, PopulationArrivalOutcome outcome,
        SpeciesData targetSpecies = null)
    {
        Kind = kind;
        Outcome = outcome;
        Day = day;
        SourceBlock = sourceBlock;
        TargetBlock = targetBlock;
        SourcePopulationId = sourcePopulation.PopulationId;
        ResultPopulationId = resultPopulation.PopulationId;
        Amount = amount;
        SourceCountBefore = sourceCountBefore;
        SourceCountAfter = sourcePopulation.speciesAmount;
        TargetCountBefore = targetCountBefore;
        TargetCountAfter = resultPopulation.speciesAmount;
        ResultNiche = resultPopulation.ecologicalNiche;
        ResultSpeciesId = resultPopulation.speciesId;
        TargetSpecies = targetSpecies;
    }
}
