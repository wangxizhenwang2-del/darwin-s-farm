# Peacock walk QA

- Source: supplied transparent peacock PNG; GPT image sprite row, then canonical sprite-gen extraction and atlas composition.
- 8 extracted frames, 512 × 512 RGBA each; 4096 × 512 transparent sheet.
- Animation timing: 8 fps, 125 ms per frame, 1 second per loop in Unity.
- Visual review: all frames face left; fan remains open; both legs visible; first frame has both feet on the ground; frame 3 has one short blink. Leg poses alternate through the row. Frame 7 returns toward the starting stance.
- Automated inspection: 8 natural poses, no extraction errors or warnings. Adjacent full-frame mean absolute RGBA differences are 7.04–12.40; frame 7→0 is 11.24, within that range.
- Verdict: best-effort experimental walk. The pose sequence and seam read as a slow loop, though image generation cannot guarantee exact foot locking or mechanically precise neck and tail timing.
