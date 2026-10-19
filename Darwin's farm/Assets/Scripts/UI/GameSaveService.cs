using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DarwinFarm.Environment;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Three full-world slots. All Unity object access stays on the main thread.
// Files contain a versioned, checksummed payload; a prior copy survives an
// interrupted overwrite. No partial payload is accepted for loading.
public static class GameSaveService
{
    private const int Schema = 1;
    private const int MaximumBytes = 32 * 1024 * 1024;
    private static GameData pending;
    private static string pendingError;
    public static bool HasPending => pending != null;
    public static string ConsumePendingError() { string value=pendingError; pendingError=null; return value; }
#if UNITY_EDITOR
    public static string TestFolderOverride;
#endif
    private static string Folder =>
#if UNITY_EDITOR
        !string.IsNullOrEmpty(TestFolderOverride) ? TestFolderOverride :
#endif
        Path.Combine(Application.persistentDataPath, "Saves");
    private static string PathFor(int slot) => Path.Combine(Folder, "slot_" + slot + ".json");
    private static SaveAssetCatalog Catalog => Resources.Load<SaveAssetCatalog>("DarwinJournal/SaveAssetCatalog");

    [Serializable] private sealed class Envelope
    {
        public int schema;
        public string sha256, payload;
    }
    public sealed class SlotInfo
    {
        public bool exists, valid, backup;
        public int day;
        public string timestamp, error;
    }
    private sealed class GameData
    {
        public int schema;
        public string timestamp, scene, modelVersion;
        public SimulationTime.SaveState clock;
        public SimulationController.SaveState simulation;
        public EnvironmentWorld.SaveState environment;
        public List<EnvironmentTechnologyController.SavedChoice> technologies;
        public EcologyNewsDetector.SaveState news;
        public EcologyNewsHUD.SaveState newsHud;
        public TutorialFlowController.SaveState tutorialFlow;
        public List<TerrainKind> terrainUnlocks;
        public bool waterUnlocked;
        public string bioenergy, storage;
        public List<TileData> tiles;
        public List<SpeciesProgress> species;
    }
    private sealed class TileData
    {
        public int x,y,height,rotation;
        public string definition;
        public JObject block;
        public List<JObject> populations;
    }
    private sealed class SpeciesProgress
    {
        public string id;
        public bool unlocked, extinct;
        public int revealedTraits;
    }
    public static SlotInfo ReadSlot(int slot)
    {
        CheckSlot(slot);
        if (!File.Exists(PathFor(slot)) && !File.Exists(PathFor(slot)+".bak")) return new SlotInfo();
        try
        {
            GameData data=ReadBest(slot,out bool backup);
            ValidateAssets(data);
            return new SlotInfo { exists=true, valid=true, backup=backup, day=data.clock.day, timestamp=data.timestamp };
        }
        catch(Exception e) { return new SlotInfo { exists=true, valid=false, error=e.Message }; }
    }
    public static bool TrySave(int slot, out string error)
    {
        error=null;
        try
        {
            CheckSlot(slot);
            GameData data=Capture();
            string payload=JsonConvert.SerializeObject(data,Formatting.None);
            var envelope=new Envelope { schema=Schema, payload=payload, sha256=Hash(payload) };
            string json=JsonConvert.SerializeObject(envelope,Formatting.None);
            if(Encoding.UTF8.GetByteCount(json)>MaximumBytes)throw new IOException("存档超过安全容量上限。");
            Directory.CreateDirectory(Folder);
            string path=PathFor(slot), temporary=path+".tmp", backup=path+".bak";
            File.WriteAllText(temporary,json,new UTF8Encoding(false));
            Read(temporary); // Check what was actually written before replacing a slot.
            if(File.Exists(path))
            {
                try { File.Replace(temporary,path,backup); }
                catch(PlatformNotSupportedException)
                {
                    File.Copy(path,backup,true);
                    File.Copy(temporary,path,true);
                    File.Delete(temporary);
                }
            }
            else File.Move(temporary,path);
            return true;
        }
        catch(Exception e) { error=e.Message; Debug.LogError("Save failed: "+e); return false; }
    }
    // Captures the exact current save state for a test session's starting point.
    // Call only on the main thread, once when recording begins.
    public static string CaptureShareSnapshot() => JsonConvert.SerializeObject(Capture(), Formatting.None);
    public static bool TryPrepareLoad(int slot,out string error)
    {
        error=null;
        try { CheckSlot(slot); pending=ReadBest(slot,out _); ValidateAssets(pending); return true; }
        catch(Exception e) { pending=null; error=e.Message; return false; }
    }
    public static bool TryRestorePending(out string error)
    {
        error=null;
        if(pending==null) { error="没有待加载的存档。"; return false; }
        GameData data=pending; pending=null;
        try { Restore(data); return true; }
        catch(Exception e) { pendingError=e.Message; error=e.Message; Debug.LogError("Load failed: "+e); return false; }
    }
    public static bool TryDelete(int slot,out string error)
    {
        error=null;
        try
        {
            CheckSlot(slot);
            foreach(string path in new[]{PathFor(slot),PathFor(slot)+".bak",PathFor(slot)+".tmp"})
                if(File.Exists(path))File.Delete(path);
            return true;
        }
        catch(Exception e) { error=e.Message; return false; }
    }
    private static GameData Capture()
    {
        var grid=UnityEngine.Object.FindFirstObjectByType<MapGridManager>();
        var clock=UnityEngine.Object.FindFirstObjectByType<SimulationTime>();
        var simulation=UnityEngine.Object.FindFirstObjectByType<SimulationController>();
        var environment=UnityEngine.Object.FindFirstObjectByType<SimulationEnvironmentController>();
        var technology=UnityEngine.Object.FindFirstObjectByType<EnvironmentTechnologyController>();
        var account=UnityEngine.Object.FindFirstObjectByType<BioenergyAccount>();
        var economy=UnityEngine.Object.FindFirstObjectByType<PlayerEconomyController>();
        var news=UnityEngine.Object.FindFirstObjectByType<EcologyNewsDetector>();
        var hud=UnityEngine.Object.FindFirstObjectByType<EcologyNewsHUD>(FindObjectsInactive.Include);
        var tutorial=TutorialFlowController.Current;
        SaveAssetCatalog catalog=Catalog;
        if(grid==null||clock==null||simulation==null||environment==null||technology==null||
            account==null||economy==null||news==null||hud==null||catalog==null)throw new InvalidOperationException("存档组件尚未准备好。");
        var placed=grid.GetPlacedTiles().OrderBy(t=>t.Coordinate.x).ThenBy(t=>t.Coordinate.y).ToList();
        var ids=placed.ToDictionary(t=>t.Block,t=>Key(t.Coordinate.x,t.Coordinate.y));
        var codec=new SaveFieldCodec(catalog,ids,null);
        var data=new GameData {
            schema=Schema, timestamp=DateTime.UtcNow.ToString("o"),
            scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            modelVersion=SimulationController.ModelRandomVersion,
            clock=clock.CaptureSaveState(), simulation=simulation.CaptureSaveState(),
            environment=environment.CaptureSaveState(), technologies=technology.CaptureSaveState(),
            terrainUnlocks=environment.CaptureTerrainUnlocks(), waterUnlocked=environment.WaterIsUnlocked,
            news=news.CaptureSaveState(), newsHud=hud.CaptureSaveState(),
            tutorialFlow=tutorial != null ? tutorial.CaptureSaveState() : null,
            bioenergy=account.CaptureState(), storage=economy.CaptureStorageState(),
            tiles=new List<TileData>(), species=new List<SpeciesProgress>()
        };
        foreach(MapTileInstance tile in placed)
        {
            BlockInfo block=tile.Block;
            if(block==null||block.community==null)throw new InvalidOperationException("地块生态数据缺失。");
            var saved=new TileData { x=tile.Coordinate.x,y=tile.Coordinate.y,
                height=tile.HeightLevel,rotation=tile.RotationSteps,
                definition=catalog.TileId(tile.Definition),block=codec.Capture(block),
                populations=new List<JObject>() };
            foreach(PopulationData population in block.community)
                if(population!=null)saved.populations.Add(codec.Capture(population));
            data.tiles.Add(saved);
        }
        foreach(var entry in catalog.species)
            if(entry.asset!=null)data.species.Add(new SpeciesProgress { id=entry.id,
                unlocked=entry.asset.HasEverUnlocked,extinct=entry.asset.IsExtinct,
                revealedTraits=entry.asset.RevealedTraitMask });
        Validate(data);
        return data;
    }
    private static void Restore(GameData data)
    {
        ValidateAssets(data);
        var grid=UnityEngine.Object.FindFirstObjectByType<MapGridManager>();
        var clock=UnityEngine.Object.FindFirstObjectByType<SimulationTime>();
        var simulation=UnityEngine.Object.FindFirstObjectByType<SimulationController>();
        var environment=UnityEngine.Object.FindFirstObjectByType<SimulationEnvironmentController>();
        var technology=UnityEngine.Object.FindFirstObjectByType<EnvironmentTechnologyController>();
        var account=UnityEngine.Object.FindFirstObjectByType<BioenergyAccount>();
        var economy=UnityEngine.Object.FindFirstObjectByType<PlayerEconomyController>();
        var news=UnityEngine.Object.FindFirstObjectByType<EcologyNewsDetector>();
        var hud=UnityEngine.Object.FindFirstObjectByType<EcologyNewsHUD>(FindObjectsInactive.Include);
        if(grid==null||clock==null||simulation==null||environment==null||technology==null||
            account==null||economy==null||news==null||hud==null)throw new InvalidOperationException("场景缺少存档组件。");
        environment.BeginSaveRestore();
        SaveAssetCatalog catalog=Catalog;
        var remaining=new List<TileData>(data.tiles.Where(t=>t.x!=0||t.y!=0));
        MapTileDefinition savedOrigin=catalog.Tile(data.tiles.First(t=>t.x==0&&t.y==0).definition);
        if(!grid.TryRestoreInitialTile(savedOrigin))
            throw new InvalidOperationException("无法恢复初始地块。");
        if(!grid.TryGetTile(Vector2Int.zero,out MapTileInstance origin) ||
            catalog.TileId(origin.Definition)!=data.tiles.First(t=>t.x==0&&t.y==0).definition)
            throw new InvalidOperationException("初始地块与存档不一致。");
        while(remaining.Count>0)
        {
            int before=remaining.Count;
            for(int i=remaining.Count-1;i>=0;i--)
            {
                TileData tile=remaining[i];var coordinate=new Vector2Int(tile.x,tile.y);
                if(!grid.CanPlaceAt(coordinate))continue;
                if(!grid.TryPlaceTile(coordinate,catalog.Tile(tile.definition),tile.rotation))
                    throw new InvalidOperationException("无法重建存档地块。");
                remaining.RemoveAt(i);
            }
            if(remaining.Count==before)throw new InvalidOperationException("存档地块不连通。");
        }
        foreach(TileData saved in data.tiles)
        {
            var coordinate=new Vector2Int(saved.x,saved.y);
            if(!grid.TryGetTile(coordinate,out MapTileInstance tile))throw new InvalidOperationException("存档地块丢失。");
            if(tile.HeightLevel!=saved.height && !grid.TrySetTileHeight(coordinate,saved.height))
                throw new InvalidOperationException("无法恢复地块海拔。");
        }
        clock.RestoreSaveState(data.clock);
        environment.RestoreSaveState(data.environment);
        var placed=grid.GetPlacedTiles().ToDictionary(t=>Key(t.Coordinate.x,t.Coordinate.y));
        var links=placed.ToDictionary(p=>p.Key,p=>p.Value.Block);
        var codec=new SaveFieldCodec(catalog,null,links);
        foreach(TileData saved in data.tiles)
        {
            BlockInfo block=links[Key(saved.x,saved.y)];
            codec.Restore(saved.block,block);
            block.community=new List<PopulationData>();
            foreach(JObject record in saved.populations)
            {
                var population=new PopulationData(false);
                codec.Restore(record,population);
                block.community.Add(population);
            }
        }
        simulation.RestoreSaveState(data.simulation);
        technology.RestoreSaveState(data.technologies);
        foreach(SpeciesProgress progress in data.species)
            catalog.Species(progress.id).RestoreProgress(progress.unlocked,progress.extinct,progress.revealedTraits);
        bool legacy=data.terrainUnlocks==null;
        bool legacyWater=legacy && data.species.Any(progress=>progress.unlocked &&
            (catalog.Species(progress.id).SpeciesId=="fd1c574bfd1247b2933cdfbc54758cf8" ||
             catalog.Species(progress.id).SpeciesId=="b2afc73aa25c41b18fd8f5ed4f3bc62a"));
        environment.RestoreUnlocks(data.terrainUnlocks,data.waterUnlocked||legacyWater,legacy);
        if(!account.TryRestoreState(data.bioenergy)||!economy.TryRestoreStorageState(data.storage))
            throw new InvalidOperationException("经济数据无法恢复。");
        news.RestoreSaveState(data.news);
        hud.RestoreSaveState(data.newsHud);
        TutorialFlowController.Current?.RestoreSaveState(data.tutorialFlow);
    }
    private static GameData Read(string path)
    {
        if(!File.Exists(path))throw new FileNotFoundException("该槽位没有存档。");
        if(new FileInfo(path).Length>MaximumBytes)throw new IOException("存档文件过大。");
        var envelope=JsonConvert.DeserializeObject<Envelope>(File.ReadAllText(path,Encoding.UTF8));
        if(envelope==null||envelope.schema!=Schema||string.IsNullOrEmpty(envelope.payload)||
            !string.Equals(Hash(envelope.payload),envelope.sha256,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("存档版本或校验值不正确。");
        GameData data=JsonConvert.DeserializeObject<GameData>(envelope.payload);
        Validate(data);
        return data;
    }
    private static GameData ReadBest(int slot,out bool backup)
    {
        backup=false;
        string path=PathFor(slot);
        try { return Read(path); }
        catch(Exception original)
        {
            string second=path+".bak";
            if(!File.Exists(second))throw;
            try { GameData data=Read(second); backup=true; return data; }
            catch { throw new InvalidDataException("主存档及备份都无法读取。",original); }
        }
    }
    private static void Validate(GameData data)
    {
        if(data==null||data.schema!=Schema||data.clock==null||data.simulation==null||
            data.environment==null||data.technologies==null||data.news==null||data.newsHud==null||data.tiles==null||
            data.species==null||data.clock.day<0||data.clock.day!=data.simulation.day||
            data.clock.day!=data.environment.day||data.tiles.Count<1||data.tiles.Count>4096||
            data.tiles.Count(t=>t.x==0&&t.y==0)!=1||
            data.tiles.Select(t=>Key(t.x,t.y)).Distinct().Count()!=data.tiles.Count||
            data.tiles.Any(t=>t.height<0||t.height>2||t.rotation<0||t.rotation>3||
                string.IsNullOrEmpty(t.definition)||t.block==null||t.populations==null)||
            (data.terrainUnlocks!=null && data.terrainUnlocks.Any(t=>!Enum.IsDefined(typeof(TerrainKind),t)))||
            data.bioenergy==null||data.storage==null||
            data.modelVersion!=SimulationController.ModelRandomVersion)
            throw new InvalidDataException("存档结构与当前游戏版本不匹配。");
    }
    private static void ValidateAssets(GameData data)
    {
        Validate(data);
        if(Catalog==null)throw new InvalidOperationException("缺少存档资源目录。");
        foreach(TileData tile in data.tiles)Catalog.Tile(tile.definition);
        foreach(SpeciesProgress value in data.species)Catalog.Species(value.id);
        if(data.scene!=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
            throw new InvalidDataException("存档来自不同场景。");
    }
    private static string Key(int x,int y) => x+","+y;
    private static void CheckSlot(int slot)
    { if(slot<1||slot>3)throw new ArgumentOutOfRangeException(nameof(slot)); }
    private static string Hash(string value)
    {
        using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value)))
            .Replace("-",string.Empty).ToLowerInvariant();
    }
}
