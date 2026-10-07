using System;
using System.Collections.Generic;

namespace DarwinFarm.Environment
{
    // Model: owns timelines and graph rules, but never writes Unity objects or money.
    public sealed class EnvironmentWorld
    {
        private readonly Dictionary<GridPosition, EnvironmentTile> tiles = new Dictionary<GridPosition, EnvironmentTile>();
        private Dictionary<GridPosition, TopologyNode> topology = new Dictionary<GridPosition, TopologyNode>();
        private readonly Dictionary<long, MonsoonState> monsoons = new Dictionary<long, MonsoonState>();
        private readonly List<MonsoonSettlement> settlements = new List<MonsoonSettlement>();
        private readonly HashSet<string> usedReceipts = new HashSet<string>(StringComparer.Ordinal);
        private long nextMonsoonId = 1;
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
        public IReadOnlyList<MonsoonSnapshot> ReadMonsoons()
        {
            var ids = new List<long>(monsoons.Keys); ids.Sort();
            var result = new List<MonsoonSnapshot>(ids.Count);
            foreach (long id in ids) result.Add(new MonsoonSnapshot(monsoons[id]));
            return result.AsReadOnly();
        }
        public IReadOnlyList<MonsoonSettlement> ReadSettlements() => new List<MonsoonSettlement>(settlements).AsReadOnly();
        public bool AcknowledgeSettlement(long id) => settlements.RemoveAll(x => x.MonsoonId == id) > 0;

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
            bool sourceChanged = false;
            foreach (var wind in monsoons.Values)
                if (!next.TryGetValue(wind.Source, out TopologyNode node) ||
                    !topology.TryGetValue(wind.Source, out TopologyNode previous) ||
                    node.Elevation != previous.Elevation || !SameNeighbors(node.Neighbors, previous.Neighbors))
                    sourceChanged = true;

            var removed = new List<GridPosition>();
            foreach (var position in tiles.Keys) if (!next.ContainsKey(position)) removed.Add(position);
            foreach (var position in removed) tiles.Remove(position);
            topology = next;
            foreach (var node in next.Values)
            {
                if (!tiles.TryGetValue(node.Position, out EnvironmentTile tile)) tiles.Add(node.Position, new EnvironmentTile(node));
                else if (tile.Elevation != node.Elevation)
                { tile.Elevation = node.Elevation; Reclassify(tile); }
            }
            if (sourceChanged)
                foreach (long id in new List<long>(monsoons.Keys)) EndMonsoon(id, MonsoonExitReason.SourceChanged);
            else RecomputeMonsoons();
        }

        public bool TrySetClimate(GridPosition position, EnvironmentAttribute attribute, double amount, out string error)
        {
            error = null;
            if (attribute != EnvironmentAttribute.Temperature && attribute != EnvironmentAttribute.Humidity ||
                !EnvironmentRules.IsClimateStrength(amount)) { error = "单格温湿度强度必须为 ±25 或 ±50"; return false; }
            if (!tiles.TryGetValue(position, out EnvironmentTile tile)) { error = "目标地块不存在"; return false; }
            int index = (int)attribute;
            tile.Locals[index] = new LocalClimateEffect();
            double target = EnvironmentRules.Clamp(attribute, EnvironmentRules.Defaults(tile.Terrain).Get(attribute) + amount);
            tile.Tracks[index].Begin(tile.Values[index], target, 25, EffectPhase.Entering);
            // A local operation masks both wind components, not the source/area record.
            RefreshClimateTargets(tile, true);
            return true;
        }

        public bool TryCancelClimate(GridPosition position, EnvironmentAttribute attribute, out string error)
        {
            error = null;
            if (attribute != EnvironmentAttribute.Temperature && attribute != EnvironmentAttribute.Humidity ||
                !tiles.TryGetValue(position, out EnvironmentTile tile) || tile.Locals[(int)attribute] == null)
            { error = "该地块没有对应单格操作"; return false; }
            int index = (int)attribute;
            tile.Locals[index].Age = 125;
            StartLocalReturn(tile, index);
            RefreshClimateTargets(tile, true);
            return true;
        }

        public bool TrySetRecovery(GridPosition position, double amount, out string error)
        {
            error = null;
            if (!EnvironmentRules.IsRecoveryStrength(amount)) { error = "植被增量强度必须为 ±25000 或 ±50000"; return false; }
            if (!tiles.TryGetValue(position, out EnvironmentTile tile)) { error = "目标地块不存在"; return false; }
            tile.Values[2] = EnvironmentRules.Clamp(EnvironmentAttribute.Recovery, EnvironmentRules.Defaults(tile.Terrain).Recovery + amount);
            tile.Tracks[2].Stop(tile.Values[2]);
            tile.RecoveryReturning = true;
            Reclassify(tile);
            // Immediate transformation determines the fixed return target for this command.
            tile.Tracks[2].Begin(tile.Values[2], EnvironmentRules.Defaults(tile.Terrain).Recovery, 100, EffectPhase.Returning);
            return true;
        }

