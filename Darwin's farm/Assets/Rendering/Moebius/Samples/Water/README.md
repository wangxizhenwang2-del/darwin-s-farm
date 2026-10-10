# Moebius Water

Open **MoebiusWaterDemo.unity** and enter Play Mode. The camera already uses the existing Moebius renderer. Water animation uses shader time; there is no animation component to attach.

The demo includes opaque pastel water, moving ink ripples, small vertex waves, an irregular cream shoreline, foam around rocks and optional foam contour ink.

## Add water to a scene

1. Open **Tools > Moebius > Water Setup**.
2. Set Width, Depth and Subdivisions Per Axis, then click **Create Water Surface**. Save the mesh. A unique water material is created alongside it.
3. Position the horizontal water surface. Keep Y Scale at 1 for the supplied wave-safe mesh bounds.
4. Select the water object. The window automatically fills **Water Renderer**. The water mesh defines the visible water region. Its open outer edges do not create foam.
5. Click **Update Shoreline**. Use 512 resolution and 4 metres distance range initially. The tool finds visible opaque ground/rocks and Unity Terrain in the same scene automatically. There is no Shore Objects list. It saves the mask and an independent material under `Assets/Rendering/Moebius/Content/Water` and assigns them to this surface. Later updates reuse the pair when the generated material is not shared by another loaded scene object.
6. Adjust the grouped material Inspector. Use Debug View to inspect Flow, Wave Height, Shore Distance, Foam or Normals.
7. Use the existing Moebius renderer on the camera for silhouette ink and paper treatment.

For an existing subdivided horizontal mesh, assign **Darwin/Moebius Water** or duplicate the default material at `Assets/Rendering/Moebius/Content/Defaults/Materials/MoebiusWater.mat`. The default material has no scene-specific shoreline mask: bake one to activate shore foam.

Flow Direction and Wave Direction use the first two vector components as world X and Z. Flow Speed and Ripple Drift Speed are independent: turning off Flow does not also turn off ripple drift. Each effect has a toggle and strength or amplitude. Color Variation is off by default.

The current defaults favor visible motion: Flow Speed = 0.5, Ripple Drift Speed = 0.12, Wave Speed = 1.4 and Foam Animation Speed = 0.28. Wave Height remains 0.055 metres to retain the quiet illustration silhouette. These defaults apply to the demo and default materials as well as newly created materials.

Ripple antialiasing uses derivatives of continuous coordinates before cell lookup and integrates subpixel stroke coverage. Neighboring persistent strokes are evaluated so a stroke does not vanish or change its seed at a cell boundary. Ripple Width defaults to 0.018. Animation uses continuous shader time, with no quantized frame steps.

**Ripple Wobble Strength** adds small, continuous hand-drawn changes to each stroke's curve (default 0.035). Set it to 0 for the original curve behavior. **Ripple Wobble Speed** controls how quickly the curve changes (default 3); 0 freezes the added shape. Each stroke uses its own phase, so the lines do not shake in unison. Wobble is independent of flow/drift and only changes ripple ink: it does not move the water mesh, shore foam or outline. No frame-based random reseeding or extra texture is used.

**Animate Edit Mode Preview** defaults to enabled. An Editor-only preview driver requests continuous redraws while a loaded scene contains enabled water, even with Water Setup closed. Its deadline advances on a regular schedule instead of resetting after late callbacks. You can pause it in Water Setup; that setting lasts for the Editor session. Play Mode uses the game's own frame rate. The window also displays Game camera render cadence and the longest frame gap; this measures render events, not GPU execution time. Reset the monitor after loading/compiling to separate startup pauses from sustained motion. A low actual frame rate can still make continuous animation look stepped.

## Shadows on water

The **Cast Shadows** material group controls **Receive Main Light Shadows**, **Shadow Ink Color**, **Shadow Ink Strength** and **Shadow Hatch Density**. Water now reads the existing URP main-light shadow map at its displaced world position and expresses cast shadows as anchored pen hatching, with a very slight tint. It preserves the pastel base instead of adding Lambert/PBR lighting. The demo, prefab and newly created water surfaces have Renderer Receive Shadows enabled; water's own casting remains off by default. For an existing surface, enable material shadow reception and Renderer Receive Shadows, and ensure the main directional light and the casting object's Renderer have shadows enabled. Shadow reception is realtime; moving a caster does not require a shoreline bake. Additional point/spot-light shadows are not sampled by this feature.

## Shoreline data

The shoreline is a static world-space distance field, not a color painting. R stores distance in the configured metre range; GB store its gradient for wave normals. No UVs or colliders are required. The baker can read imported static meshes even if runtime Read/Write is off.

Click **Update Shoreline** again after moving/resizing water, moving shores or changing water level. The supplied prefab uses this demo's mapping and must be updated when placed elsewhere. Place the water above submerged ground, intersecting the bank: geometry entirely below the water does not create foam. The baker reads the flat mesh's transformed vertex height, rather than assuming its object origin is the water level.

Only actual above-water ground/rock coverage counts as land. Empty space beyond the water mesh stays open, so rectangular borders and adjacent water seams do not generate artificial foam. Real banks just outside the mesh are still found within the padded mapping range. Unity Terrain is sampled by world height, including its holes. Transparent objects, other water surfaces, Moebius billboard characters, disabled objects and objects under an Animator or Rigidbody are excluded. Skinned meshes are not bake sources. Without actual land, the mask is neutral and the surface has no shoreline foam.

Projection uses above-water opaque geometry, so overhanging rock foam can extend beyond the exact water-height cross section. An ordinary opaque bridge or floating decoration with no Animator/Rigidbody can also be treated as land: automatic detection cannot infer what an object represents. For adjacent water surfaces, share wave/flow parameters and time phase to keep procedural movement continuous; update both surfaces after changing the region.

This shader is for horizontal lakes, rivers and coastal water. It does not simulate vertical waterfalls, reflection, refraction, buoyancy or transparent underwater objects. The water color stays independent of realistic lighting.

## Validation

Actual Unity renders, pass probes and the complete implementation report are in `Artifacts/MoebiusWater` at the project root. Run `Tools/ValidateMoebiusWater.ps1` for the full pass checks, or add `-AutoPreview` for main-light shadow reception, automatic region/Terrain checks, pinned-time water/foam draws and successive realtime camera renders after screenshot warmup. `ContinuousWaterSamples.csv` samples the actual surface shader at exact 1/60-second intervals with shader time fixed by a command buffer; repeat draws at the same time must be pixel-identical. This isolates procedural animation from camera refresh rate. It does not establish the frame rate of your active scene. Validation helpers remain outside Assets and are not part of the game build. The command leaves regenerated demo assets isolated for review; it does not overwrite existing scenes.

The demo grid has 25,921 vertices and 51,200 triangles. Lower subdivisions for large numbers of water surfaces or constrained hardware. No water-specific Renderer Feature, runtime RenderTexture, compute shader or per-frame C# updater is used.
