# WhiteBox biome visual calibration

Target: 15 × 15 visual interior within the 17 × 17 tile. Bounds use enabled renderers. The lowest visible point is aligned to local Y=0. These wrappers are visual assets only; terrain collision and navigation remain in ColorSurface.

| Source | Original bounds X×Z | Original bottom Y | Scale factor | Calibrated bottom Y | Ground mesh top Y after scaling | Wrapper |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Desert_Moebius | 12 × 12 | 0 | 1.25 | 0 | 1.941 | Desert_WhiteBoxVisual.prefab |
| Forest_Moebius | 10.666 × 12 | 0 | 1.25 | 0 | 2.611 | Forest_WhiteBoxVisual.prefab |
| Grassland_Moebius | 11.518 × 12 | 0.031 | 1.25 | 0 | 2.541 | Grassland_WhiteBoxVisual.prefab |
| Highland_Colored | 17 × 17 | 0 | 0.882 | 0 | 1.842 | Highland_WhiteBoxVisual.prefab |

The WhiteBox scene binds these four wrappers to `MapTileColorTransitionSystem`.
At runtime the wrapper follows the tile root's 90-degree rotation. The source
ground mesh is sampled on a 0.5 m grid, smoothed, scaled to 55% with a 0.65 m
height cap, and faded to zero over the inner 2 m of the model edge. Its renderer
is hidden; `ColorSurface` uses that sampled relief as its only walkable ground.
Each retained decoration is projected onto the resulting surface. Decorations
outside the central tile are hidden. Forest
trees and large stones, grassland trunks and large props, desert large props,
and highland mountains use separate simplified obstacle boxes and NavMesh
`Not Walkable` volumes. The visual meshes have no active collision and are on
Ignore Raycast; `ColorSurface` remains the only terrain ground collider. The
strength, cap and edge blend are adjustable on the transition component.
