# Moebius Illustrated Waterfall

Open `MoebiusWaterfallDemo.unity` for the sample. The sheet now includes a quarter-circle lip that connects the horizontal inlet to the vertical waterfall. It retains the existing Moebius line style. Four closed splash meshes form two rising rear accents, one low wide hero mass, and a shallow horizontal surface foam fan. Impact foam and broken expanding arcs are integrated into the receiving Moebius Water shader. The two legacy overlay children remain inactive. All visual animation remains on the GPU; one small component tracks impact placement.

## Place in another scene

1. Drag `MoebiusWaterfall.prefab` into the scene. Its origin is near the lower waterline; local -Z points downstream. Open `MoebiusWaterfallDemo.unity` directly to inspect the existing sample. Sample-generation menus have been removed from the main editor.
2. Match **Sheet Size** to the sheet's unscaled width and vertical height. UV V follows surface distance from the bottom to the horizontal inlet. Keep the bottom close to the receiving water surface.
3. Open **Tools > Moebius > Waterfall > Bind Impact to Water**, select the sheet and receiving Moebius Water renderer, then press **Bind Impact** once.
4. This enables impact, stores fallback endpoints, copies the water flow direction and adds **Moebius Waterfall Impact Binding** to the receiving water. Save the scene. The component's **Waterfall Sheet** reference is the persistent association.
5. Moving, rotating or scaling the sheet or any parent now updates impact automatically in Edit and Play Mode. Editing Sheet Size also updates endpoints. No shoreline bake or repeated binding is necessary. To choose another receiving water, bind that renderer once; to replace the sheet, change the component's Waterfall Sheet reference.
6. Move the whole waterfall prefab to carry splash children with it. Moving only the sheet updates the impact field but does not reposition the independently placed splash meshes. Keep the surface foam crossing the waterline; adjust its height if water level changes.
7. Use the existing Moebius camera renderer. **Animate Edit Mode Preview** redraws animation without entering Play Mode. Endpoint tracking itself does not require this preview toggle.

## Curved upper lip

The sample's existing WaterfallSheet mesh has a 0.75-local-metre quarter-circle bend. The vertical bottom and impact line remain unchanged. The upstream endpoint is local `(0, 3, 0.75)` relative to the sheet, with an upward normal; the sample world inlet is at Y 6.2, Z 2.45. UpperWaterGrid has a matching central opening, avoiding overlapping water faces. Its vertices remain flat to keep this join pinned while shader flow and surface ripples continue to animate towards the fall.

UV V is normalized arc length. **Surface Length** is 6.428097 local metres and **Curved Lip Start V** is 0.8167269. These material values describe the mesh and coordinate its flow/highlight; changing them does not change its shape. **Upstream Water Color** matches UpperWater's Water Color. A pale crest lies on the bend and fades into the inlet color. For a flat custom sheet, leave Surface Length at 0 and Lip Start V at 1 to retain the original shading.

For another scene, align the upper water edge with the sheet's curved upstream endpoint, and match its color. Moving the whole prefab still updates lower impact automatically, but does not move a separately placed upper water object. The optional generator is archived in `Tools/MoebiusValidation/Editor/MoebiusWaterfallSetup.cs`, outside Unity's Assets folder. Its `RebuildSampleLip()` helper and `SampleLipRadius` remain available to isolated CLI validation; ordinary authoring uses the existing meshes and prefab.

## Impact defaults

| Parameter | Recommended value |
|---|---|
| Impact Radius / Half Depth | 2.1 m; width comes from the sheet endpoints |
| Turbulence Strength / Noise Scale | 0.95 / 0.85 |
| Foam Density / Motion Speed | 0.68 / 0.65 |
| Impact Foam Color / Ink Strength | cyan white `(0.90, 0.97, 0.96)` / 0.12 |
| Ripple Speed / Spawn Interval | 0.9 m/s / 1.1 s |
| Ripple Lifetime / Max Distance / Count | 6.6 s / 6 m / 6 |
| Ripple Width / Distortion / Noise Scale | 0.065 m / 0.2 m / 0.7 |
| Arc Breakup / Length Variation | 0.58 / 0.55 |
| Ripple Color / Intensity | pale cyan white `(0.86, 0.95, 0.95)` / 0.85 |
| Distance Fade / Lifetime Fade | 0.40 / 0.35 |

Ordinary water defaults to **Enable Waterfall Impact = Off**. The sample phase is 5 seconds. Increase Arc Breakup for fewer arcs. Actual lifetime is capped at Count × Spawn Interval; count is capped at eight. Animation remains continuous, with stable spatial noise and smoothly fading wave lifetimes.

## Splash shape and motion

Authored asymmetric Hermite contours replace the circle-union clouds. Swept rear crests are secondary; the pale near-white hero mass has a flatter lower boundary, varied upper crests and broad horizontal tips. The pale cyan-white foreground fan lies in XZ and is partially submerged, connecting the volume to surface impact foam. These are shallow closed 3D volumes, not transparent cards or particles. Existing mesh/child names and asset GUIDs are retained to preserve references.

| Layer | Motion Role | Pulse Speed / Amplitude | Wobble Strength | Fill |
|---|---|---|---|---|
| Back left | BackLift | 0.85 / 0.018 | 0.022 | `(0.73, 0.88, 0.89)` |
| Back right | BackLift | 0.85 / 0.018 | 0.022 | `(0.80, 0.92, 0.91)` |
| Hero mass | MainMass | 1.30 / 0.045 | 0.090 | `(0.97, 0.98, 0.95)` |
| Surface fan | SurfaceSpread | 1.10 / 0.055 | 0.055 | `(0.88, 0.96, 0.94)` |

Wobble Speed is 0.8 for the rear accents and 1.15 for the main/front layers; Internal Color Variation is 0; Secondary Color Strength is 0.28 (front 0.22). Phases are 0 / 1.7 / 3.4 / 5.1 seconds. BackLift pins the base while lifting the crest; MainMass has visible rolling crests above a planted base; SurfaceSpread advances and withdraws at its downstream edge while holding the upstream join. Existing custom materials default to Legacy motion. All passes use the same deformation.

## Shadows and limits

Sheet and splash receive URP main-light cast shadows as pen hatching: enabled, strength 0.55, density 8 lines/m by default. Depth, raw/oct-normal and shadow-caster passes share the visible deformation, including the curved mesh. The Moebius outline renderer is unchanged.

Tracking uses the sheet transform and Sheet Size, not mesh-vertex analysis or collision detection. The receiving shader projects the line into world XZ and ignores endpoint Y, so it is intended for roughly horizontal water. Existing waves follow a moving source as a whole; historical wave origins are not stored. One receiving renderer/material slot supports one impact line.

Live endpoints use a slot-0 MaterialPropertyBlock; shared material assets are not updated every frame. Other renderers sharing the material remain unaffected. The bound water renderer uses the ordinary URP rendering path rather than SRP Batcher; shader layouts remain compatible and no renderer, pass or draw call is added. Avoid another script taking ownership of the same slot's property block. Disabling/removing the binding restores the original block and saved material endpoints. The runtime binding script is required for automatic tracking in a build; the Editor window is only for initial setup.

The isolated validator is `Tools/ValidateMoebiusWaterfall.ps1`; reports and captures are in `Artifacts/MoebiusWaterfall`. It compiles all four shaders, checks pass silhouettes, shadows, ripple lifetimes, automatic edit/render tracking and actual Play Mode frames. It does not close the user's Unity Editor or modify other game scenes.
