# Grassland / Forest / Rainforest 粒子辨识度调整

本次调整的是已有三个 VFX Prefab。主要区别放在**形状、发射位置、运动方向和节奏**上，同时大幅减少三者共有的小亮点。

| 场景 | 主要形状 | 运动与空间分布 | 次要层 |
|---|---|---|---|
| Grassland | 细长带芒草籽、浅色弯曲风痕 | 从 39 处草丛附近生成，较快横向掠过草面 | 极少量小花粉 |
| Forest | 较大的阔叶、裂叶轮廓，橄榄/赭黄色变化 | 从树冠下方缓慢旋转下落，水平风较弱 | 少量细小尘点；风线关闭 |
| Rainforest | 细长树冠滴水、低处柔雾 | 滴水近乎竖直快速下落，湿雾缓慢平移；雾使用低矮的朝相机粒子片 | 极少量冷绿孢子；风线关闭 |

原有 Moebius Shader、HLSL、相机、上色、地形和植物保持原样；沙漠与高原粒子未修改。

## 新增形状资源

- `Content/VFX/Grassland/Textures/GrassSeedsAtlas.png`：四种带芒草籽。
- `Content/VFX/Forest/Textures/BroadLeavesAtlas.png`：宽叶、长叶及有裂口的叶片。
- `Content/VFX/Rainforest/Textures/CanopyDripsAtlas.png`：细长水滴。
- `Content/VFX/Rainforest/Textures/HumidMistAtlas.png`：柔边、低频团状雾片。
- `Content/VFX/Grassland/Meshes/GrassSeedEmission.asset`：草丛附近的发射分布，取代原树冠落叶发射位置。

路径前缀均为 `Assets/Rendering/Moebius/`。四份新图集为 512×512、2×2 格，透明背景，带 mipmap 和高质量压缩。已有的共享 MotesAtlas / LeavesAtlas / DustAtlas 保持不变，避免影响其他环境。

更新了三个 Prefab 与各自叶片/风线/雾材质；Grassland、Forest 场景直接继承更新后的同 GUID Prefab。RainForest 场景中的相应实例数据已同步。GrasslandWindController.cs 只改 Inspector 标签，将原 Leaves 控件显示为 Grass Seeds；WoodlandWindController.cs 只补充槽位用途提示。没有新增运行时更新逻辑。

## 默认参数与调整入口

三个根对象名仍为 **GrasslandVFX / ForestVFX / RainforestVFX**。本次将整体及启用层的强度统一为 1，再分别配置密度；原先叠乘到很高的全局强度不再沿用。

| 参数 | Grassland | Forest | Rainforest |
|---|---:|---:|---:|
| Base Wind Speed | 1.05 | 0.24 | 0.13 |
| Motes Rate | 0.35/s | 1.4/s | 0.8/s |
| Grass Seeds / Leaves / Drips Rate | 4.1/s | 5.2/s | 5.5/s |
| Wind Wisps Rate | 1.6/s | 0 | 0 |
| Mist Rate | 无 | 无 | 1.05/s |
| Seeds / Leaves / Drips Size | 0.22–0.4 m | 0.4–0.68 m | 0.2–0.34 m |
| 主粒子垂直速度 | -0.015～+0.055 m/s | -0.64～-0.37 m/s | -1.8～-1.1 m/s |

所有发射率是该场景所有地块合计。Forest 的 Leaves 控件调整落叶；Rainforest 共用控制器中原 Leaves 槽位现绑定 **CanopyDrips**，因此 Leaves Rate / Color / Size 调整树冠滴水。雨林的 Mist Intensity 和 RainforestLowMist 材质 Opacity 调整湿雾。

草原风痕已加宽、提高对比度，并增加地形接触回收。雨林滴水关闭旋转与 Noise，保持水滴向下落；雾片为约 0.8–1.15 米高的低矮 Billboard，使用现有深度淡化，与树干和地面交界更柔和。均未增加 Renderer Feature 或屏幕空间效果。

## 验证

在 Unity 6000.2.9f1 / URP 17.2 中完成同机位前后对比、15 秒连续模拟、保存重载和实际 Play Mode 检查。

| 检查 | Grassland | Forest | Rainforest |
|---|---:|---:|---:|
| 峰值同时粒子数 | 27 | 35 | 30 |
| 穿地 / 粒子中心越界样本 | 0 / 0 | 0 / 0 | 0 / 0 |
| 未变的环境组件快照 | 709 | 1,236 | 967 |
| 实际 Play Mode 各层数量 | 3 / 15 / 5 | 12 / 17 / 0 | 6 / 9 / 0 / 11 |
| Shader / 运行错误 | 0 | 0 | 0 |

所有场景关闭 VFX 后，截图均与原环境逐字节一致；场景中的非 VFX YAML 对象块均保持一致。原有共享贴图、渲染系统和其他环境通过文件哈希核对保留。

各子目录 `Before.png` 是同机位旧版比较图（比较时整体强度统一为 1），`Frame599.png` 是新效果约第 10 秒，`MainCamera.png` / `PlayMode.png` 保留原主相机构图。截图不能完整体现风向和下落速度，建议在 Play Mode 或编辑预览中观察。

## 重复生成与限制

更新工具是 `Tools/BuildBiomeVFXIdentity.ps1`，对应离线代码为 `Tools/MoebiusValidation/Editor/MoebiusBiomeVFXIdentity.cs` 和 `MoebiusBiomeVFXIdentityPlay.cs`。先在隔离工程验证，再通过 `-ApplyExisting` 安装；本次使用 `-FixtureName MoebiusMergeValidation`。工具要求已有三套 VFX；若使用旧的基础搭建工具重建，请再运行本次辨识度调整工具以恢复新风格。

发射网格仍与现有地形/植物布局对应，移动地形或草丛后需重新生成。滴水落地后结束生命周期，没有添加水滴飞溅或积水模拟。未执行独立 Player Build 或目标设备 GPU 基准。
