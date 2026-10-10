using System;
using System.Collections.Generic;

namespace DarwinFarm.Environment
{
    // Terrain-type adjacency is a design map, not a recipe or a restriction on elevation.
    // The environment classifier remains responsible for the actual terrain at any height.
    public static class TerrainTransitionGraph
    {
        private static readonly IReadOnlyList<TerrainKind> grassland = Array.AsReadOnly(new[]
            { TerrainKind.Desert, TerrainKind.Forest, TerrainKind.Rainforest, TerrainKind.Tundra });
        private static readonly IReadOnlyList<TerrainKind> desert = Array.AsReadOnly(new[]
            { TerrainKind.Grassland, TerrainKind.Rainforest, TerrainKind.Volcano });
        private static readonly IReadOnlyList<TerrainKind> forest = Array.AsReadOnly(new[]
            { TerrainKind.Grassland, TerrainKind.Rainforest, TerrainKind.Tundra, TerrainKind.Highland });
        private static readonly IReadOnlyList<TerrainKind> rainforest = Array.AsReadOnly(new[]
            { TerrainKind.Grassland, TerrainKind.Desert, TerrainKind.Forest });
        private static readonly IReadOnlyList<TerrainKind> tundra = Array.AsReadOnly(new[]
            { TerrainKind.Grassland, TerrainKind.Forest, TerrainKind.Highland, TerrainKind.SnowMountain });
        private static readonly IReadOnlyList<TerrainKind> highland = Array.AsReadOnly(new[]
            { TerrainKind.Forest, TerrainKind.Tundra, TerrainKind.SnowMountain, TerrainKind.Volcano });
        private static readonly IReadOnlyList<TerrainKind> snowMountain = Array.AsReadOnly(new[]
            { TerrainKind.Tundra, TerrainKind.Highland });
        private static readonly IReadOnlyList<TerrainKind> volcano = Array.AsReadOnly(new[]
            { TerrainKind.Desert, TerrainKind.Highland });

        public static IReadOnlyList<TerrainKind> Neighbors(TerrainKind terrain)
        {
            switch (terrain)
            {
                case TerrainKind.Grassland: return grassland;
                case TerrainKind.Desert: return desert;
                case TerrainKind.Forest: return forest;
                case TerrainKind.Rainforest: return rainforest;
                case TerrainKind.Tundra: return tundra;
                case TerrainKind.Highland: return highland;
                case TerrainKind.SnowMountain: return snowMountain;
                case TerrainKind.Volcano: return volcano;
                default: throw new ArgumentOutOfRangeException(nameof(terrain));
            }
        }

        public static bool AreAdjacent(TerrainKind from, TerrainKind to)
        {
            foreach (TerrainKind neighbor in Neighbors(from))
                if (neighbor == to) return true;
            return false;
        }
    }
}
