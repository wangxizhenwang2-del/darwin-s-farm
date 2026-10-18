# Moebius Biomes

## Volcano and Snow Mountain

Drag these main prefabs into a scene:

- `Prefabs/Volcano_Moebius.prefab`
- `Prefabs/SnowMountain_Moebius.prefab`

Open `Scenes/Volcano_Moebius.unity` or `Scenes/SnowMountain_Moebius.unity` for a centered preview with the existing Moebius renderer. The volcano camera faces the lava outlet. Preview positioning does not change the original prefab transforms or model scale.

| Folder | Contents |
| --- | --- |
| `Meshes/Volcano.fbx` | Original volcano model |
| `Meshes/SnowMountain.fbx` | Original snow mountain model |
| `Meshes/Volcano/` | Baked rock UVs, outline mesh and lava flow meshes |
| `Materials/Volcano/` | Rock, outline, lava flow and lava lake materials |
| `Materials/SnowMountain/` | Snow/rock surface and outline materials |
| `Textures/Volcano/` | Baked rock contact mask, flowing ink and lava lake field |
| `Textures/Volcano/Source/` | Original grayscale rock textures |

`Prefabs/SnowMountain_Template.prefab` is the separate output of the Snow Mountain setup tool. Use `SnowMountain_Moebius.prefab` for the authored biome; rebuilding the template does not replace that main prefab.

## Authoring tools

Snow Mountain tools remain under **Tools > Moebius > Snow Mountain**. Their source is grouped in `Assets/Rendering/Moebius/Editor/SnowMountain/`.

Volcano bake tools remain under **Tools > Moebius > Volcano**. They address the authored `Volcano (1)` object in `Assets/Scenes/New Scene.unity`, as before. Their output paths now point to the folders above. Moving assets does not require rebaking existing masks or UVs.

Dedicated shaders and HLSL stay in the framework's shared `Shaders/Materials/` and `Shaders/Includes/` folders. Their rendering behavior and material settings are unchanged.

All relocated assets retain their original GUIDs, so existing scene, prefab and material references continue to resolve.
