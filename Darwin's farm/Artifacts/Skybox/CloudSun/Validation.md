# Illustrated clouds and sun — current revision

Updated the existing single `ClearSky.mat`; retained the clear blue sky, standard URP skybox path and existing scene lighting.

## Changes

- Cloud banks now use connected scalloped lobes with gentle coordinate warping. Direct noisy-distance thresholding and the old internal ellipse outlines were removed.
- Two restrained warm-colored masks form flat interior cloud layers; there is no PBR cloud lighting or volumetric marching.
- Sun angular radius defaults to 7.5 degrees. The lower disc washes toward the existing sky color, with an open horizontal cloud shelf passing in front.
- Added Sun Cloud-Shelf Strength / Height / Width. Strength zero restores an unobscured disc. Width controls ribbon thickness relative to the sun radius.
- Sun defaults to manual direction for composition. Existing Follow Main Directional Light remains available and was verified; scene light transforms are not changed.
- No new textures, GameObjects, Renderer Features, full-screen passes or game-time render targets.

## Native validation

Unity 6000.2.9f1 / URP 17.2.0, Direct3D 12, RTX 5070 Ti Laptop GPU, isolated test project.

- Final shader and Inspector code compiled without new shader warnings or errors.
- Native Play Mode render passed; time advances and the scene retains its sky material.
- Camera-translation comparison and full 360-degree return comparison passed.
- Continuous cloud movement, main-light sun synchronization, manual sun direction and independent sun disable passed.
- Sun shelf comparison verified a change in the lower disc while preserving the upper disc.
- Native-tested shader, includes, Inspector and material hashes match the installed main-project files.

`Sky_ClearSky.png` and `PlayMode.png` are actual Unity renders. `Sun_CloudShelf.png` / `Sun_WithoutShelf.png` show the sun control comparison. Full assertions are in `Report.txt` and `PlayReport.txt`.

Changed implementation: MoebiusSkybox.shader; MoebiusSkyboxInput.hlsl; MoebiusSkyboxAtmosphere.hlsl; MoebiusSkyboxClouds.hlsl; MoebiusSkyboxShaderGUI.cs; ClearSky.mat. Usage notes and the existing offline validation helper were updated.

Direct manual editor UI operation, mobile/XR and target GPU frame-time profiling remain unverified. Existing object fog and baked environment/reflection workflows are unchanged. The project's unrelated existing NPR Enum drawer warning remains outside this change.
