# Peacock Walk

Eight transparent 512 × 512 PNG frames, plus one 4096 × 512 sprite sheet. The peacock faces left. Frame 0 is the grounded starting pose; frame 3 contains the brief blink.

In Unity, import the individual PNGs as **Sprite (2D and UI)**, select `frame-0` through `frame-7` in order, and create an Animation Clip. Set **Samples = 8** and enable **Loop Time**. The eight frames then occupy exactly one second at 125 ms each. Use one sprite per 512 × 512 cell if slicing `PeacockWalkSheet.png` instead.

The GIF preview in `Artifacts/sprite-gen/peacock-walk/run/qa/walk.gif` is for visual review. Unity playback should use the PNG frames and the 8 fps timing above.
