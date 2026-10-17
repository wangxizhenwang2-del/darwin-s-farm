# Snow Mountain and Volcano environment details

Added to the existing main biome prefabs on 2026-10-17. Existing sample scenes inherit these additions without being rewritten.

Snow Mountain now includes five snow-covered conifers, capped boulder clusters and loose stones, three ice outcrops, low snow drifts and a small frozen pool. There are 31 editable prop instances, grouped at the perimeter and foot of the mountain. The main summit stays dominant and the foreground retains open snow.

Volcano now includes three basalt column groups, dark rock clusters, irregular cooled crust plates, two charred snags and two small geothermal steam vents. There are 26 editable prop instances. Placement respects the existing lava-lake outlet bounds and retains the exposed lava flow. Current foundation material choices, including scene overrides, are preserved.

## Organization

Both main prefabs contain an **Environment Details** child. Move, rotate, scale or remove individual nested prop instances there. The parent compensates for the large original import scale so prop dimensions are in metres.

- `Samples/Biomes/Prefabs/Props/`: reusable prop prefabs.
- `Samples/Biomes/Meshes/MountainDressing/`: lightweight static meshes.
- `Samples/Biomes/Materials/MountainDressing/`: shared NPR palettes and a steam material.

Rock silhouettes reuse the existing Tundra Rock_01/02/03 geometry, normalized into separate meshes and given snow/rock surface assignments. Original Tundra assets are unchanged. Other props use baked meshes with the existing Moebius NPR shader. Steam reuses the existing dust atlas/shader and the existing editor particle-preview component. There are no new shaders, renderer features, runtime spawning scripts or menu entries.

The additions are visual decoration; the frozen pool is not a new gameplay water tile. No gameplay/navigation logic was changed.

## Validation

Native Unity 6000.2.9f1 / URP 17.2, isolated project using the current project prefabs, sample scenes, original models and renderer settings.

- Before/after captures visually inspected using the existing scene cameras.
- Original renderer mesh/material bindings preserved during authoring: Snow Mountain 3, Volcano 5.
- Added static geometry: Snow Mountain 2,633 triangles; Volcano 2,004 triangles. This is geometry accounting, not a frame-rate benchmark.
- All new material/shader and mesh references load; no missing scripts or logged errors.
- Native Play Mode: the original eruption starts, settles, and repeats; both snowfall layers run. Ambient vent steam is separate from the eruption-cycle checks.
- Source hash audit: only the two main mountain prefabs changed among pre-existing assets. Existing scenes, camera placement, original shaders/materials, VFX rigs and other biome assets were not overwritten. New GUID/reference checks passed.

The offline authoring helper is outside Assets at `Tools/MoebiusValidation/Editor/MoebiusMountainDressing.cs` and requires a marked isolated Temp fixture.
