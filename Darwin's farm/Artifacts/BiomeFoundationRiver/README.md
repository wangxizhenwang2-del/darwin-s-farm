# Biome foundations and River water

Implemented on Vincent-Branch, 2026-10-17.

- SnowMountain_Moebius and Volcano_Moebius now include a 17 x 17 x 0.3 metre slab, matching the existing Tundra slab footprint/thickness. Snow and ash colors use the existing Moebius NPR shader. Mountain, outline and lava meshes/materials remain intact.
- River's imported I/T tiles were 0.17 metres wide. A uniform 100x correction brings each to 17 metres. Original vegetation, painted banks, source FBX and UVs are preserved.
- Each channel has one new subdivided water mesh, extracted from its actual recessed bed triangles and extended across the perimeter ledge at open connectors. I: 9,600 triangles; T: 12,864 triangles. Two additional water renderers, using the existing Water shader and no additional renderer feature or runtime controller.
- Continuous flow, ink ripples, modest vertex waves, main-light shadow reception and animated shore foam reuse existing modules. Shoreline distance fields are 512 x 512, range 3 metres, baked from the raised bank geometry. Open connectors are not marked as land.
- WaterInput adds an opt-in local shoreline coordinate path; default remains world-space. This allows the River masks to follow translation/yaw rotation without rebaking. The existing Water Setup baker explicitly resets its output to world-space.
- Updated both mountain preview scenes and added `Assets/Rendering/Moebius/Samples/Biomes/Scenes/River_Moebius.unity`.

## Validation

Unity 6000.2.9f1 / URP 17.2, isolated native Unity project using the source Moebius assets, original FBXs, settings and packages.

- Native scene captures reviewed: foundations visible, original mountain/lava styling retained, both river shapes covered, foam on banks with open endpoints.
- Shader/material and mesh reference checks: zero errors (`Report.txt`).
- Shoreline-only GPU render before and after translation plus 90-degree yaw: mean normalized RGB difference 4.46e-9, below 0.002 tolerance.
- Native Play Mode: river image changes between early and 3-second captures; zero logged errors (`PlayReport.txt`). This checks animation execution, not a measured frame-rate guarantee.
- Existing asset hashes and new GUID references audited (`AssetAudit.txt`); no original assets deleted or unrelated existing assets changed. Git diff whitespace check passed.

## Usage and limits

Open the three biome scenes from `Samples/Biomes/Scenes`. Enter Play Mode to see river animation. The existing Animate Edit Mode Preview option also previews water while editing.

River materials live in `Samples/Biomes/Materials/River`. Useful controls: Water Color, Flow Speed, Ripple Drift Speed, Wave Height and Foam Width.

Keep prefab water horizontal and uniformly scaled. The local shore bake follows position/yaw; flow direction and animation dimensions remain world-space controls. Set Flow Direction after rotating a tile if needed. T uses one artistic current direction, not a branching flow simulation. Changing bank shape or water level requires a new shoreline bake; the existing Water Setup tool creates world-space masks, so bake after final scene placement.

The reproducible authoring/validation helper remains outside Assets in `Tools/MoebiusValidation/Editor/MoebiusBiomeFinishing.cs`; it is not compiled into the game or added to the main project's Editor menus.
