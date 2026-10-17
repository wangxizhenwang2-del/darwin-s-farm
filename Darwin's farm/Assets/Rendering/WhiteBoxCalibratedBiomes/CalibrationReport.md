# WhiteBox biome visual calibration

Target: 15 × 15 ground footprint inside a 17 × 17 WhiteBox tile. Calibration uses the selected ground mesh's heading and centre. Foliage bounds and neighbouring demonstration tiles do not affect scale or origin. Terrain collision and navigation remain in ColorSurface.

| Biome | Source | Ground samples | Native particle systems |
| --- | --- | ---: | ---: |
| Forest | Forest_Moebius.unity / Forest_no_edge | 841 | 3 |
| Desert | Desert_Moebius.unity | 841 | 4 |
| Grassland | Grassland_Moebius.unity / Land1 | 841 | 3 |
| Highland | Highland_Colored.unity | 841 | 4 |
| Rainforest | RainForest.unity / Rain_Forest_no_edge | 841 | 4 |
| Tundra | Tundra_Colored.unity | 841 | 4 |

Forest and rainforest use a single authored tile. Other demonstration geometry is disabled in the adapters. All 22 native particle systems retain their authored materials and controllers; emission meshes are cropped into tile coordinates and projected onto ColorSurface at runtime. Preview/probe instances leave the rigs inactive.

Regenerate with `Tools/Darwin's Farm/Set up biome artwork`. Palette samples are written to `.utmp/biome-rollout/calibration.md`. See [BiomeArtwork.md](BiomeArtwork.md) for current integration and validation details.

The WhiteBox scene binds all eight biome wrappers to `MapTileColorTransitionSystem`.
At runtime the wrapper follows the tile root's 90-degree rotation. The authored
ground mesh renders directly with Vincent's original shader and every material
slot. Split normals, UVs, tangents and vertex masks remain unchanged; the full
interior relief is preserved. A readable copy of Highland's painted mesh is
baked in the adapter folder because its original asset is not CPU-readable.
`ColorSurface` combines the exact authored collision shape with a one-metre
joining rim. The rim reuses Moebius shading, without a PBR replacement.
Each retained decoration is projected onto the resulting surface. Forest
trees and large stones, grassland trunks and large props, desert large props,
and highland mountains use separate simplified obstacle boxes and NavMesh
`Not Walkable` volumes. The visual meshes have no active collision and are on
Ignore Raycast; `ColorSurface` remains the only terrain ground collider. The
legacy relief strength/cap controls apply only when authored artwork is disabled.
