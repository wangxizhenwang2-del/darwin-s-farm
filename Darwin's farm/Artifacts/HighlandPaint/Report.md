# Highland import and paint validation

- Imported the supplied Highland.mb with Maya 2027 standalone and exported polygon geometry to Assets/Art/ImportedHighland/Highland.fbx. Embedded Maya script nodes were disabled. The supplied source file was never saved or overwritten.
- Imported all 38 renderers. The two source mountains have no UVs. Created 38 separate derivative mesh assets with fresh packed UV0 for painting; original imported mesh UVs remain unchanged.
- Assigned an independent Darwin/Moebius NPR material to each renderer. No runtime shader or drawing-tool behavior was changed for this import.
- Created Highland_Paintable.prefab, clean Highland_PaintTest.unity, and Highland_GradientExample.unity with a saved warm/cool ground gradient. The clean ground material is separate from the example ground material.
- Use the existing general-purpose Tools > Moebius > Paint tool on Ground - Paint Here in the test scene. The redundant Highland scene-opening shortcut was removed; no separate Highland paint tool exists. Usage instructions are in Assets/Rendering/Moebius/HighlandPaintTest/README.md.

Validation: 210 build/paint checks passed in an isolated Unity 6000.2.9f1 / URP 17.2 project, with zero shader/runtime/test errors. Checks include every derivative mesh and unique material, ray picking, GPU curved/terrain surface stamping, two-color blending, Undo/Redo, PNG save, Revert, paint-session reopen, scene reload, lighting changes and native paint-tool activation.

Delivered assets and .meta GUIDs were compared with the validated mirror (81 generated assets). A second Unity process successfully opened both delivered scenes, verified all 38 paintable renderers per scene, opened their albedo paint workspaces, and rendered the clean and saved-gradient versions with zero shader/runtime/test errors. The main project was already locked by its running editor; this cold-process test used the matching isolated mirror and did not interrupt the user's editor or existing scenes.

Detailed checks and screenshots: HighlandPaint/ and Installed/ beside this report. Source geometry/material metadata: SourceReport.json.

Painting remains per selected mesh/material slot. Soft brush colors blend within that target; a stroke does not automatically cross separate objects/material slots. Auto-generated UV charts improve independent painting but do not add painted-image seam dilation.
