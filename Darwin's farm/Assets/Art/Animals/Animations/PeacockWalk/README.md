# Peacock Walk

Eight transparent 512 × 512 PNG frames, plus one 4096 × 512 sprite sheet. The peacock faces left. Frame 0 is the grounded starting pose; frame 3 contains the brief blink.

These frames are now connected to the peacock SpeciesData through `PeacockAnimation.asset` and `Peacock.overrideController`. `PeacockIdle.anim` holds frame 0; this is a static idle pose, not a separate idle animation. `PeacockWalk.anim` loops all eight frames in one second (8 fps at the default 1.8 world units/second movement speed).

The shared `Common/PopulationLocomotion.controller` switches between Idle and Walk using `IsMoving`; `WalkSpeed` scales only the walking cycle. `PopulationAnimationDriver` measures actual horizontal movement, keeps facing stable near the camera axis, and changes only the sprite and horizontal flip. The movement proxy, billboard anchor and scale stay independent of animation. All eight frames use the full 512 × 512 canvas, centered pivots and 512 pixels/unit; do not crop or resize frames individually.

Species portraits and UI icons still use their original static illustrations. Species without a Population Animation asset keep their static world sprite. Niche descendants inherit their ancestor's animation alongside its illustration. For another animated species, create its clips and an override of the shared controller, create a Population Animation Set, and assign it on SpeciesData.

To regenerate the imported sprites and generated animation assets, use **Tools → Darwin's Farm → Animation → Rebuild Peacock Locomotion** outside Play mode. This rebuilds the generated controller and clips; do not use it to preserve manual edits to those assets. It does not save or modify scenes. For the live regression check, enter Play mode and run `PopulationAnimationChecks.Run()` in a disposable session, then inspect `PopulationAnimationChecks.Result`.

The GIF preview in `Artifacts/sprite-gen/peacock-walk/run/qa/walk.gif` is for visual review. Unity playback should use the PNG frames and the 8 fps timing above.
