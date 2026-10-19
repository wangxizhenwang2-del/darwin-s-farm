# Billboard Swimming and Flight

Open `BillboardSwimFlight.unity`. The scene previews in Edit Mode and runs in Play Mode.

- Swimming: the existing 鹿游 (26 frames at 7 fps) and 鲨鹿 (23 frames at 7 fps) common animation sets, with a waterline, curved wake strokes and detached expanding broken ripples.
- Flight: the existing 鹿飞 movement sequence (8 frames at 8 fps), with continuous flight routes, gentle bob/bank and curved air strokes. Two instances use different phases and sizes.
- All animals remain camera-facing SpriteRenderer billboards, including Scene View. No 3D animal models or newly generated animation artwork are used.

Select `Billboard Swimming and Flight`: `Animate In Edit Mode` controls the live editor preview; `Playing` controls the timeline; `Playback Speed` scales it. Disable Playing and change Preview Time to inspect a pose. Actor Route Center / Radius / Speed control the presentation routes. Immersion controls the swimming waterline. Effect line width, intensity and ripple parameters are on the corresponding effect objects.

These are presentation components only. The existing species data, animation assets, gameplay movement, UI and build scenes are unchanged. The demo uses the existing Moebius water, outline renderer and air/water sprite shaders, plus a dedicated stroke material. Four fixed-capacity transient effect meshes are recreated on load and released on disable.

Native Unity 6000.2.9f1 / URP 17.2 / DX12 validation passed: proper sequence references, frame/motion advancement, camera-facing orientation, visible effects-on/off image comparison and saved-scene Play Mode reload. Evidence and an 8 fps animated preview are in `Artifacts/AnimalMotion`. Mobile performance and manual operation of the live Editor have not been tested.