        public bool TryCancelRecovery(GridPosition position, out string error)
        {
            error = null;
            if (!tiles.TryGetValue(position, out EnvironmentTile tile) || !tile.RecoveryReturning)
            { error = "该地块没有植被增量操作"; return false; }
            tile.Tracks[2].Begin(tile.Values[2], EnvironmentRules.Defaults(tile.Terrain).Recovery, 100, EffectPhase.Returning);
            return true;
        }

        public bool TryPreviewMonsoon(GridPosition source, int temperatureOffset, int humidityOffset,
            out IReadOnlyList<GridPosition> area, out string error)
        {
            area = Array.Empty<GridPosition>(); error = null;
            if (!EnvironmentRules.IsMonsoonStrength(temperatureOffset) || !EnvironmentRules.IsMonsoonStrength(humidityOffset))
            { error = "季风温湿度强度分别必须为 ±20 或 ±40"; return false; }
            if (!topology.TryGetValue(source, out TopologyNode node) || node.Elevation == 2)
            { error = "季风发源点必须是海拔 0 或 1 的已放置地块"; return false; }
            var positions = new List<GridPosition>(Flood(source)); positions.Sort(); area = positions.AsReadOnly();
            return true;
        }

        public bool TryDeployMonsoon(GridPosition source, int temperatureOffset, int humidityOffset,
            EnvironmentPayment payment, out long id, out string error)
        {
            id = 0;
            if (!TryPreviewMonsoon(source, temperatureOffset, humidityOffset, out _, out error)) return false;
            if (payment != null && usedReceipts.Contains(payment.ReceiptId))
            { error = "支付凭据已经绑定其他季风"; return false; }
            long existing = 0;
            foreach (var wind in monsoons.Values) if (wind.Source.Equals(source)) existing = wind.Id;
            if (existing != 0) EndMonsoon(existing, MonsoonExitReason.Replaced);
            id = nextMonsoonId++;
            var state = new MonsoonState { Id = id, Source = source, TemperatureOffset = temperatureOffset,
                HumidityOffset = humidityOffset, Payment = payment };
            if (payment != null) usedReceipts.Add(payment.ReceiptId);
            monsoons.Add(id, state);
            RecomputeMonsoons();
            return true;
        }

        public bool TryCancelMonsoon(long id, out string error)
        {
            error = null;
            if (!monsoons.ContainsKey(id)) { error = "季风已不存在"; return false; }
            EndMonsoon(id, MonsoonExitReason.Manual); return true;
        }

