Source: huisan @ feaf8c7e114a1ffecb66e7905b53cef877eb3ac8
Target: Vincent-Branch

Only L_New and Cross_River and their required dependencies were transferred. No branch-wide merge was performed. Shared Vincent shaders, existing biomes, and the canonical TransparentPaint texture are reused. Two dedicated tree material copies preserve the source palette without changing shared Grassland materials.

Open Assets/Rendering/Moebius/Samples/Biomes/Scenes/River_L_Cross_Moebius.unity. Both 17-metre tile prefabs contain a Flowing Water child with the existing Moebius Water shader, separate channel-shaped mesh, local shoreline distance mask, animated foam and shadow reception.

Unity 6000.2.9f1 native validation: all meshes / materials / scripts resolve, shader compilation succeeds, rendered time-phase changes and visible water coverage pass, scene save / reload retains both water surfaces. See Report.txt and AssetAudit.txt. Images are rendered in the isolated Temp project; no play-mode profiling was performed.

The authoring helper is outside Assets: Tools/MoebiusValidation/Editor/MoebiusHuisanRiverSetup.cs. It runs only in the marked isolated fixture and adds no project menu entries.
