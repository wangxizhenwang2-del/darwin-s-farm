# Capsule bank faces and river seams

## Causes and fixes

- The generated capsule previously had only a top surface and outside wall. Both lateral ends, the curved underwater edge and underside are now closed with outward-facing triangles. Existing two material slots are retained.
- L_New's outer model ledge sits roughly 0.103569m above the wrapper floor. The foundation was forced to zero for every river, exposing the background through the band between model and wall. The wrapper now stores the measured perimeter height and the network uses it without adding a duplicate foundation roof.
- Join classification accepted up to 0.06m position error and 0.03m height error, but previously left each surface at its own boundary. Reciprocal ends now meet at their common center, width and average height, with a smooth 0.65m transition. Existing shader edge attenuation also brings vertex waves to zero at this seam; flow lines remain animated. No overlapping water patch is added.

## Actual verification

Unity 6000.2.9f1 / URP 17.2, DX12, existing isolated validation project. Main authored scenes were not overwritten.

- All capsule banks in the four existing connection scenes are watertight after positional welding. Every welded edge has two incident faces with opposite directions; enclosed volumes are positive; no triangles are degenerate.
- Foundation heights meet the actual river model perimeter. River foundation meshes still contain no upward-facing roof.
- Exact grid placement, a 4cm horizontal offset with a 2cm vertical offset, a rotated network and independently adjusted water levels all share coincident boundary vertices. Maximum measured error in these cases is below 0.000004m.
- The land closure case, one-waterfall case and three-waterfall case retain their expected topology. Returning from a larger elevation difference restores the level join. Stationary networks do not rebuild repeatedly.
- Native Play Mode checks passed for rotation, displacement, elevation changes, replacing water with land and restoring the original join. No runtime errors were reported.
- Before / after side views and junction views were visually inspected. Images here are Unity renders; animations were frozen only for comparable still captures.

See AfterReport.txt and PlayReport.txt for assertions. Manual operation of the user's live Editor and target-device frame-time profiling were not performed. Generated geometry remains visual-only; gameplay collision/navigation generation is unchanged.
