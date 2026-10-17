using System;
using System.Collections.Generic;

namespace DarwinFarm.Environment
{
    // Model: owns timelines and graph rules, but never writes Unity objects or money.
    public sealed class EnvironmentWorld
    {
        private readonly Dictionary<GridPosition, EnvironmentTile> tiles = new Dictionary<GridPosition, EnvironmentTile>();
        private long nextCommandSequence = 1;
        public int CurrentDay { get; private set; }
        public EnvironmentWorld(int currentDay = 0) { CurrentDay = currentDay; }

        public bool TryRead(GridPosition position, out EnvironmentSnapshot snapshot)
        {
            snapshot = tiles.TryGetValue(position, out EnvironmentTile tile) ? new EnvironmentSnapshot(tile) : null;
            return snapshot != null;
        }
        public IReadOnlyList<EnvironmentSnapshot> ReadAll()
        {
            var positions = new List<GridPosition>(tiles.Keys); positions.Sort();
            var result = new List<EnvironmentSnapshot>(positions.Count);
            foreach (var position in positions) result.Add(new EnvironmentSnapshot(tiles[position]));
            return result.AsReadOnly();
        }
        public void SynchronizeTopology(IEnumerable<TopologyNode> nodes)
        {
            var next = new Dictionary<GridPosition, TopologyNode>();
            foreach (TopologyNode node in nodes)
            {
                if (node == null || next.ContainsKey(node.Position)) throw new ArgumentException("Duplicate or missing topology node.");
                next.Add(node.Position, node);
            }
            // Fully validate before mutation. Graph edges are physical four-neighbor edges.
            foreach (var node in next.Values)
            {
                var unique = new HashSet<GridPosition>();
                foreach (var neighbor in node.Neighbors)
                {
                    if (!unique.Add(neighbor) || !next.TryGetValue(neighbor, out TopologyNode other) ||
                        Math.Abs((long)neighbor.X - node.Position.X) + Math.Abs((long)neighbor.Y - node.Position.Y) != 1 ||
                        !Contains(other.Neighbors, node.Position)) throw new ArgumentException("Topology must have symmetric four-neighbor edges.");
                }
            }
            var removed = new List<GridPosition>();
            foreach (var position in tiles.Keys) if (!next.ContainsKey(position)) removed.Add(position);
            foreach (var position in removed) tiles.Remove(position);
            foreach (var node in next.Values)
            {
                if (!tiles.TryGetValue(node.Position, out EnvironmentTile tile)) tiles.Add(node.Position, new EnvironmentTile(node));
                else if (tile.Elevation != node.Elevation || tile.IsWater != node.IsWater)
                {
                    tile.Elevation = node.Elevation; tile.IsWater = node.IsWater;
                    Reclassify(tile);
                }
            }
        }

        public bool TrySetClimate(GridPosition position, EnvironmentAttribute attribute, double amount, out string error)
        {
            error = null;
            if (attribute != EnvironmentAttribute.Temperature && attribute != EnvironmentAttribute.Humidity ||
                !EnvironmentRules.IsClimateStrength(amount)) { error = "单格温湿度强度必须为 ±25 或 ±50"; return false; }
            if (!tiles.TryGetValue(position, out EnvironmentTile tile) || tile.IsWater)
            { error = "温湿度科技只适用于陆格"; return false; }
            int index = (int)attribute;
            double target = EnvironmentRules.Clamp(attribute, EnvironmentRules.Defaults(tile.Habitat).Get(attribute) + amount);
            if (Math.Abs(target - tile.Values[index]) < .000001)
            { error = "环境数值未发生有效变化"; return false; }
            tile.Locals[index] = new LocalClimateEffect {
                Sequence = nextCommandSequence++, Baseline = tile.Values[index],
                HabitatAtSubmit = tile.Habitat };
            tile.Tracks[index].Begin(tile.Values[index], target, 25, EffectPhase.Entering);
            EnforceSlots(tile);
            return true;
        }

        public bool TryCancelClimate(GridPosition position, EnvironmentAttribute attribute, out string error)
        {
            error = null;
            if (attribute != EnvironmentAttribute.Temperature && attribute != EnvironmentAttribute.Humidity ||
                !tiles.TryGetValue(position, out EnvironmentTile tile) || tile.Locals[(int)attribute] == null)
            { error = "该地块没有对应单格操作"; return false; }
            int index = (int)attribute;
            if (!tile.Locals[index].Active)
                return true; // A repeated cancellation must not restart return.
            tile.Locals[index].Age = EnvironmentRules.TransitionDays + EnvironmentRules.ClimateHoldDays;
            StartLocalReturn(tile, index);
            return true;
        }

