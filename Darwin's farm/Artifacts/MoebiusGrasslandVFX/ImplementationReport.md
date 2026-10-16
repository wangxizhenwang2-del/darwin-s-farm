> 2026-10-16：已进一步区分草原、森林和雨林的形状与运动。最新参数与效果见 [辨识度调整报告](<D:/GameAssignment/darwin-s-farm/Darwin's farm/Artifacts/MoebiusBiomeVFXIdentity/ImplementationReport.md>)。

# Grassland VFX：目标场景纠正

本套效果现在属于 `Assets/Rendering/Moebius/Samples/Biomes/Scenes/Grassland_Moebius.unity`，场景对象为 **GrasslandVFX**。已移除 Forest 场景中的误放实例；只移除该实例及其根引用，保留其他场景数据。

## 接入内容

- **FloatingMotes**：柔边花粉/孢子，缓慢受风漂浮，连续 Noise 和寿命淡入淡出。
- **FallingLeaves**：四种叶片图集，从 Grassland 的 Leaves 网格附近发射，缓慢下落、侧移和旋转。
- **GrasslandWindWisps**：少量手绘风线，共用风向，形状连续轻微变化。

发射网格、碰撞网格和边界已根据草原 **Land1** 重新生成；不是继续使用六块森林地形的数据。粒子采用世界坐标，控制器无逐粒子 CPU 更新。透明粒子不写入 DepthNormals，不改变原有描边。

## 文件

- `Assets/Rendering/Moebius/Runtime/Controllers/GrasslandWindController.cs`
- `Assets/Rendering/Moebius/Shaders/Materials/MoebiusGrasslandMotes.shader`
- `Assets/Rendering/Moebius/Shaders/Materials/MoebiusGrasslandLeaves.shader`
- `Assets/Rendering/Moebius/Shaders/Materials/MoebiusGrasslandWind.shader`
- `Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusGrasslandParticles.hlsl`
- `Assets/Rendering/Moebius/Content/VFX/Grassland/`：两张图集、三份材质、发射/碰撞网格、GrasslandVFX.prefab。
- `Tools/BuildGrasslandVFX.ps1`、`Tools/MoebiusValidation/Editor/MoebiusGrasslandVFXSetup.cs`、`MoebiusGrasslandVFXPlayValidation.cs`：离线生成和验证，不增加主项目 Editor 菜单。

以上由本次新增的 Forest 资源迁移并统一命名，保留 GUID，没有再复制一套。风线贴图与水平四边形继续引用已有 `Content/VFX/Highland` 中的 WindAtlas / WindAndDustQuad。

## 调整入口

选中 **GrasslandVFX → Grassland Wind Controller**。原有风向、风速、阵风、强度、颜色、尺寸、随机种子及 Edit Mode 预览控件保留。

迁移了你在误放实例上的调整：`Gust Strength = 1`、`Global VFX Intensity = 2.515`、三层 Intensity 均为 `3`。按草原面积重设基础发射率：微粒 1.7/s、落叶 0.28/s、风线 0.06/s；再乘以上述强度，风线还受阵风因子调制。

Unity ParticleSystem 将 Position、Color（含寿命 Alpha）、UV（随机选择 2×2 图集格子）传给 Shader；控制器通过缓存的 MaterialPropertyBlock 传 Tint 和边界。Shader 将贴图 Alpha、寿命 Alpha、强度及软交界淡化相乘。

## 验证

- Unity 6000.2.9f1 / URP 17.2，Shader 编译及实际 Play Mode 通过。
- 15 秒模拟峰值 **125** 个粒子，总上限 194；穿地 **0**、越出草原地块 **0**。
- 保存重载后的实际 Play Mode：微粒 105、落叶 13、风线 2，三个系统都在播放，无错误。
- 强度为 0 的截图与原草原场景截图逐字节一致；原草原 **709 个组件**的序列化快照未变。
- 检查主相机和临时近景截图，保留原相机设置。
- 文件哈希核对：除两处预期场景改动与本套 VFX 资源迁移外，其他原有资源保持原样；高原、原地块 prefab、上色贴图及 NPR 渲染系统未修改。

地块或树木单独移动后，发射/碰撞辅助网格需要重新生成。未执行独立 Player Build 或目标设备 GPU 性能基准。

画面：`PlayMode.png`、`Closeup.png`；详细测试数值：`Report.txt`、`RuntimeReport.txt`。
