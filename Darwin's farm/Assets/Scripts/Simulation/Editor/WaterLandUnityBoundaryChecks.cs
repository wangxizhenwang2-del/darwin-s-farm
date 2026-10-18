#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Editor fixture: validates physical routes and topology, not a playable water life cycle.
public static class WaterLandUnityBoundaryChecks
{
    [MenuItem("Tools/Darwin Farm/Validation/Water Land/Run Boundary Checks")]
    public static void RunBatch()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        SpeciesData waterSpecies = null, landSpecies = null, unlinkedSpecies = null;
        bool failed = false;
        try
        {
            BlockInfo a = Cell(scene, "A", 0, 0, false);
            BlockInfo w1 = Cell(scene, "w1", 1, 0, true);
            BlockInfo w2 = Cell(scene, "w2", 2, 0, true);
            BlockInfo b = Cell(scene, "B", 0, 1, false);
            BlockInfo w3 = Cell(scene, "w3", 1, 1, true);
            Link(a, w1); Link(w1, w2); Link(w1, w3); Link(b, w3);
            LinkWater(w1, w2); LinkWater(w1, w3);
            waterSpecies = ScriptableObject.CreateInstance<SpeciesData>();
            landSpecies = ScriptableObject.CreateInstance<SpeciesData>();
            unlinkedSpecies = ScriptableObject.CreateInstance<SpeciesData>();
            waterSpecies.baseEcologicalNiche = EcologicalNiche.Water;
            landSpecies.baseEcologicalNiche = EcologicalNiche.Land;
            unlinkedSpecies.baseEcologicalNiche = EcologicalNiche.Land;
            waterSpecies.evolutionTargets.Add(landSpecies);
            landSpecies.evolutionTargets.Add(waterSpecies);
            var fish = new PopulationData { species = waterSpecies,
                ecologicalNiche = EcologicalNiche.Water, speciesAmount = 100,
                bodyBiomass = 4000f, bodyBiomassInitialized = true,
                energyReserve = 200f };
            w3.community.Add(fish);
            GameObject root = new GameObject("WaterLand fixture");
            SceneManager.MoveGameObjectToScene(root, scene);
            SimulationController controller = root.AddComponent<SimulationController>();
            controller.SetBlocks(new List<BlockInfo> { a, w1, w2, b, w3 });
            controller.SetWaterEcologyEnabled(true);
            int heads = fish.speciesAmount;
            float stock = w1.plantBiomass + w2.plantBiomass + w3.plantBiomass;
            Snapshot(0, "initial", controller, a, w1, w2, b, w3);
            Route(controller, w3, fish, a, landSpecies, "NO_LOCAL_EDGE", "T01");
            Route(controller, w3, fish, b, landSpecies, "ROUTE_OK", "T03");
            Route(controller, w3, fish, b, unlinkedSpecies, "NO_GRAPH_EDGE", "T10 graph");
            var occupying = new PopulationData { species = landSpecies,
                ecologicalNiche = EcologicalNiche.Land, speciesAmount = 10 };
            b.community.Add(occupying);
            Route(controller, w3, fish, b, landSpecies,
                "TARGET_ALREADY_PRESENT", "T10 local target");
            b.community.Remove(occupying);
            SpeciesData realDeer = AssetDatabase.LoadAssetAtPath<SpeciesData>(
                "Assets/BiowebV46/Species/鹿-fd1c574b.asset");
            SpeciesData realSwimDeer = AssetDatabase.LoadAssetAtPath<SpeciesData>(
                "Assets/BiowebV46/Species/鹿游-103a527e.asset");
            Require(realDeer != null && realSwimDeer != null,
                "Real deer/swim-deer evolution assets must load");
            var realLand = new PopulationData { species = realDeer,
                ecologicalNiche = EcologicalNiche.Land, speciesAmount = 100 };
            a.community.Add(realLand);
            Route(controller, a, realLand, w1, realSwimDeer,
                "ROUTE_OK", "T14 real asset deer to swim-deer");
            Route(controller, a, realLand, w3, realSwimDeer,
                "NO_LOCAL_EDGE", "T14 real asset remote water");
            a.community.Remove(realLand);
            var deer = new PopulationData { species = landSpecies,
                ecologicalNiche = EcologicalNiche.Land, speciesAmount = 100 };
            a.community.Add(deer);
            Route(controller, a, deer, w1, waterSpecies, "ROUTE_OK", "T13 other water cell");
            Route(controller, a, deer, w3, waterSpecies, "NO_LOCAL_EDGE", "T14 remote water");
            a.community.Remove(deer);
            Require(fish.speciesAmount == heads &&
                w1.plantBiomass + w2.plantBiomass + w3.plantBiomass == stock,
                "T01 inspection must be read-only");
            fish.evolutionConversions.Add(new EvolutionConversionRecord
                { target = landSpecies, effectiveDays = 4 });
            Require(controller.MoveWithinWaterRegion(w3, w1, fish,
                controller.BuildWaterRegions(), 10),
                "T02 legal in-basin position move must succeed");
            fish = w1.community.Find(pop => pop != null &&
                pop.ecologicalNiche == EcologicalNiche.Water);
            Require(fish != null && fish.speciesAmount == heads,
                "T15 water position move conserves population count");
            Require(Mathf.Abs(fish.bodyBiomass - 4000f) < 0.001f &&
                Mathf.Abs(fish.energyReserve - 200f) < 0.001f,
                "T15 water position move conserves body and reserve");
            Require(fish.evolutionConversions.Count == 0,
                "T11 position move clears prior target qualification");
            Snapshot(10, "move-w3-to-w1", controller, a, w1, w2, b, w3);
            Route(controller, w1, fish, a, landSpecies, "ROUTE_OK", "T02 new shore");
            Route(controller, w1, fish, b, landSpecies, "NO_LOCAL_EDGE", "T02 old shore");
            w1.community.Remove(fish); w3.community.Add(fish);
            b.elevation = 2;
            Route(controller, w3, fish, b, landSpecies, "NO_LOCAL_EDGE", "T04 cliff");
            b.elevation = 0;
            Link(a, w3);
            Route(controller, w3, fish, a, landSpecies, "NO_LOCAL_EDGE", "T04 diagonal");
            w3.SetNeighbors(new List<BlockInfo> { w1, b });
            a.SetNeighbors(new List<BlockInfo> { w1 });
            b.SetNeighbors(new List<BlockInfo>());
            Route(controller, w3, fish, b, landSpecies,
                "NO_LOCAL_EDGE", "T04 missing reverse");
            b.SetNeighbors(new List<BlockInfo> { w3 });
            w3.SetWaterLinks(new List<BlockInfo>());
            WaterRegionMap regions = controller.BuildWaterRegions();
            Require(regions.GetRegion(w3) != regions.GetRegion(w1),
                "T05 one-sided water opening cannot share a basin");
            Require(w3.community.Contains(fish) && fish.speciesAmount == heads,
                "T05 topology change must not relocate population");
            Require(Mathf.Abs(w1.plantBiomass + w2.plantBiomass +
                w3.plantBiomass - stock) < 0.001f,
                "T05 topology split does not duplicate water stock");
            Snapshot(10, "split-w1-w3", controller, a, w1, w2, b, w3);
            Route(controller, w3, fish, b, landSpecies,
                "ROUTE_OK", "T12 shore survives unrelated water split");
            LinkWater(w1, w3);
            w3.elevation = 1;
            regions = controller.BuildWaterRegions();
            Require(regions.GetRegion(w3) != regions.GetRegion(w1) &&
                HabitatTopology.ChannelsConnect(w1, w3),
                "T06 height one is cross-region water channel");
            w3.elevation = 2;
            Require(!HabitatTopology.ChannelsConnect(w1, w3),
                "T07 height two blocks water channel");
            Debug.Log("WATER_LAND_UNITY_BOUNDARY_PASS seed=20261018 checks=25 day=10 fixture=editor");
        }
        catch (Exception error)
        {
            failed = true;
            Debug.LogException(error);
        }
        finally
        {
            if (waterSpecies != null) UnityEngine.Object.DestroyImmediate(waterSpecies);
            if (landSpecies != null) UnityEngine.Object.DestroyImmediate(landSpecies);
            if (unlinkedSpecies != null) UnityEngine.Object.DestroyImmediate(unlinkedSpecies);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        if (Application.isBatchMode) EditorApplication.Exit(failed ? 1 : 0);
        else if (failed) throw new Exception("Water/land boundary checks failed; see Console.");
    }