        public bool TrySetRecovery(GridPosition position, double amount, out string error)
            => TrySetRecoveryBatch(new[] { position }, amount, false, out error);

        // Validate the complete water body before any member changes. Reapplying
        // the plant tool adds to the current recovery and restarts its timeline.
        public bool TrySetRecoveryBatch(IReadOnlyList<GridPosition> positions,
            double amount, bool suppress, out string error)
        {
            error = null;
            if (positions == null || positions.Count == 0 ||
                (suppress ? amount != .25 && amount != .5 : !EnvironmentRules.IsRecoveryStrength(amount)))
            { error = suppress ? "生态抑制比例必须为 25% 或 50%" : "增量强度必须为 ±25000 或 ±50000"; return false; }
            var members = new List<EnvironmentTile>(positions.Count);
            var unique = new HashSet<GridPosition>();
            foreach (GridPosition position in positions)
            {
                if (!unique.Add(position) || !tiles.TryGetValue(position, out EnvironmentTile tile))
                { error = "目标水域含重复或不存在的地块"; return false; }
                members.Add(tile);
            }
            foreach (EnvironmentTile tile in members)
            {
                double next = tile.Values[2] + (suppress ? -amount * 100000 : amount);
                next = EnvironmentRules.Clamp(EnvironmentAttribute.Recovery, next);
                if (Math.Abs(next - tile.Values[2]) < .000001)
                { error = "环境数值未发生有效变化"; return false; }
            }
            foreach (EnvironmentTile tile in members)
            {
                double next = EnvironmentRules.Clamp(EnvironmentAttribute.Recovery,
                    tile.Values[2] + (suppress ? -amount * 100000 : amount));
                tile.RecoveryBaseline = tile.Values[2];
                tile.RecoveryHabitatAtSubmit = tile.Habitat;
                if (tile.IsWater)
                {
                    tile.Values[2] = next;
                    tile.Tracks[2].Stop(next);
                }
                else
                    tile.Tracks[2].Begin(tile.Values[2], next,
                        EnvironmentRules.LandRecoveryTransitionDays,
                        EffectPhase.Entering);
                tile.RecoveryReturning = true;
                tile.RecoveryAge = 0;
                tile.RecoverySequence = nextCommandSequence++;
                tile.RecoveryDetached = false;
                tile.RecoveryCancelIssued = false;
                Reclassify(tile);
                EnforceSlots(tile);
            }
            return true;
        }

        public bool TryCancelRecovery(GridPosition position, out string error)
            => TryCancelRecoveryBatch(new[] { position }, out error);

        public bool TryCancelRecoveryBatch(IReadOnlyList<GridPosition> positions, out string error)
        {
            error = null;
            if (positions == null || positions.Count == 0)
            { error = "目标水域为空"; return false; }
            var members = new List<EnvironmentTile>(positions.Count);
            var unique = new HashSet<GridPosition>();
            foreach (GridPosition position in positions)
            {
                if (!unique.Add(position) || !tiles.TryGetValue(position, out EnvironmentTile tile) ||
                    !tile.RecoveryReturning)
                { error = "目标地块没有可统一撤销的植被增量操作"; return false; }
                members.Add(tile);
            }
            bool allReturning = true;
            foreach (EnvironmentTile tile in members)
                if (tile.RecoveryAge < tile.RecoveryHoldThreshold)
                    allReturning = false;
            if (allReturning) return true;
            foreach (EnvironmentTile tile in members)
                if (tile.RecoveryAge >= tile.RecoveryHoldThreshold)
                { error = "水域操作阶段不一致"; return false; }
            foreach (EnvironmentTile tile in members)
            {
                tile.RecoveryCancelIssued = true;
                tile.RecoveryAge = tile.RecoveryHoldThreshold;
                StartRecoveryReturn(tile);
            }
            return true;
        }

        public void DetachRecovery(GridPosition position)
        {
            if (!tiles.TryGetValue(position, out EnvironmentTile tile) || !tile.IsWater) return;
            tile.RecoveryReturning = false;
            tile.RecoveryAge = 0;
            tile.RecoveryCancelIssued = false;
            tile.RecoveryDetached = true;
            tile.Tracks[2].Stop(tile.Values[2]);
        }
        public void ReconcileWaterRecovery(IReadOnlyList<GridPosition> positions)
        {
            if (positions == null || positions.Count == 0) return;
            double total = 0;
            foreach (GridPosition position in positions)
                if (tiles.TryGetValue(position, out EnvironmentTile tile) && tile.IsWater)
                    total += tile.Values[2];
                else return;
            double average = EnvironmentRules.Clamp(EnvironmentAttribute.Recovery,
                total / positions.Count);
            foreach (GridPosition position in positions)
            {
                EnvironmentTile tile = tiles[position];
                tile.Values[2] = average;
                DetachRecovery(position);
            }
        }

