# Billboard Swimming and Flight

Open `BillboardSwimFlight.unity`. The scene previews in Edit Mode and runs in Play Mode.

- Swimming: the existing 鹿游 (26 frames at 7 fps) and 鲨鹿 (23 frames at 7 fps) common animation sets, with a waterline, curved wake strokes and detached expanding broken ripples.
- Flight: the existing 鹿飞 movement sequence (8 frames at 8 fps), with continuous flight routes, gentle bob/bank and curved air strokes. Two instances use different phases and routes.
- Flight now uses tapered, partly broken trails, wingtip crescents synchronized to the animation cycle, and detached fading gusts. Its shared style is `Assets/Resources/Population/AnimalMotionVFX.asset`.
- All four actors use the same 3.5-unit reference silhouette height. Scale comes from each animation set's baked visible reference height, excluding transparent padding; frames retain one consistent scale throughout playback.
- All animals remain camera-facing SpriteRenderer billboards, including Scene View. No 3D animal models or newly generated animation artwork are used.

Select `Billboard Swimming and Flight`: `Animate In Edit Mode` controls the live editor preview; `Playing` controls the timeline; `Playback Speed` scales it. Disable Playing and change Preview Time to inspect a pose. Actor Route Center / Radius / Speed control the presentation routes. Immersion controls the swimming waterline. Effect line width, intensity and ripple parameters are on the corresponding effect objects.

These are presentation components only. Shared animation assets store a reference silhouette height also used by gameplay and the animation lab; gameplay proxy/body-size scaling still applies. The demo uses the existing Moebius water, outline renderer and air/water sprite shaders, plus the shared stroke material. Swimming retains its original demo mesh; flight uses a 726-vertex child mesh. Transient meshes are released on disable.

WhiteBox integration: `WhiteboxPopulation` automatically adds `MoebiusAnimalMotionStrokes` to air and water populations using their existing ecological niche. Ground animals do not receive it. No extra demo animals are inserted into normal gameplay. The effect follows measured positions rather than prescribing movement. Swimming generates widening wake lines and six staggered, distorted broken ripples on the water plane. Existing frame animations and reference silhouette sizing remain in use.

Select the MapGridManager object's `PopulationMovementController`: Settings contains Flight Effects / Swimming Effects and independent intensity controls. The shared AnimalMotionVFX asset exposes line width (relative to the visible animal height), pale/ink colors, trail lifetime, wingtip/gust strengths, and swimming wake/ripple timing. Suggested starting values are the asset defaults: width .018, intensity .85, trail 1.35 s, wake 1.8 s, ripple interval .55 s, lifetime 2.4 s, expansion .4 body heights/s. Stopping fades trails; existing swimming rings finish their life. Teleporting clears history to avoid long streaks. Each active animal adds one renderer using the same existing material; no per-frame mesh or material creation is needed.

Native Unity 6000.2.9f1 / URP 17.2 / DX12 validation passed: proper sequence references, frame/motion advancement, camera-facing orientation, visible effects-on/off image comparison and saved-scene Play Mode reload. Evidence and an 8 fps animated preview are in `Artifacts/AnimalMotion`. Mobile performance and manual operation of the live Editor have not been tested.
