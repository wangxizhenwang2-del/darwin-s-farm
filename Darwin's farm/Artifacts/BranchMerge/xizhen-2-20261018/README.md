# xizhen-2 into Vincent-Branch

Destination before merge: `0341ab446a8a36c96bf206c2a860f02ef5f81fb4`.

Source: `origin/xizhen-2` at `a256a8d27bfef4898450177b5d60f532d7d0d3e3`.

## Resolution policy

Keep Vincent's design/rendering behavior where branches overlap, while accepting xizhen-2 gameplay, placement, ecology and UI integration.

The textual conflict was in MoebiusRiverNetwork.cs. RiverConnectionGeometry.cs also changed on both branches and received an explicit review. Vincent's closed capsule mesh, shared water seam alignment, displacement attenuation, measured L_New foundation height and waterfall silhouette are retained. Gameplay query APIs, per-tile incremental regeneration and full-tile water matching are integrated so the imported map controller can use these visual systems. Existing-profile bank geometry is unchanged.

Existing skybox shader, cloud HLSL, ClearSky material and L_New wrapper prefab match the pre-merge Vincent versions. The four incoming changes to existing Moebius shaders only replace oversized Inspector Enum declarations with named enums; their rendering source is identical after stripping those attributes. New animal sprite shaders are retained for the new movement integration.

The source branch's historical reports, model outputs and isolated test-project snapshot are included as part of its normal Git history. The snapshot under output/ is outside the active Unity project's Assets directory.

## Validation

The merged active project's complete Assets content and ProjectSettings were copied into the guarded existing Temp/MoebiusMergeValidation fixture. Validation used Unity 6000.2.9f1, URP 17.2, DX12 and locally cached dependencies; the main project's package manifest and authored scenes were not rewritten by tests.

- Full runtime and Editor C# compilation passed.
- V6.2 model mobility: 9/9 passed.
- Actual WhiteBox palette: water rendering, natural elevations, connections and untouched land passed.
- Water population integration: 16 groups passed.
- Shared water roaming and speed controls: 7 groups passed.
- Drag preview and first-frame placement: all four real water palette entries passed.
- Vincent river regression: all four connection scenes passed. Capsule meshes remain watertight with consistent winding and positive volume; foundation walls meet the authored perimeter; no duplicate foundation roof. Exact/tolerant/rotated joins share their boundary, and larger height changes still produce waterfalls.

See AcceptanceReport.txt, RiverRegression.txt and the detailed gameplay reports alongside this file. WhiteBox.png is an actual native render of the acceptance fixture. These checks do not constitute a full manual gameplay playthrough, mobile validation or a performance benchmark.

Offline acceptance wrapper: Tools/MoebiusValidation/Editor/MoebiusBranchMergeValidation.cs. It adds no Editor menu and only runs in the marked isolated fixture.
