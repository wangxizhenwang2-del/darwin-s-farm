# Highland billboard animals

Open **Assets/Rendering/Moebius/Samples/Biomes/Scenes/Highland_Animals.unity** to see the five animals on the existing colored plateau. The original Highland_Colored scene is preserved.

Each animal uses a MeshFilter, MeshRenderer and the existing **Darwin/Moebius Billboard Character** shader. No movement script, billboard MonoBehaviour, Animator or additional Renderer Feature is added. Camera-facing orientation is done in the vertex shader, around the world Y axis, with the animal remaining upright.

The original artwork is copied unchanged to **Assets/Art/Animals/Textures/**. Material UV scale/offset trims transparent margins without cropping/repainting the PNG. Quad proportions use the visible artwork bounds, and the transform is positioned near the feet on the terrain. OceanDeer has a small 0.12-unit surface offset for its flowing fins.

The camera uses the same Moebius renderer as the plateau: existing screen outlines and paper processing apply to the animals as well. The animal materials also enable an illustration filter with softer color bands, warm paper tint, internal ink detail, subtle dark-area hatching and UV-anchored grain. Original PNG artwork remains unchanged; this is an adjustable material effect rather than 3D fur shading.

All five prefabs and their Highland scene instances cast and receive realtime shadows. The ShadowCaster pass clips the same texture alpha and uses the same trimmed UV region as the visible animal, so transparent margins do not cast a solid rectangular shadow. Shadows follow the upright camera-facing illustration. `Runtime/Rendering/MoebiusBillboardCameraGlobals.cs` supplies the active rendering camera before URP passes; its editor initializer also supports Scene view. This keeps orthographic and perspective shadows aligned with the visible billboard instead of turning toward the shadow camera. No per-animal runtime component is required.

## Reuse and adjustments

- Drag any of the five **Prefabs/** into your own scene. Place its Transform near the intended ground contact point.
- Scale X for width and Y for height. Keep Z scale close to X for conservative camera-facing culling bounds.
- **Materials/** contains each animal's material. Adjust Tint, Brightness, Alpha Cutoff or Received Shadow Strength as needed.
- **Moebius Illustration Filter / Style Strength** controls the full illustration treatment; set it to 0 to show the original image colors. Adjust Color Levels, Color Saturation, Internal Ink Strength, Dark Area Hatching and Paper Grain independently.
- Keep the MeshRenderer's **Cast Shadows** enabled and enable main-light shadows in the scene and URP asset. These shadows are the silhouette of a flat upright illustration, not volumetric animal geometry.
- Keep the material's Base Map scale/offset to preserve the fitted artwork region.
- **BillboardAnimalQuad.asset** stores conservative foot-anchored bounds so the object does not disappear early near camera edges. It uses standard quad vertices and UVs.
- The animals are static visual samples. They do not move, animate, or switch artwork for side/back directions.

Names: MushroomFox (灵感菇狸), Wolf (狼), PeacockSnakeLion (孔雀蛇狮), OceanDeer (鹿进化海洋), ForestDeer (鹿进化森林).

Validation screenshots and checks are in **Artifacts/HighlandAnimals/**. The one-off authoring script stays in the isolated temporary project rather than adding another tool to the main project.

Run **Tools/ValidateMoebiusAnimals.ps1** for shader/runtime checks, isolated alpha-cutout ground shadows, perspective/orthographic orbit checks, filter comparisons and the delivered Highland scene. Latest screenshots and report are copied to **Artifacts/AnimalRendering/**.