        // Developer editing must go through this boundary so it cannot leave stale timelines.
        public bool TryEditEnvironment(GridPosition position, int temperature, int humidity, int recovery, out string error)
        {
            error = null;
            if (temperature < 0 || temperature > 100 || humidity < 0 || humidity > 100 || recovery < 0 || recovery > 150000 ||
                !tiles.TryGetValue(position, out EnvironmentTile tile)) { error = "环境输入超出允许范围或目标不存在"; return false; }
            tile.Locals[0] = tile.Locals[1] = null; tile.RecoveryReturning = false;
            tile.Values[0] = temperature; tile.Values[1] = humidity; tile.Values[2] = recovery;
            for (int i = 0; i < 3; i++) tile.Tracks[i].Stop(tile.Values[i]);
            Reclassify(tile); RefreshClimateTargets(tile); RefreshRecoveryTarget(tile);
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
                for (int i = 0; i < 3; i++) if (tile.Tracks[i].Active) tile.Values[i] = tile.Tracks[i].Tick();
                // Increment both ages before handling phase boundaries, independent of field order.
                for (int i = 0; i < 2; i++) if (tile.Locals[i] != null) tile.Locals[i].Age++;
                bool localEnded = false;
                for (int i = 0; i < 2; i++)
                {
                    if (tile.Locals[i] == null) continue;
                    if (tile.Locals[i].Age == 125) StartLocalReturn(tile, i);
                    if (tile.Locals[i].Age >= 150) { tile.Locals[i] = null; localEnded = true; }
                }
                if (localEnded) RefreshClimateTargets(tile);
                if (tile.RecoveryReturning && !tile.Tracks[2].Active)
                { tile.RecoveryReturning = false; RefreshRecoveryTarget(tile); }
                Reclassify(tile);
            }
        }

        private void StartLocalReturn(EnvironmentTile tile, int index)
        {
            double target = ClimateDestination(tile, index);
            tile.Tracks[index].Begin(tile.Values[index], target, 25, EffectPhase.Returning);
            RefreshClimateTargets(tile, true);
        }
        private static double ClimateDestination(EnvironmentTile tile, int index)
        {
            bool enteringOrHolding = false;
            for (int i = 0; i < 2; i++) if (tile.Locals[i] != null && tile.Locals[i].Age < 125) enteringOrHolding = true;
            return tile.MonsoonId.HasValue && !enteringOrHolding ? tile.WindTargets[index] :
                EnvironmentRules.Defaults(tile.Terrain).Get((EnvironmentAttribute)index);
        }
        private void RefreshClimateTargets(EnvironmentTile tile, bool retargetReturns = false)
        {
            for (int i = 0; i < 2; i++)
            {
                double destination = ClimateDestination(tile, i);
                if (tile.Locals[i] == null) EnsureTarget(tile, i, destination);
                else if (retargetReturns && tile.Locals[i].Age >= 125 &&
                    Math.Abs(tile.Tracks[i].Target - destination) > .000001)
                {
                    // A changed wind/local owner interrupts a return from its actual value.
                    tile.Locals[i].Age = 125;
                    tile.Tracks[i].Begin(tile.Values[i], destination, 25, EffectPhase.Returning);
                }
            }
        }
        private void RefreshRecoveryTarget(EnvironmentTile tile)
        { if (!tile.RecoveryReturning) EnsureTarget(tile, 2, EnvironmentRules.Defaults(tile.Terrain).Recovery); }
        private static void EnsureTarget(EnvironmentTile tile, int index, double target)
        {
            var track = tile.Tracks[index];
            if (Math.Abs(track.Target - target) < .000001 && (track.Active || Math.Abs(tile.Values[index] - target) < .000001)) return;
            track.Begin(tile.Values[index], target, 25, EffectPhase.Automatic);
        }
        private void Reclassify(EnvironmentTile tile)
        {
            var kind = EnvironmentRules.Classify(tile.Elevation, EnvironmentRules.Round(tile.Values[0]),
                EnvironmentRules.Round(tile.Values[1]), EnvironmentRules.Round(tile.Values[2]));
            if (kind == tile.Terrain) return;
            tile.Terrain = kind;
            RefreshClimateTargets(tile); RefreshRecoveryTarget(tile);
        }

        private HashSet<GridPosition> Flood(GridPosition source)
        {
            var result = new HashSet<GridPosition>();
            if (!topology.TryGetValue(source, out TopologyNode root) || root.Elevation == 2) return result;
            var queue = new Queue<GridPosition>(); result.Add(source); queue.Enqueue(source);
            while (queue.Count > 0)
                foreach (var neighbor in topology[queue.Dequeue()].Neighbors)
                    if (topology[neighbor].Elevation == root.Elevation && result.Add(neighbor)) queue.Enqueue(neighbor);
            return result;
        }
        private void RecomputeMonsoons()
        {
            var occupied = new Dictionary<GridPosition, long>();
            var conflicts = new HashSet<long>();
            foreach (var wind in monsoons.Values)
            {
                wind.Area = Flood(wind.Source);
                foreach (var position in wind.Area)
                    if (occupied.TryGetValue(position, out long other)) { conflicts.Add(other); conflicts.Add(wind.Id); }
                    else occupied.Add(position, wind.Id);
            }
            // Detect the whole conflict graph before cancelling, including paused wind cells.
            var conflictIds = new List<long>(conflicts); conflictIds.Sort();
            foreach (long id in conflictIds) EndMonsoon(id, MonsoonExitReason.Overlap);
            occupied.Clear();
            foreach (var wind in monsoons.Values) foreach (var position in wind.Area) occupied.Add(position, wind.Id);
            foreach (var tile in tiles.Values)
            {
                long? desired = occupied.TryGetValue(tile.Position, out long id) ? id : (long?)null;
                if (tile.MonsoonId == desired) continue;
                tile.MonsoonId = desired;
                if (desired.HasValue)
                {
                    var wind = monsoons[desired.Value]; var defaults = EnvironmentRules.Defaults(tile.Terrain);
                    tile.WindTargets[0] = EnvironmentRules.Clamp(EnvironmentAttribute.Temperature, defaults.Temperature + wind.TemperatureOffset);
                    tile.WindTargets[1] = EnvironmentRules.Clamp(EnvironmentAttribute.Humidity, defaults.Humidity + wind.HumidityOffset);
                }
                RefreshClimateTargets(tile, true);
            }
        }
        private void EndMonsoon(long id, MonsoonExitReason reason)
        {
            if (!monsoons.TryGetValue(id, out MonsoonState wind)) return;
            monsoons.Remove(id); settlements.Add(new MonsoonSettlement(wind, reason));
            foreach (var tile in tiles.Values)
                if (tile.MonsoonId == id) { tile.MonsoonId = null; RefreshClimateTargets(tile, true); }
        }
        private static bool Contains(IReadOnlyList<GridPosition> positions, GridPosition value)
        { foreach (var position in positions) if (position.Equals(value)) return true; return false; }
        private static bool SameNeighbors(IReadOnlyList<GridPosition> a, IReadOnlyList<GridPosition> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!a[i].Equals(b[i])) return false;
            return true;
        }
    }
}
