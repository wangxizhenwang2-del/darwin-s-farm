using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DarwinFarm.Environment
{
    public sealed class TopologyNode
    {
        public GridPosition Position { get; }
        public int Elevation { get; }
        public TerrainKind InitialTerrain { get; }
        public bool IsWater { get; }
        public IReadOnlyList<GridPosition> Neighbors { get; }
        public TopologyNode(GridPosition position, int elevation, TerrainKind initialTerrain, IEnumerable<GridPosition> neighbors,
            bool isWater = false)
        {
            if (elevation < 0 || elevation > 2) throw new ArgumentOutOfRangeException(nameof(elevation));
            EnvironmentRules.Defaults(initialTerrain); // Validate before this node can mutate a world.
            Position = position; Elevation = elevation; InitialTerrain = initialTerrain; IsWater = isWater;
            var list = new List<GridPosition>(neighbors ?? Array.Empty<GridPosition>());
            list.Sort(); Neighbors = list.AsReadOnly();
        }
    }

    public readonly struct AttributeSnapshot
    {
        public double Value { get; }
        public double Target { get; }
        public EffectPhase Phase { get; }
        public int RemainingDays { get; }
        public double Progress { get; }
        // Presentation metadata stays with the applied effect, including its return phase.
        public int ToolTier { get; }
        internal AttributeSnapshot(double value, double target, EffectPhase phase, int remaining, double progress,
            int toolTier = 0)
        { Value = value; Target = target; Phase = phase; RemainingDays = remaining; Progress = progress; ToolTier = toolTier; }
    }

    public sealed class EnvironmentSnapshot
    {
        public GridPosition Position { get; }
        public int Elevation { get; }
        public TerrainKind Terrain { get; }
        public HabitatState Habitat { get; }
        public string HabitatTitle => EnvironmentRules.Profile(Habitat).Title;
        public bool IsWater { get; }
        public TerrainDefaults Defaults => EnvironmentRules.Defaults(Habitat);
        public TerrainDefaults NaturalDefaults { get; }
        public AttributeSnapshot Temperature { get; }
        public AttributeSnapshot Humidity { get; }
        public AttributeSnapshot Recovery { get; }
        public bool HasLocalClimate { get; }
        public bool HasLocalTemperature { get; }
        public bool HasLocalHumidity { get; }
        public bool HasRecoveryCommand { get; }
        public bool CanCancelRecovery { get; }
        internal EnvironmentSnapshot(EnvironmentTile tile)
        {
            Position = tile.Position; Elevation = tile.Elevation; Terrain = tile.Terrain;
            Habitat = tile.Habitat; IsWater = tile.IsWater;
            NaturalDefaults = EnvironmentRules.Defaults(tile.Habitat);
            Temperature = tile.Attribute(0); Humidity = tile.Attribute(1); Recovery = tile.Attribute(2);
            HasLocalClimate = tile.HasLocalClimate;
            HasLocalTemperature = tile.Locals[0] != null;
            HasLocalHumidity = tile.Locals[1] != null;
            HasRecoveryCommand = tile.RecoveryReturning;
            CanCancelRecovery = tile.RecoveryReturning &&
                tile.RecoveryAge < tile.RecoveryHoldThreshold &&
                !tile.RecoveryCancelIssued;
        }
    }

    internal sealed class Transition
    {
        internal double Start, Target;
        internal int Age, Duration;
        internal EffectPhase Phase;
        internal bool Active => Age < Duration;
        internal double Progress => Duration == 0 ? 1 : Math.Min(1, (double)Age / Duration);
        internal void Begin(double start, double target, int duration, EffectPhase phase)
        { Start = start; Target = target; Age = 0; Duration = duration; Phase = phase; }
        internal double Tick()
        { if (Active) Age++; return Start + (Target - Start) * Progress; }
        internal void Stop(double value) { Begin(value, value, 0, EffectPhase.None); }
    }

    internal sealed class LocalClimateEffect
    {
        internal int Age;
        internal int Tier;
        internal long Sequence;
        internal double Baseline;
        internal HabitatState HabitatAtSubmit;
        internal bool Active => Age < EnvironmentRules.TransitionDays + EnvironmentRules.ClimateHoldDays;
        internal EffectPhase Phase => Age < EnvironmentRules.TransitionDays ? EffectPhase.Entering :
            Active ? EffectPhase.Holding : EffectPhase.Returning;
    }

    internal sealed class EnvironmentTile
    {
        internal readonly GridPosition Position;
        internal int Elevation;
        internal bool IsWater;
        internal TerrainKind Terrain;
        internal HabitatState Habitat;
        internal readonly double[] Values = new double[3];
        internal readonly Transition[] Tracks = { new Transition(), new Transition(), new Transition() };
        internal readonly LocalClimateEffect[] Locals = new LocalClimateEffect[2];
        internal long RecoverySequence;
        internal int RecoveryAge;
        internal int RecoveryTier;
        internal bool RecoveryReturning;
        internal bool RecoveryCancelIssued;
        internal bool RecoveryDetached;
        internal double RecoveryBaseline;
        internal HabitatState RecoveryHabitatAtSubmit;
        internal int RecoveryHoldThreshold => IsWater ?
            EnvironmentRules.RecoveryHoldDays :
            EnvironmentRules.LandRecoveryTransitionDays +
                EnvironmentRules.LandRecoveryHoldDays;
        internal int RecoveryReturnDuration => IsWater ?
            EnvironmentRules.RecoveryReturnDays :
            EnvironmentRules.LandRecoveryReturnDays;
        internal bool HasLocalClimate => Locals[0] != null || Locals[1] != null;
        internal EnvironmentTile(TopologyNode node)
        {
            Position = node.Position; Elevation = node.Elevation; IsWater = node.IsWater;
            var defaults = EnvironmentRules.Defaults(node.InitialTerrain);
            Habitat = EnvironmentRules.ClassifyHabitat(Elevation, defaults.Temperature, defaults.Humidity);
            Terrain = EnvironmentRules.VisualTerrain(Habitat);
            defaults = EnvironmentRules.Defaults(Habitat);
            Values[0] = defaults.Temperature; Values[1] = defaults.Humidity;
            Values[2] = IsWater ? EnvironmentRules.WaterDefaultRecovery : defaults.Recovery;
            for (int i = 0; i < 3; i++) Tracks[i].Stop(Values[i]);
        }
        internal AttributeSnapshot Attribute(int index)
        {
            var track = Tracks[index]; var local = index < 2 ? Locals[index] : null;
            var phase = local != null ? local.Phase :
                index == 2 && RecoveryReturning && !IsWater &&
                    RecoveryAge < EnvironmentRules.LandRecoveryTransitionDays
                    ? EffectPhase.Entering :
                index == 2 && RecoveryReturning && RecoveryAge < RecoveryHoldThreshold
                    ? EffectPhase.Holding : track.Active ? track.Phase : EffectPhase.None;
            int remaining = local != null && local.Phase == EffectPhase.Holding ?
                EnvironmentRules.TransitionDays + EnvironmentRules.ClimateHoldDays - local.Age :
                index == 2 && RecoveryReturning && !IsWater &&
                    RecoveryAge < EnvironmentRules.LandRecoveryTransitionDays ?
                    EnvironmentRules.LandRecoveryTransitionDays - RecoveryAge :
                index == 2 && RecoveryReturning && RecoveryAge < RecoveryHoldThreshold ?
                    RecoveryHoldThreshold - RecoveryAge : Math.Max(0, track.Duration - track.Age);
            double target = index == 2 && RecoveryReturning &&
                RecoveryAge >= EnvironmentRules.LandRecoveryTransitionDays &&
                RecoveryAge < RecoveryHoldThreshold ? Values[2] : track.Target;
            return new AttributeSnapshot(EnvironmentRules.Clamp((EnvironmentAttribute)index, Values[index]),
                EnvironmentRules.Clamp((EnvironmentAttribute)index, target),
                phase, remaining, track.Progress, local != null ? local.Tier :
                    index == 2 && RecoveryReturning ? RecoveryTier : 0);
        }
    }

}
