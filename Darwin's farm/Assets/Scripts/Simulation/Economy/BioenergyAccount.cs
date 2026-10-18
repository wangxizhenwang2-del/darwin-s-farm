using System;
using System.Collections.Generic;
using DarwinFarm.Environment;
using UnityEngine;

// Owns money and receipts. Ecology and EnvironmentWorld never mutate the account.
[DisallowMultipleComponent]
public sealed class BioenergyAccount : MonoBehaviour
{
    [Serializable]
    public sealed class Receipt
    {
        public string id;
        public string reason;
        public int day;
        public long amount;
    }

    [Serializable]
    private sealed class SavedState
    {
        public long balance;
        public bool costsEnabled;
        public int lastSettledDay;
        public bool initialSpeciesCaptured;
        public List<string> rewardedSpecies = new List<string>();
        public List<Receipt> receipts = new List<Receipt>();
    }

    // Testing default: legal operations are free; enable costs for economy testing.
    [SerializeField] private bool costsEnabled = false;
    [SerializeField] private long balance;
    private int lastSettledDay;
    private bool initialSpeciesCaptured;
    private SimulationController simulation;
    private SimulationTime clock;
    private readonly HashSet<string> rewardedSpecies = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<Receipt> receipts = new List<Receipt>();

    public event Action<long> BalanceChanged;
    public long Balance => balance;
    public bool HasEverHadWildPopulation => initialSpeciesCaptured;
    public bool CostsEnabled { get => costsEnabled; set => costsEnabled = value; }
    public IReadOnlyList<Receipt> Receipts
    {
        get
        {
            var snapshot = new List<Receipt>(receipts.Count);
            foreach (Receipt receipt in receipts)
                snapshot.Add(new Receipt { id = receipt.id, reason = receipt.reason,
                    day = receipt.day, amount = receipt.amount });
            return snapshot.AsReadOnly();
        }
    }

    private void Awake()
    {
        balance = BioenergySettings.Current.initialBalance;
        simulation = GetComponent<SimulationController>();
        clock = GetComponent<SimulationTime>();
    }

    private void OnEnable()
    {
        if (simulation != null)
        {
            simulation.OnBeforeDaySimulated += BeforeDay;
            simulation.OnDaySimulated += AfterDay;
        }
        if (clock != null) clock.OnDayReset += RebaseDay;
    }

    private void OnDisable()
    {
        if (simulation != null)
        {
            simulation.OnBeforeDaySimulated -= BeforeDay;
            simulation.OnDaySimulated -= AfterDay;
        }
        if (clock != null) clock.OnDayReset -= RebaseDay;
    }

    // The command is applied after a temporary debit. A rejected command leaves no debit.
    public bool TryExecute(int price, string reason, Func<string, long, bool> command,
        out string error)
    {
        error = null;
        if (price < 0 || command == null) { error = "无效交易"; return false; }
        PlayerEconomyController operations = GetComponent<PlayerEconomyController>();
        if (operations != null && operations.IsGameLost)
        { error = "本局已经结束，请重新开局"; return false; }
        long paid = costsEnabled ? price : 0;
        if (balance < paid) { error = "生物能不足"; return false; }
        string id = Guid.NewGuid().ToString("N");
        balance -= paid;
        bool committed;
        try { committed = command(id, paid); }
        catch (Exception exception)
        {
            balance += paid;
            Debug.LogException(exception, this);
            error = "操作未完成";
            return false;
        }
        if (!committed)
        {
            balance += paid;
            error = "操作条件已变化";
            return false;
        }
        receipts.Add(new Receipt { id = id, reason = reason, day = clock != null ? clock.currentDay : 0,
            amount = -paid });
        BalanceChanged?.Invoke(balance);
        return true;
    }

    public void Grant(long amount, string reason)
    {
        if (amount <= 0) return;
        checked { balance += amount; }
        receipts.Add(new Receipt { id = Guid.NewGuid().ToString("N"), reason = reason,
            day = clock != null ? clock.currentDay : 0, amount = amount });
        BalanceChanged?.Invoke(balance);
    }

    private void RebaseDay(int day)
    {
        if (day < lastSettledDay) lastSettledDay = day;
    }