        // Developer editing must go through this boundary so it cannot leave stale timelines.
        public bool TryEditEnvironment(GridPosition position, int temperature, int humidity, int recovery, out string error)
        {
            error = null;
            if (temperature < 0 || temperature > 100 || humidity < 0 || humidity > 100 || recovery < 0 || recovery > 150000 ||
                !tiles.TryGetValue(position, out EnvironmentTile tile)) { error = "环境输入超出允许范围或目标不存在"; return false; }
            tile.Locals[0] = tile.Locals[1] = null; tile.RecoveryReturning = false;
            tile.RecoveryAge = 0;
            tile.RecoveryCancelIssued = false;
            tile.RecoveryDetached = false;
            tile.Values[0] = EnvironmentRules.Clamp(EnvironmentAttribute.Temperature, temperature);
            tile.Values[1] = EnvironmentRules.Clamp(EnvironmentAttribute.Humidity, humidity);
            tile.Values[2] = recovery;
            for (int i = 0; i < 3; i++) tile.Tracks[i].Stop(tile.Values[i]);
            Reclassify(tile);
            RefreshRecoveryTarget(tile);
            return true;
        }

        public void AdvanceToDay(int day)
        {
            if (day < CurrentDay) throw new InvalidOperationException("Reset the environment session when resetting the simulation clock.");
            while (CurrentDay < day) { CurrentDay++; AdvanceOneDay(); }
        }
        // ResetDay resets the clock only; ecological values and effect ages are preserved.
        public void RebaseClock(int day) => CurrentDay = day;
        private void AdvanceOneDay()
        {
            foreach (var tile in tiles.Values)
            {
                for (int i = 0; i < 2; i++) if (tile.Tracks[i].Active) tile.Values[i] = tile.Tracks[i].Tick();
                if (tile.Tracks[2].Active) tile.Values[2] = tile.Tracks[2].Tick();
                // Each active command owns a slot only until its return begins.
                for (int i = 0; i < 2; i++) if (tile.Locals[i] != null) tile.Locals[i].Age++;
                bool localEnded = false;
                for (int i = 0; i < 2; i++)
                {
                    if (tile.Locals[i] == null) continue;
                    if (tile.Locals[i].Age == EnvironmentRules.TransitionDays + EnvironmentRules.ClimateHoldDays)
                        StartLocalReturn(tile, i);
                    if (tile.Locals[i].Age >= EnvironmentRules.TransitionDays +
                        EnvironmentRules.ClimateHoldDays + EnvironmentRules.ClimateReturnDays)
                    { tile.Locals[i] = null; localEnded = true; }
                }
                if (localEnded) RefreshClimateTargets(tile);
                if (tile.RecoveryReturning && tile.RecoveryAge < tile.RecoveryHoldThreshold)
                {
                    tile.RecoveryAge++;
                    if (tile.RecoveryAge == tile.RecoveryHoldThreshold)
                        StartRecoveryReturn(tile);
                }
                if (tile.RecoveryReturning && !tile.IsWater &&
                    tile.RecoveryAge == EnvironmentRules.LandRecoveryTransitionDays &&
                    !tile.Tracks[2].Active)
                    tile.Tracks[2].Stop(tile.Values[2]);
                if (tile.RecoveryReturning && !tile.Tracks[2].Active)
                {
                    if (tile.RecoveryAge >= tile.RecoveryHoldThreshold)
                    {
                        tile.RecoveryReturning = false;
                        tile.RecoveryDetached = !tile.IsWater &&
                            tile.RecoveryHabitatAtSubmit == tile.Habitat;
                        RefreshRecoveryTarget(tile);
                    }
                }
                if (!tile.RecoveryReturning && !tile.RecoveryDetached && !tile.IsWater)
                    RefreshRecoveryTarget(tile);
                Reclassify(tile);
            }
        }

