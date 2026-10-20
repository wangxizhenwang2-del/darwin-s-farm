using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DefaultExecutionOrder(1000)]
public sealed class EcologyNewsDetector : MonoBehaviour
{
    [Header("News thresholds (do not change ecology simulation)")]
    [SerializeField, Min(1)] private int crashWindowDays = 10;
    [SerializeField, Min(1)] private int crashMinimumLoss = 20;
    [SerializeField, Range(.01f, 1f)] private float crashMinimumFraction = .4f;
    [SerializeField, Min(1)] private int crashCooldownDays = 30;
    [SerializeField, Min(1)] private int crisisConsecutiveDays = 5;
    [SerializeField, Min(1f)] private float crisisCapacityRatio = 1.2f;
    private sealed class SpeciesAtSite
    {
        public string key, site, species, name;
        public int count;
        public float capacity;
    }
    private sealed class CrashState
    {
        public readonly Queue<int> past = new Queue<int>();
        public bool armed = true;
        public int lastDay = -100000;
    }
    private sealed class CrisisState { public int days; public bool active; }
    public readonly EcologicalEventBus Bus = new EcologicalEventBus();
    private readonly Dictionary<string,SpeciesAtSite> previous = new Dictionary<string,SpeciesAtSite>();
    private readonly HashSet<string> everHere = new HashSet<string>();
    private readonly Dictionary<string,CrashState> crashes = new Dictionary<string,CrashState>();
    private readonly Dictionary<string,CrisisState> crises = new Dictionary<string,CrisisState>();
    private readonly List<PopulationTransitionResult> migrations = new List<PopulationTransitionResult>();
    private readonly List<PopulationTransitionResult> evolutions = new List<PopulationTransitionResult>();
    private SimulationController simulation;
    private SimulationEnvironmentController environment;
    private SimulationTime clock;
    private readonly List<EcologicalEventData> startupUnlockNews=new List<EcologicalEventData>();
    private bool ready;
    private bool firstTerrainNewsShown;
    private int ordinal;
    [Serializable] public sealed class SaveState
    {
        public int ordinal;
        public List<SavedSite> previous=new List<SavedSite>();
        public List<string> everHere=new List<string>();
        public List<SavedCrash> crashes=new List<SavedCrash>();
        public List<SavedCrisis> crises=new List<SavedCrisis>();
    }
    [Serializable] public sealed class SavedSite
    { public string key,site,species,name; public int count; public float capacity; }
    [Serializable] public sealed class SavedCrash
    { public string key; public int[] past; public bool armed; public int lastDay; }
    [Serializable] public sealed class SavedCrisis
    { public string key; public int days; public bool active; }
    public SaveState CaptureSaveState()
    {
        var result=new SaveState { ordinal=ordinal };
        foreach(var v in previous.Values)result.previous.Add(new SavedSite {
            key=v.key,site=v.site,species=v.species,name=v.name,count=v.count,capacity=v.capacity });
        result.everHere.AddRange(everHere);
        foreach(var pair in crashes)result.crashes.Add(new SavedCrash {
            key=pair.Key,past=pair.Value.past.ToArray(),armed=pair.Value.armed,lastDay=pair.Value.lastDay });
        foreach(var pair in crises)result.crises.Add(new SavedCrisis {
            key=pair.Key,days=pair.Value.days,active=pair.Value.active });
        return result;
    }
    public void RestoreSaveState(SaveState state)
    {
        if(state==null||state.previous==null||state.everHere==null||
            state.crashes==null||state.crises==null)throw new ArgumentException("Invalid ecology news history.");
        ordinal=state.ordinal;
        firstTerrainNewsShown=environment!=null && environment.CaptureTerrainUnlocks()
            .Any(kind=>kind!=DarwinFarm.Environment.TerrainKind.Desert);
        previous.Clear();everHere.Clear();crashes.Clear();crises.Clear();
        foreach(var v in state.previous)
            previous.Add(v.key,new SpeciesAtSite { key=v.key,site=v.site,species=v.species,
                name=v.name,count=v.count,capacity=v.capacity });
        foreach(string key in state.everHere)everHere.Add(key);
        foreach(var v in state.crashes)
        {
            var item=new CrashState { armed=v.armed,lastDay=v.lastDay };
            foreach(int count in v.past)item.past.Enqueue(count);
            crashes.Add(v.key,item);
        }
        foreach(var v in state.crises)crises.Add(v.key,new CrisisState { days=v.days,active=v.active });
    }

