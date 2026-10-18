# Moebius Skybox implementation and validation

> Historical first version. The current single clear-day sky and validation are in `ClearSky/Validation.md`. The warm, overcast and twilight materials and background mountains described below have been retired.

Completed in the existing Unity 6000.2.9f1 / URP 17.2.0 project. The implementation uses the standard skybox path and the existing Moebius renderer; no existing shader, renderer, material or gameplay scene was changed.

## Deliverables

Full architecture, parameter guide, file list, setup workflow, fog / reflection behavior and biome palette suggestions:

`Assets/Rendering/Moebius/Samples/Skybox/README.md`

Main scene: `Assets/Rendering/Moebius/Samples/Skybox/MoebiusSkyboxDemo.unity`.

Presets: `Assets/Rendering/Moebius/Content/Skybox/Materials/{IllustratedDaylight,GoldenHour,PastelOvercast,IllustratedTwilight}.mat`.

The final reference-driven design uses clean five-region color, four fuller flat-bottom cloud banks plus two thin cloud strata around the sphere, restrained contour ink, larger illustrated sun discs in the warm / twilight presets, and optional two-layer distant ridge silhouettes. All presets share one shader.

## Actual checks

Native Unity ran in an isolated project using the main project's rendering assets and pipeline configuration. Final source files and generated assets were checked against the files installed in the main project.

| Check | Result |
| --- | --- |
| C# compile, including custom ShaderGUI | Passed |
| Final shader compile and native rendering | Passed, Direct3D 12 / RTX 5070 Ti Laptop, zero sky shader diagnostics |
| Earlier D3D11 native validation | Passed before the final reference-driven cloud/ridge revision; final revision tested with D3D12 |
| Shared single shader / one pass | Passed |
| Forest sample with existing Moebius ink renderer | Rendered; original biome assets and lights retained |
| Four palettes | Rendered |
| Camera translation by (1200,350,-800) | Mean normalized RGB difference about 1.2e-8 |
| Full 360-degree return | Passed pixel comparison |
| Cardinal directions / zenith / nadir | Rendered; visually inspected sampled views, no longitude or polar seam seen |
| Continuous cloud movement | 1/60-second RGB difference about 2.5e-6; 20-second difference about 0.00145, without frame-stepped random seeds |
| Main-light sun direction | Passed before/after light rotation comparison |
| Manual sun direction | Matches synchronized direction |
| Sun independently disabled | Passed |
| Perspective and orthographic cameras | Rendered |
| Existing Moebius renderer and standard PC renderer | Rendered |
| Optional stepped gradient / Unity fog-color matching | Rendered |
| Ambient mode / ambient color / reflection intensity / fog flag | Preserved when assigning sky |
| Native Play Mode | Rendered; GPU time advances and scene material remains assigned (see PlayReport.txt) |

`Report.txt` contains the native final edit-mode assertions. `PlayReport.txt` records the final Play Mode run. Screenshots are actual Unity renders, not image-generation mockups.

## Limits and manual follow-up

- No direct manual interaction with the user's live Scene View or Material Inspector was performed. UI code compiled, while Scene View sky visibility / Always Refresh controls still depend on the user's editor settings.
- Mobile, XR, baked/prefiltered reflection quality and GPU frame-time profiling on target hardware remain unverified. No claim of a measured GPU performance improvement is made.
- No Buto installation was found. An old OasisFog profile entry has density zero; the current Moebius renderer has only its ink feature. Real volumetric fog integration could not be exercised.
- The current object NPR shader does not consume Unity built-in fog. Sky fog-color matching and background ridge atmosphere do not add distance fog to existing scene meshes. The two background ridges are optional infinitely distant illustrations, not 3D terrain.
- Existing environment lighting and reflection resources are not rebuilt automatically. For a deterministic offline sky capture, use a manual sun direction matching the scene light; a capture camera without a visible URP main light otherwise uses the manual fallback direction.
- Native logs also report the project's pre-existing Moebius NPR debug Enum material-drawer warning (13 entries). This is unrelated to the new sky shader and was left unchanged. No sky shader warnings or exceptions occurred in the successful final run.

No additional game-time GameObjects, camera managers, textures, RenderTextures, full-screen passes, reflection update loops or Renderer Features are required by the skybox. The temporary offscreen targets used for screenshots belong only to the validation tooling.