        private void StartLocalReturn(EnvironmentTile tile, int index)
        {
            LocalClimateEffect effect = tile.Locals[index];
            double destination = effect != null && effect.HabitatAtSubmit == tile.Habitat
                ? effect.Baseline : ClimateDestination(tile, index);
            tile.Tracks[index].Begin(tile.Values[index], destination,
                EnvironmentRules.ClimateReturnDays, EffectPhase.Returning);
        }
        private void StartRecoveryReturn(EnvironmentTile tile)
        {
            double destination = tile.IsWater ? RecoveryDefault(tile) :
                tile.RecoveryHabitatAtSubmit == tile.Habitat
                    ? tile.RecoveryBaseline : RecoveryDefault(tile);
            tile.Tracks[2].Begin(tile.Values[2], destination,
                tile.RecoveryReturnDuration, EffectPhase.Returning);
        }
        private static double ClimateDestination(EnvironmentTile tile, int index)
        {
            return EnvironmentRules.Defaults(tile.Habitat).Get((EnvironmentAttribute)index);
        }
        private void RefreshClimateTargets(EnvironmentTile tile)
        {
            for (int i = 0; i < 2; i++)
            {
                // A biome label changes the destination of a returning command,
                // but never moves an untouched climate axis on its own.
                if (tile.Locals[i] == null && tile.Tracks[i].Phase == EffectPhase.Automatic &&
                    tile.Tracks[i].Active)
                    EnsureTarget(tile, i, ClimateDestination(tile, i));
            }
        }
        private void RefreshRecoveryTarget(EnvironmentTile tile)
        { if (!tile.RecoveryReturning && !tile.RecoveryDetached) EnsureTarget(tile, 2, RecoveryDefault(tile)); }
        private static double RecoveryDefault(EnvironmentTile tile)
        {
            if (tile.IsWater) return EnvironmentRules.WaterDefaultRecovery;
            return EnvironmentRules.Defaults(tile.Habitat).Recovery;
        }
        private static void EnsureTarget(EnvironmentTile tile, int index, double target)
        {
            var track = tile.Tracks[index];
            if (Math.Abs(track.Target - target) < .000001 && (track.Active || Math.Abs(tile.Values[index] - target) < .000001)) return;
            track.Begin(tile.Values[index], target, index == 2 ? EnvironmentRules.RecoveryReturnDays :
                EnvironmentRules.ClimateReturnDays, EffectPhase.Automatic);
        }
        private void EnforceSlots(EnvironmentTile tile)
        {
            const int allowed = 2;
            while (true)
            {
                int count = tile.RecoveryReturning &&
                    tile.RecoveryAge < tile.RecoveryHoldThreshold ? 1 : 0;
                for (int i = 0; i < 2; i++)
                    if (tile.Locals[i] != null && tile.Locals[i].Active) count++;
                if (count <= allowed) return;
                long oldest = long.MaxValue;
                int owner = -1;
                for (int i = 0; i < 2; i++)
                    if (tile.Locals[i] != null && tile.Locals[i].Active &&
                        tile.Locals[i].Sequence < oldest)
                    { oldest = tile.Locals[i].Sequence; owner = i; }
                if (tile.RecoveryReturning && tile.RecoveryAge < tile.RecoveryHoldThreshold &&
                    tile.RecoverySequence < oldest) owner = 2;
                if (owner == 2)
                {
                    tile.RecoveryAge = tile.RecoveryHoldThreshold;
                    tile.RecoveryCancelIssued = true;
                    StartRecoveryReturn(tile);
                }
                else
                {
                    tile.Locals[owner].Age = EnvironmentRules.TransitionDays +
                        EnvironmentRules.ClimateHoldDays;
                    StartLocalReturn(tile, owner);
                }
            }
        }
        private void Reclassify(EnvironmentTile tile)
        {
            if (tile.IsWater) return;
            var habitat = EnvironmentRules.ClassifyHabitat(tile.Elevation,
                EffectiveClimate(tile, 0), EffectiveClimate(tile, 1));
            if (habitat == tile.Habitat) return;
            tile.Habitat = habitat;
            tile.Terrain = EnvironmentRules.VisualTerrain(habitat);
            // A completed land recovery command preserves its captured baseline
            // only in the habitat where it was submitted.
            if (tile.RecoveryDetached && !tile.RecoveryReturning)
                tile.RecoveryDetached = false;
            for (int i = 0; i < 2; i++)
            {
                if (tile.Locals[i] != null && !tile.Locals[i].Active)
                {
                    tile.Locals[i].Age = EnvironmentRules.TransitionDays + EnvironmentRules.ClimateHoldDays;
                    StartLocalReturn(tile, i);
                }
                else if (tile.Locals[i] == null && tile.Tracks[i].Phase == EffectPhase.Automatic)
                    tile.Tracks[i].Stop(tile.Values[i]);
            }
            if (tile.RecoveryReturning && tile.RecoveryAge >= tile.RecoveryHoldThreshold)
                StartRecoveryReturn(tile);
            else RefreshRecoveryTarget(tile);
        }
        private static double EffectiveClimate(EnvironmentTile tile, int index) =>
            EnvironmentRules.Clamp((EnvironmentAttribute)index, tile.Values[index]);
        private static bool Contains(IReadOnlyList<GridPosition> positions, GridPosition value)
        { foreach (var position in positions) if (position.Equals(value)) return true; return false; }
    }
}
