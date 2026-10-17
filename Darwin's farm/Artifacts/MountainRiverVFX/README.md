# River foam, periodic volcano eruptions and mountain snowfall

Implemented on Vincent-Branch, 2026-10-17. Existing scene edits and deleted documentation were preserved.

## Changes

- `Samples/Biomes/Materials/River/River_I_Water.mat` and `River_T_Water.mat`: foam already existed but was narrow/subtle. Width is now **0.52m** (previously 0.24m), intensity 1, ink strength 0.45, patch strength 0.95, animation speed 0.38, shore motion 0.19. The foam color is pale white-green. Existing local shoreline masks and open river connectors remain unchanged.
- `Samples/Biomes/Prefabs/Volcano_Moebius.prefab`: nested `Volcano VFX` rig fitted to the actual molten crater, with metre-scale compensation for the original model transform. Three native looping ParticleSystems emit faceted molten shards, small embers and rounded grey clouds using the existing Moebius NPR/particle shaders.
- `Samples/Biomes/Prefabs/SnowMountain_Moebius.prefab`: nested `Snow Mountain VFX` rig with continuous fine snow and larger drawn flakes. White/cool-blue palette reuses the existing Tundra snow atlas. Snow collides with the actual mountain/foundation and is killed outside the tile bounds. It does not accumulate or modify surface shading.
- New reusable rig prefabs, materials and meshes are grouped under `Content/VFX/Volcano/` and `Content/VFX/SnowMountain/`.

No runtime particle-motion controller, shader, renderer feature, default texture or menu item was added. Unity's native ParticleSystem modules drive emission, motion, rotation, gravity, collision and lifetime curves. `MoebiusParticlePreview` adds editor-only simulation/redraw under `UNITY_EDITOR`; it does not drive particles in the player. The offline generator is outside Assets: `Tools/MoebiusValidation/Editor/MoebiusMountainVFXSetup.cs`.

## Viewing and tuning

Open the existing River_Moebius, Volcano_Moebius or SnowMountain_Moebius scene. Their existing prefab instances inherit the changes automatically. The two particle rigs now animate automatically in Edit Mode without selection. Toggle `Animate Edit Mode Preview` on each rig's `MoebiusParticlePreview` component to pause/resume the editor preview. Initial warmup: Volcano 1.8s, Snow Mountain 6s. Play Mode starts its own native cycle after clearing preview state.

| Control | Current value | Where to edit |
| --- | --- | --- |
| River foam width | 0.52 metres | River I/T water materials, Foam Width |
| Volcano cycle | 12 seconds | Select all three children under Volcano VFX; Main > Duration |
| First eruption | 1 second after starting | Emission > Bursts |
| Burst window | 1.0–1.85 seconds per cycle | Each volcano system's Emission > Bursts |
| Molten shard speed | 5.4–7.4 m/s | Lava Bombs > Main > Start Speed |
| Molten shard size | 0.30–0.55m before mesh dimensions/curves | Lava Bombs > Main > Start Size |
| Ash rise speed | 0.8–1.2 m/s | Ash Plume > Main > Start Speed |
| Fine snow rate | 42 particles/s | Fine Snowfall > Emission > Rate over Time |
| Larger flakes rate | 7 particles/s | Larger Drawn Snowflakes > Emission > Rate over Time |
| Snow fall speed | Fine 1.05–1.55 m/s; large 0.8–1.1 m/s | Velocity over Lifetime > Y |

Keep all three eruption systems at the same Duration when adjusting the period. This is a visual eruption only; it does not deform terrain or damage gameplay entities. Invisible collision helpers use Ignore Raycast, matching existing biome VFX conventions. Volcano cap: 224 particles; snow cap: 576 particles. No measured frame-rate guarantee is implied by these caps.

## Validation

Native Unity 6000.2.9f1 / URP 17.2 in an isolated project with source prefabs, FBXs, settings and shared rendering assets.

- Actual scene captures reviewed: wider shoreline foam remains confined to real banks; snowflakes visible over the mountain; eruption shards/ink clouds fit the existing camera.
- Deterministic native simulation: volcano empty at 0.5s and 9.5s, active at 2.2s/4.5s, active again at 14.2s.
- Native Play Mode: automatic eruption starts, settles, repeats in the next cycle; both snow layers start automatically.
- Shader/material checks and scene missing-script checks passed; zero logged errors in Report.txt and PlayReport.txt.
- AssetAudit.txt verifies only the four intended existing assets changed, no source assets disappeared, and new GUIDs/references are valid. Existing scenes were not rewritten.