    private void OnEnable()
    {
        environment=FindFirstObjectByType<SimulationEnvironmentController>();
        clock=FindFirstObjectByType<SimulationTime>();
        if(environment!=null)
        {
            environment.TerrainUnlocked+=OnTerrainUnlocked;
            environment.WaterUnlocked+=OnWaterUnlocked;
        }
    }
    private void Start()
    {
        simulation=FindFirstObjectByType<SimulationController>();
        ready=true;
        foreach(EcologicalEventData value in startupUnlockNews) Bus.Publish(value);
        startupUnlockNews.Clear();
        if(simulation==null){Debug.LogWarning("News detector: no simulation.",this);return;}
        foreach(var entry in Snapshot().Values)
        {
            previous[entry.key]=entry;
            if(entry.count>0)everHere.Add(entry.key);
        }
        simulation.OnMigrationResolved+=RememberMigration;
        simulation.OnPresetEvolutionResolved+=RememberEvolution;
        simulation.OnDaySimulated+=AfterDay;
    }
    private void OnDestroy()
    {
        if(environment!=null)
        {
            environment.TerrainUnlocked-=OnTerrainUnlocked;
            environment.WaterUnlocked-=OnWaterUnlocked;
        }
        if(simulation==null)return;
        simulation.OnMigrationResolved-=RememberMigration;
        simulation.OnPresetEvolutionResolved-=RememberEvolution;
        simulation.OnDaySimulated-=AfterDay;
    }
    private void PublishUnlock(EcologicalEventData value)
    {
        if(ready)Bus.Publish(value);
        else startupUnlockNews.Add(value);
    }
    private void OnTerrainUnlocked(DarwinFarm.Environment.TerrainKind kind, Vector2Int coordinate)
    {
        bool first = !firstTerrainNewsShown;
        firstTerrainNewsShown = true;
        if (first && TutorialFlowController.HandlesFirstTerrainNews(kind)) return;
        string name=TerrainName(kind);
        PublishUnlock(new EcologicalEventData {
            eventId="terrain:"+kind,gameDay=clock!=null?clock.currentDay:0,
            type=EcologicalEventType.TerrainUnlocked,
            targetBlockId=coordinate.x+","+coordinate.y,
            message=first ? "新地形【"+name+"】解锁了！" :
                "地块 ("+coordinate.x+","+coordinate.y+") 自然演变为「"+name+"」；现在可以建造"+name+"地块。"
        });
    }
    private void OnWaterUnlocked()
    {
        PublishUnlock(new EcologicalEventData {
            eventId="terrain:all-water",gameDay=clock!=null?clock.currentDay:0,
            type=EcologicalEventType.WatersUnlocked,
            message="首次发现鹿或鹿驯，所有水域地块已解锁。"
        });
    }
    public static string TerrainName(DarwinFarm.Environment.TerrainKind kind)
    {
        switch(kind)
        {
            case DarwinFarm.Environment.TerrainKind.Forest:return "森林";
            case DarwinFarm.Environment.TerrainKind.Rainforest:return "雨林";
            case DarwinFarm.Environment.TerrainKind.Grassland:return "草原";
            case DarwinFarm.Environment.TerrainKind.Desert:return "荒漠";
            case DarwinFarm.Environment.TerrainKind.Tundra:return "苔原";
            case DarwinFarm.Environment.TerrainKind.Highland:return "高山";
            case DarwinFarm.Environment.TerrainKind.SnowMountain:return "雪山";
            case DarwinFarm.Environment.TerrainKind.Volcano:return "火山";
            default:return kind.ToString();
        }
    }
    private void RememberMigration(PopulationTransitionResult result)
    {
        if(result.Kind==PopulationTransitionKind.Migration && result.Amount>0 &&
           result.TargetCountAfter>result.TargetCountBefore)migrations.Add(result);
    }
    private void RememberEvolution(PopulationTransitionResult result)
    {
        if(result.Kind==PopulationTransitionKind.PresetEvolution && result.Amount>0)evolutions.Add(result);
    }
    private static string SpeciesKey(PopulationData p)
    {
        if(p==null)return "";
        if(!string.IsNullOrEmpty(p.speciesId))return p.speciesId;
        if(p.species!=null && !string.IsNullOrEmpty(p.species.SpeciesId))return p.species.SpeciesId;
        return p.lineageName??"";
    }
    private static string DisplayName(PopulationData p)
        => p.species!=null ? p.species.SpeciesName :
           !string.IsNullOrWhiteSpace(p.lineageName)?p.lineageName:"未知物种";
    private Dictionary<string,SpeciesAtSite> Snapshot()
    {
        var result=new Dictionary<string,SpeciesAtSite>();
        if(simulation==null)return result;
        foreach(BlockInfo block in simulation.ReadBlocks().OrderBy(b=>b==null?"":b.name,StringComparer.Ordinal))
        {
            if(block==null || block.community==null)continue;
            foreach(PopulationData p in block.community)
            {
                if(p==null || p.speciesAmount<=0)continue;
                string species=SpeciesKey(p);
                if(string.IsNullOrEmpty(species))continue;
                string key=block.name+"|"+species;
                if(!result.TryGetValue(key,out var value))
                    result[key]=value=new SpeciesAtSite{key=key,site=block.name,species=species,name=DisplayName(p)};
                value.count+=p.speciesAmount;
                float k=p.carryingCapacity;
                if(k>0 && !float.IsNaN(k) && !float.IsInfinity(k))value.capacity+=k;
            }
        }
        return result;
    }
    private void AfterDay(int day)
    {
        ordinal=0;
        var current=Snapshot();
        var totals=current.Values.GroupBy(v=>v.species).ToDictionary(g=>g.Key,g=>g.Sum(v=>v.count));
        var oldTotals=previous.Values.GroupBy(v=>v.species).ToDictionary(g=>g.Key,g=>g.Sum(v=>v.count));
        foreach(var group in previous.Values.Where(v=>v.count>0 && !current.ContainsKey(v.key))
                     .GroupBy(v=>v.species).OrderBy(g=>g.Key,StringComparer.Ordinal))
        {
            bool global=!totals.TryGetValue(group.Key,out int total) || total==0;
            if(global)
            {
                var first=group.First();
                string message=group.Count()>1 ? "「"+first.name+"」已在整个农场灭绝。" :
                    "最后一群「"+first.name+"」在"+SiteLabel(first.site)+"灭绝了。";
                Emit(day,EcologicalEventType.GlobalExtinction,first,null,0,message);
            }
            else foreach(var lost in group.OrderBy(v=>v.site,StringComparer.Ordinal))
                Emit(day,EcologicalEventType.LocalExtinction,lost,null,0,
                    SiteLabel(lost.site)+"观察到「"+lost.name+"」种群灭绝。");
        }
        foreach(var lost in previous.Keys.Where(k=>!current.ContainsKey(k)).ToArray())
        {
            crashes.Remove(lost);
            crises.Remove(lost);
        }
        foreach(var v in current.Values.OrderBy(v=>v.key,StringComparer.Ordinal))
        {
            bool wasHere=everHere.Contains(v.key);
            if(wasHere && !previous.ContainsKey(v.key))
                Emit(day,EcologicalEventType.SpeciesReappeared,v,null,0,
                    "消失的「"+v.name+"」重新出现在"+SiteLabel(v.site)+"。");
            everHere.Add(v.key);
            CheckCrash(day,v);
            CheckCrisis(day,v);
        }
        foreach(var evolution in evolutions.Where(e=>e.Day==day)
                     .OrderBy(e=>e.TargetSpecies==null?"":e.TargetSpecies.SpeciesId,StringComparer.Ordinal))
        {
            string species=evolution.TargetSpecies==null?evolution.ResultSpeciesId:evolution.TargetSpecies.SpeciesId;
            if(string.IsNullOrEmpty(species) || oldTotals.ContainsKey(species) || !totals.ContainsKey(species))continue;
            var v=current.Values.FirstOrDefault(x=>x.species==species && x.site==evolution.TargetBlock?.name);
            if(v!=null)Emit(day,EcologicalEventType.NewSpecies,v,null,0,
                "新物种「"+v.name+"」出现在"+SiteLabel(v.site)+"！");
            oldTotals[species]=totals[species];
        }
        foreach(var migration in migrations.Where(m=>m.Day==day)
                     .OrderBy(m=>m.SourceBlock?.name,StringComparer.Ordinal)
                     .ThenBy(m=>m.TargetBlock?.name,StringComparer.Ordinal))
        {
            string species=FindSpeciesForPopulationId(migration.TargetBlock,migration.ResultPopulationId);
            var v=current.Values.FirstOrDefault(x=>x.site==migration.TargetBlock?.name &&
                x.species==species);
            if(v==null)continue;
            Emit(day,EcologicalEventType.SuccessfulMigration,v,migration.SourceBlock?.name,migration.Amount,
                migration.Amount+" 只"+v.name+"从 "+SiteLabel(migration.SourceBlock?.name)+
                "迁入 "+SiteLabel(v.site)+"。");
        }
        previous.Clear();foreach(var pair in current)previous.Add(pair.Key,pair.Value);
        migrations.Clear();evolutions.Clear();
    }
    private static string FindSpeciesForPopulationId(BlockInfo block,string id)
    {
        if(block==null || block.community==null)return "";
        foreach(var p in block.community)
            if(p!=null && p.PopulationId==id)return SpeciesKey(p);
        return "";
    }
    private void CheckCrash(int day,SpeciesAtSite v)
    {
        if(!crashes.TryGetValue(v.key,out var state))crashes[v.key]=state=new CrashState();
        if(state.past.Count==crashWindowDays)
        {
            int tenDaysAgo=state.past.Peek();
            bool severe=tenDaysAgo-v.count>=crashMinimumLoss &&
                v.count<=tenDaysAgo*(1f-crashMinimumFraction);
            if(!severe)state.armed=true;
            if(severe && state.armed && day-state.lastDay>=crashCooldownDays)
            {
                Emit(day,EcologicalEventType.PopulationCrash,v,null,0,
                    SiteLabel(v.site)+"的「"+v.name+"」种群数量急剧下降！");
                state.armed=false;state.lastDay=day;
            }
            state.past.Dequeue();
        }
        state.past.Enqueue(v.count);
    }
    private void CheckCrisis(int day,SpeciesAtSite v)
    {
        if(!crises.TryGetValue(v.key,out var state))crises[v.key]=state=new CrisisState();
        if(v.capacity<=0){state.days=0;return;}
        float ratio=v.count/v.capacity;
        if(ratio<1f){state.days=0;state.active=false;}
        else if(ratio>crisisCapacityRatio)
        {
            state.days++;
            if(state.days>=crisisConsecutiveDays && !state.active)
            {
                state.active=true;
                Emit(day,EcologicalEventType.CapacityCrisis,v,null,0,
                    SiteLabel(v.site)+"的「"+v.name+"」正面临生态承载危机！");
            }
        }
        else state.days=0;
    }
    private void Emit(int day,EcologicalEventType type,SpeciesAtSite source,string origin,int count,string message)
    {
        int sequence=ordinal++;
        Bus.Publish(new EcologicalEventData{eventId=day+":"+sequence,gameDay=day,sequence=sequence,type=type,
            speciesId=source.species,sourceBlockId=origin??source.site,targetBlockId=source.site,
            speciesName=source.name,populationCount=count,message=message});
    }
    private static string SiteLabel(string site)
    {
        if(string.IsNullOrEmpty(site))return "未知地点";
        string[] parts=site.Split('_');
        if(parts.Length>=4 && int.TryParse(parts[1],out _) && int.TryParse(parts[2],out _))
            return string.Join("_",parts.Skip(3))+" ("+parts[1]+","+parts[2]+")";
        return site;
    }
}