    private void BeforeDay(int day)
    {
        if (initialSpeciesCaptured) return;
        var initial = CountWildSpecies();
        if (initial.Count == 0) return;
        foreach (var pair in initial) rewardedSpecies.Add(pair.Key);
        initialSpeciesCaptured = true;
    }

    private void AfterDay(int day)
    {
        if (!costsEnabled) return;
        if (day <= lastSettledDay) return;
        if (!initialSpeciesCaptured) BeforeDay(day);
        long daily = 0;
        foreach (var pair in CountWildSpecies())
        {
            if (rewardedSpecies.Add(pair.Key))
                Grant(BioenergySettings.Current.UnlockReward(pair.Value.level),
                    "首次解锁:" + pair.Key);
            if (pair.Value.count > 1)
                daily += (long)Math.Floor(BioenergySettings.Current.dailyIncomeMultiplier *
                    (double)pair.Value.value * Math.Log(pair.Value.count));
        }
        if (daily > 0) Grant(daily, "每日收成:" + day);
        lastSettledDay = day;
    }

    private struct SpeciesCount
    {
        public long count;
        public int value;
        public int level;
    }

    private Dictionary<string, SpeciesCount> CountWildSpecies()
    {
        var result = new Dictionary<string, SpeciesCount>(StringComparer.Ordinal);
        if (simulation == null) return result;
        foreach (BlockInfo block in simulation.ReadBlocks())
        {
            if (block == null || block.community == null) continue;
            foreach (PopulationData population in block.community)
            {
                if (population == null || population.speciesAmount <= 0) continue;
                string key = SpeciesKey(population);
                if (string.IsNullOrEmpty(key)) continue;
                result.TryGetValue(key, out SpeciesCount count);
                count.count += population.speciesAmount;
                count.level = Mathf.Clamp(population.species != null ?
                    population.species.trophicLevel : population.trophicLevel, 0, 2);
                count.value = population.species != null ?
                    Mathf.Clamp(population.species.bioenergyValue, 1, 100) :
                    BioenergySettings.Current.FallbackSpeciesValue(count.level);
                result[key] = count;
            }
        }
        return result;
    }

    public static string SpeciesKey(PopulationData population) => population == null ? null :
        population.species != null ? population.species.EconomyId :
        !string.IsNullOrEmpty(population.speciesId) ? population.speciesId : population.lineageName;

    public int PreviewDailyIncome()
    {
        if (!costsEnabled) return 0;
        long total = 0;
        foreach (SpeciesCount count in CountWildSpecies().Values)
            if (count.count > 1) total += (long)Math.Floor(
                BioenergySettings.Current.dailyIncomeMultiplier * (double)count.value *
                Math.Log(count.count));
        return (int)Math.Min(int.MaxValue, total);
    }

    public string CaptureState()
    {
        var state = new SavedState { balance = balance, costsEnabled = costsEnabled,
            lastSettledDay = lastSettledDay, initialSpeciesCaptured = initialSpeciesCaptured,
            rewardedSpecies = new List<string>(rewardedSpecies), receipts = new List<Receipt>(receipts) };
        return JsonUtility.ToJson(state);
    }

    // Load after restoring the world and clock, before the next simulated day.
    public bool TryRestoreState(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        SavedState state;
        try { state = JsonUtility.FromJson<SavedState>(json); }
        catch { return false; }
        if (state == null || state.balance < 0 || state.lastSettledDay < 0 ||
            state.rewardedSpecies == null || state.receipts == null ||
            (clock != null && state.lastSettledDay > clock.currentDay)) return false;
        var receiptIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (Receipt receipt in state.receipts)
            if (receipt == null || string.IsNullOrEmpty(receipt.id) ||
                !receiptIds.Add(receipt.id)) return false;
        balance = state.balance;
        costsEnabled = state.costsEnabled;
        lastSettledDay = state.lastSettledDay;
        initialSpeciesCaptured = state.initialSpeciesCaptured;
        rewardedSpecies.Clear();
        foreach (string key in state.rewardedSpecies) if (!string.IsNullOrEmpty(key)) rewardedSpecies.Add(key);
        receipts.Clear(); receipts.AddRange(state.receipts);
        BalanceChanged?.Invoke(balance);
        return true;
    }
}
