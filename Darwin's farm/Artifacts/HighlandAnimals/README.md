# Highland animal test

Delivered scene: Assets/Rendering/Moebius/BiomeTests/Scenes/Highland_Animals.unity

Five original transparent illustrations are placed on the current colored plateau using the existing Moebius Billboard Character shader and renderer. No runtime scripts were added. Original PNG bytes are preserved; texture import settings and material UV fitting are used to avoid transparent margins affecting apparent scale.

The first placement preview showed overlapping illustrations; final positions were separated for readability. The camera has overview and closer screenshots, with another-angle capture to inspect camera-facing behavior. Animals retain their original drawing and painted shading, use a slight warm tint, and participate in existing outline/paper effects. No new animal hatching or anatomy was generated.

26 checks passed, with zero shader/runtime/test errors: original alpha, surface placement, shader-only object components, cropped UV bounds, camera rotation without changing object transforms, 3D depth occlusion, all five objects after scene reload, and persistent mesh culling bounds. Saved and reloaded overview renders are byte-identical. Seventeen delivered files and their .meta GUIDs match the verified isolated project.

The generated scene omits missing-script components inherited from previously removed prototype files; the source colored scene was not rewritten. No terrain materials, shared renderer materials or existing shader source was changed.

See Overview.png, Closeup.png, OtherAngle.png and Report.txt.