    private static BlockInfo Cell(Scene scene, string name, int x, int y, bool water)
    {
        GameObject root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, scene);
        MapTileInstance tile = root.AddComponent<MapTileInstance>();
        SerializedObject serialized = new SerializedObject(tile);
        serialized.FindProperty("coordinate").vector2IntValue = new Vector2Int(x, y);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        BlockInfo block = root.AddComponent<BlockInfo>();
        block.waterCoverage = water ? WaterCoverage.Lake : WaterCoverage.Land;
        block.elevation = 0;
        block.plantBiomass = 1000f;
        block.community = new List<PopulationData>();
        block.SetNeighbors(new List<BlockInfo>());
        return block;
    }

    private static void Link(BlockInfo a, BlockInfo b)
    {
        var left = new List<BlockInfo>(a.Neighbors) { b };
        var right = new List<BlockInfo>(b.Neighbors) { a };
        a.SetNeighbors(left); b.SetNeighbors(right);
    }

    private static void LinkWater(BlockInfo a, BlockInfo b)
    {
        var left = new List<BlockInfo>(a.WaterLinks) { b };
        var right = new List<BlockInfo>(b.WaterLinks) { a };
        a.SetWaterLinks(left); b.SetWaterLinks(right);
    }

    private static void Require(bool pass, string message)
    { if (!pass) throw new Exception(message); }

    private static void Snapshot(int day, string operation,
        SimulationController controller, params BlockInfo[] cells)
    {
        WaterRegionMap regions = controller.BuildWaterRegions();
        var state = new StringBuilder();
        foreach (BlockInfo cell in cells)
        {
            state.Append(cell.name).Append(":z=").Append(cell.elevation)
                .Append(":stock=").Append(cell.plantBiomass.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture));
            WaterRegion region = regions.GetRegion(cell);
            if (region != null)
            {
                var names = new List<string>();
                foreach (BlockInfo member in region.Members) names.Add(member.name);
                names.Sort(StringComparer.Ordinal);
                state.Append(":basin=").Append(string.Join("+", names));
            }
            foreach (PopulationData population in cell.community)
                if (population != null)
                    state.Append(":niche=").Append(population.ecologicalNiche)
                        .Append(",N=").Append(population.speciesAmount)
                        .Append(",M=").Append(population.bodyBiomass.ToString("R",
                            System.Globalization.CultureInfo.InvariantCulture))
                        .Append(",E=").Append(population.energyReserve.ToString("R",
                            System.Globalization.CultureInfo.InvariantCulture));
            state.Append(";");
        }
        string canonical = state.ToString();
        string hash;
        using (SHA256 sha = SHA256.Create())
            hash = BitConverter.ToString(sha.ComputeHash(
                Encoding.UTF8.GetBytes(canonical))).Replace("-", "").ToLowerInvariant();
        Debug.Log("WATER_LAND_STATE seed=20261018 day=" + day +
            " op=" + operation + " state=" + canonical + " sha256=" + hash);
    }
    private static void Route(SimulationController simulation, BlockInfo source,
        PopulationData population, BlockInfo target, SpeciesData species,
        string expected, string id)
    {
        string actual = simulation.InspectCrossHabitatEvolutionRoute(source,
            population, target, species);
        Debug.Log("WATER_LAND_ROUTE seed=20261018 day=0 id=" + id +
            " source=" + source.name + " target=" + target.name +
            " actual=" + actual + " expected=" + expected +
            " n=" + population.speciesAmount + " popId=" + population.PopulationId);
        Require(actual == expected, id + " expected " + expected + " got " + actual);
    }
}
#endif





\n