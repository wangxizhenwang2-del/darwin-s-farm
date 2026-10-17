# Automatic Scene preview and improved lava bombs

The previous wind-based biome controllers already subscribed to EditorApplication.update. The new native mountain ParticleSystems lacked that editor simulation hook; their shaders were not restricted to the Game camera.

`Runtime/Controllers/MoebiusParticlePreview.cs` now provides the same workflow for explicitly assigned native particle systems. All update, warmup and repaint code is inside UNITY_EDITOR. It runs without particle selection, avoids persistent assets/Play Mode, advances from elapsed editor time, pauses with Animate Edit Mode Preview, and clears simulated preview state before Play Mode. It does not serialize particle time or dirty the scene. Volcano warms up to 1.8s so an eruption is visible on opening; snow warms up to 6s for an established snowfall.

Both existing VFX prefabs have this component. Main mountain scene/prefab placement was preserved, including concurrent user edits to the main Volcano prefab.

Lava bombs now use three irregular, rounded solid rock meshes with 80 visible facets, dark cooled crust and two bent molten fracture bands. Small coplanar triangles select seven flat palette colors using the existing Moebius NPR shader; seams are actual surface color rather than overlaid geometry, so they introduce no Z-fighting. The technical palette is 7 x 1 pixels with point filtering. Each mesh has 1,280 triangles; no extra shader or draw layer is required.

The two bursts now emit 16–22 and 6–9 larger rocks instead of the original dense spray of tiny orange shards. Size: 0.30–0.55m; speed: 5.4–7.4m/s; gravity multiplier: 1. The native 12s loop, embers, cloud plume and surface effects remain in place.

Validation:

- Actual non-batch Unity Editor, not Play Mode: both rigs advance automatically without selection and contain visible particles; continuous time progression and pause toggle checked; scenes remain clean. See EditModeReport.txt.
- Native Play Mode after adding preview: eruption starts automatically, becomes quiet, repeats next cycle; both snow layers start normally. See PlayReport.txt.
- Updated lava rocks reviewed in the actual rendered closeup and wide sample. Existing shader/material checks and deterministic native simulations pass with zero logged errors. See RefinementReport.txt.
- Resource GUID/reference audit and preservation of concurrent user changes: AssetAudit.txt.

Authoring/validation helpers stay outside Assets in Tools/MoebiusValidation/Editor. No extra Editor menu was added.
