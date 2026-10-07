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
        public IReadOnlyList<GridPosition> Neighbors { get; }
        public TopologyNode(GridPosition position, int elevation, TerrainKind initialTerrain, IEnumerable<GridPosition> neighbors)
        {
            if (elevation < 0 || elevation > 2) throw new ArgumentOutOfRangeException(nameof(elevation));
            EnvironmentRules.Defaults(initialTerrain); // Validate before this node can mutate a world.
            Position = position; Elevation = elevation; InitialTerrain = initialTerrain;
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
        internal AttributeSnapshot(double value, double target, EffectPhase phase, int remaining, double progress)
        { Value = value; Target = target; Phase = phase; RemainingDays = remaining; Progress = progress; }
    }

    public sealed class EnvironmentSnapshot
    {
        public GridPosition Position { get; }
        public int Elevation { get; }
        public TerrainKind Terrain { get; }
        public TerrainDefaults Defaults => EnvironmentRules.Defaults(Terrain);
        public AttributeSnapshot Temperature { get; }
        public AttributeSnapshot Humidity { get; }
        public AttributeSnapshot Recovery { get; }
        public long? MonsoonId { get; }
        public bool HasMonsoon => MonsoonId.HasValue;
        public bool MonsoonApplied { get; }
        public bool HasLocalClimate { get; }
        internal EnvironmentSnapshot(EnvironmentTile tile)
        {
            Position = tile.Position; Elevation = tile.Elevation; Terrain = tile.Terrain;
            Temperature = tile.Attribute(0); Humidity = tile.Attribute(1); Recovery = tile.Attribute(2);
            MonsoonId = tile.MonsoonId; HasLocalClimate = tile.HasLocalClimate;
            MonsoonApplied = HasMonsoon && !HasLocalClimate;
        }
    }

    public sealed class MonsoonSnapshot
    {
        public long Id { get; }
        public GridPosition Source { get; }
        public int TemperatureOffset { get; }
        public int HumidityOffset { get; }
        public IReadOnlyList<GridPosition> Area { get; }
        internal MonsoonSnapshot(MonsoonState state)
        {
            Id = state.Id; Source = state.Source;
            TemperatureOffset = state.TemperatureOffset; HumidityOffset = state.HumidityOffset;
            var area = new List<GridPosition>(state.Area); area.Sort(); Area = area.AsReadOnly();
        }
    }

    // Reserved settlement contract: no balances, prices, deductions or refund execution here.
    public sealed class EnvironmentPayment
    {
        public string ReceiptId { get; }
        public decimal PaidAmount { get; }
        public EnvironmentPayment(string receiptId, decimal paidAmount)
        {
            if (string.IsNullOrWhiteSpace(receiptId)) throw new ArgumentException("A payment receipt needs an identity.", nameof(receiptId));
            if (paidAmount < 0) throw new ArgumentOutOfRangeException(nameof(paidAmount));
            ReceiptId = receiptId; PaidAmount = paidAmount;
        }
    }

    public sealed class MonsoonSettlement
    {
        public long MonsoonId { get; }
        public MonsoonExitReason Reason { get; }
        public EnvironmentPayment Payment { get; }
        public decimal RefundAmount => Reason == MonsoonExitReason.Overlap ? Payment?.PaidAmount ?? 0 : 0;
        internal MonsoonSettlement(MonsoonState state, MonsoonExitReason reason)
        { MonsoonId = state.Id; Reason = reason; Payment = state.Payment; }
    }

    // A future economy adapter consumes idempotent settlements, keyed by MonsoonId/ReceiptId.
    public interface IEnvironmentEconomySink { void OnMonsoonEnded(MonsoonSettlement settlement); }

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
        internal EffectPhase Phase => Age < 25 ? EffectPhase.Entering : Age < 125 ? EffectPhase.Holding : EffectPhase.Returning;
    }

    internal sealed class EnvironmentTile
    {
        internal readonly GridPosition Position;
        internal int Elevation;
        internal TerrainKind Terrain;
        internal readonly double[] Values = new double[3];
        internal readonly Transition[] Tracks = { new Transition(), new Transition(), new Transition() };
        internal readonly LocalClimateEffect[] Locals = new LocalClimateEffect[2];
        internal bool RecoveryReturning;
        internal long? MonsoonId;
        internal readonly double[] WindTargets = new double[2];
        internal bool HasLocalClimate => Locals[0] != null || Locals[1] != null;
        internal EnvironmentTile(TopologyNode node)
        {
            Position = node.Position; Elevation = node.Elevation;
            var defaults = EnvironmentRules.Defaults(node.InitialTerrain);
            Terrain = EnvironmentRules.Classify(Elevation, defaults.Temperature, defaults.Humidity, defaults.Recovery);
            defaults = EnvironmentRules.Defaults(Terrain);
            Values[0] = defaults.Temperature; Values[1] = defaults.Humidity; Values[2] = defaults.Recovery;
            for (int i = 0; i < 3; i++) Tracks[i].Stop(Values[i]);
        }
        internal AttributeSnapshot Attribute(int index)
        {
            var track = Tracks[index]; var local = index < 2 ? Locals[index] : null;
            var phase = local != null ? local.Phase : track.Active ? track.Phase : EffectPhase.None;
            int remaining = local != null && local.Phase == EffectPhase.Holding ? 125 - local.Age : Math.Max(0, track.Duration - track.Age);
            return new AttributeSnapshot(Values[index], track.Target, phase, remaining, track.Progress);
        }
    }

    internal sealed class MonsoonState
    {
        internal long Id;
        internal GridPosition Source;
        internal int TemperatureOffset, HumidityOffset;
        internal EnvironmentPayment Payment;
        internal HashSet<GridPosition> Area = new HashSet<GridPosition>();
    }
}
