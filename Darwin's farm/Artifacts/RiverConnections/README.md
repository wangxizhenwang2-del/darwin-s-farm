# Automatic Moebius River Connections

Open one of the scenes in `Scenes/`:

| Scene | Demonstration |
| --- | --- |
| 01_Connected_Rivers | Matching L / cross mouths at the same water level join. |
| 02_River_To_Land_Capsule | A land neighbour closes the river with a semicircular water end and a rounded bank slope. |
| 03_Height_Difference_Waterfall | A 3m elevation difference creates the existing illustrated waterfall family. |
| 04_Multiple_Water_Heights | Four tiles at elevations 0, 2.5, 4.5 and 5.5m, with three downhill waterfalls and two incoming falls on the middle tile. |

## Use

Select the `River L`, `River Cross` or other **wrapper root** under `Automatic River Network`. Move that wrapper, rather than the model nested inside it. Terrain centres use 17m spacing. Matching mouth arrows must oppose each other; quarter-turn Y rotations work with the square grid. Keep wrapper world scale at 1. The imported model remains scaled inside the wrapper.

- **Transform Y** raises or lowers the entire terrain and its water.
- **Water Level** sets the local surface level above the wrapper origin; the default is 0.25m. Keep it inside the authored channel walls.
- A wrapper with no **Profile** is land; **Land Surface Height** identifies its top surface.
- Duplicate the connection-ready prefabs in `Prefabs/` underneath an existing Network to extend a scene.
- **Auto Refresh** updates connections in Edit and Play Mode. **Rebuild Connections** provides an explicit refresh.
- The Network Inspector shows the number of joined edges, rounded ends and waterfalls.

An unmatched mouth, an empty neighbour, or a neighbour without a reciprocal mouth gets a rounded end. The current profiles have 5m mouths and 17m footprints. Position tolerance is 0.06m and equal-height tolerance is 0.03m. The visual layer currently matches equal-width mouths (width difference at most 0.12m).

## Rendering

The original painted land, material assets, river prefabs and waterfall prefab remain intact. The network temporarily hides original water renderers and creates its own water geometry, rounded banks and local shoreline masks. Generated objects are transient; scenes retain source profiles, and rebuild on load. Disabling the network restores the original water renderers.

Only topology, transforms, water-level changes or an explicit rebuild regenerate geometry. Flow lines, foam, ripples and splash motion continue in the existing GPU shaders. Waterfall bodies reuse the existing material / HLSL, adapt their curved overhang to the height difference, and reuse all four splash silhouettes. Independent cutout impact layers use the receiving water mesh so several incoming falls can coexist without drawing foam over land.

The water shader has two hidden river-edge parameters. Their default widths are zero; ordinary water remains unaffected. Connected surfaces flatten their waves at the shared seam or curved waterfall lip, while their flow lines keep animating.

## Closed banks and seam repair

Capsule banks now include both end walls, the curved underwater wall and a bottom face. They form a closed volume with consistent outward winding and retain the existing grass / earth material slots.

`Foundation Top` applies to river wrappers as well as land. The L_New wrapper uses its measured outer ledge height (0.103569m), closing the exposed band above the old zero-height foundation. River foundations still have no roof, avoiding the former duplicate-surface Z-fighting. The offline wrapper builder derives this height from perimeter vertices.

Equal-height river joins now use a common mouth center, width and mean water height. Accepted small placement / water-level errors blend into that shared boundary over 0.65m. Shader displacement fades to zero at the boundary, preserving animated flow lines. Height differences beyond the existing tolerance still produce waterfalls. This adjustment does not move authored terrain models or merge their land geometry.

Before / after closeups, four-scene regression results and Play Mode checks for this correction are in `Artifacts/RiverConnections/SeamFix/`. Existing networks rebuild automatically after scripts reload; no shoreline rebake is needed. The guarded offline checks are in `Tools/MoebiusValidation/Editor/MoebiusRiverSeamValidation.cs`.

## Scope and validation

These are reusable **visual connection demos**. They do not change MapGridManager placement rules, WaterBodyTopology resource ledgers, population navigation, collision meshes, or simulate water volume. New river models need a corresponding authored profile. Gameplay topology / navigation integration is a separate task.

Validated in Unity 6000.2.9f1 / URP 17.2: shader compilation; scene save / reload; original-water pixel comparison; mouth rotation; displacement; elevation changes; land replacement; restoration; no repeated rebuild while stationary. Reports and renders are under `Artifacts/RiverConnections/`.

The guarded scene authoring / validation helper is `Tools/MoebiusValidation/Editor/MoebiusRiverConnectionScenes.cs`, outside Assets. It adds no top-level Editor menu entries.
