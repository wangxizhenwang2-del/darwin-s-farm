# Desert VFX

已在 `Assets/Rendering/Moebius/Samples/Biomes/Scenes/Desert_Moebius.unity` 增加 **DesertVFX**。

## 四层效果

| 对象 | 表现 |
|---|---|
| SurfaceSand | 距地面约 0.18 米的薄层流沙，沿风向移动 |
| GustSand | 随连续阵风增强/减弱的沙尘条带 |
| AirborneSand | 稀疏、细小的浮沙颗粒；使用微粒图集，没有使用叶片 |
| DesertWindStreaks | 少量浅色手绘风线，寿命首尾淡出 |

发射分布根据实际 `pPlane1` 地形高度生成，并避开较高物体附近的发射点。辅助地面 MeshCollider 回收接触地面的粒子；触发边界和 Shader 边缘淡化限制在沙漠范围。粒子自身仍正常执行深度测试。粒子中心受边界约束，接近地块边缘的悬空笔触在斜视角可能投影到地块轮廓外。

## 资源

- `Assets/Rendering/Moebius/Content/VFX/Desert/Materials/`：SurfaceSand、GustSand、AirborneSand、DesertWind 四份材质。
- `Assets/Rendering/Moebius/Content/VFX/Desert/Meshes/`：碰撞网格、贴地发射网格、风线发射网格。
- `Assets/Rendering/Moebius/Content/VFX/Desert/Prefabs/DesertVFX.prefab`：完整四层原生 ParticleSystem。
- `Tools/BuildDesertVFX.ps1` 与 `Tools/MoebiusValidation/Editor/MoebiusDesertVFXSetup.cs`、`MoebiusDesertVFXPlayValidation.cs`：隔离生成与验证，不增加主项目 Editor 菜单。

复用现有 **MoebiusWindController**、MoebiusDust / Wind / GrasslandMotes Shader，以及已有 DustAtlas、DustNoise、WindAtlas、MotesAtlas 和四边形网格。没有新增 Runtime 脚本、Shader、贴图、Renderer Feature 或默认透明资源。沙漠使用独立材质和暖沙色参数，不修改共享资源及其他场景。

## 调整入口

选中 **DesertVFX → Moebius Wind Controller**：

| 参数 | 默认值 / 控制内容 |
|---|---|
| Base Wind Speed | 0.95 m/s，共同基准风速 |
| Gust Strength / Frequency | 1 / 0.13，连续阵风幅度与变化速度 |
| Global VFX Intensity | 1，整体密度；低于 1 同时降低可见度 |
| Dust Intensity | 同时调整贴地流沙和阵风沙尘 |
| Debris Intensity / Color / Size | 本沙漠 Prefab 中映射为 **浮沙颗粒** |
| Wind Streak Intensity | 调整手绘风线 |
| Ambient Dust Rate / Gust Dust Rate | 6/s、3.5/s；后者再乘当前阵风系数 |
| Debris Rate | 5.5/s，浮沙颗粒 |
| Wind Streak Rate | 0.5/s |
| Dust Size Range | 1.3–2.3 m |
| Debris Size Range | 0.09–0.18 m |
| Wind Length Range | 2.6–4 m |
| Random Seed | 4507 |

Animate Edit Mode Preview 可在不进入 Play Mode 时预览。若只想让沙尘更明显，可先增加 Dust Intensity；也可单独调 SurfaceSand / GustSand 材质的 Opacity。配色由控制器的 Dust Color、Debris Color、Wind Line Color 管理。

## 验证

- Unity 6000.2.9f1 / URP 17.2，Shader 编译及保存重载后的实际 Play Mode 均通过。
- 15 秒连续模拟：峰值 **57** 个粒子，总上限 **200**；穿地样本 **0**，粒子中心越界样本 **0**。
- 7.5 秒实际 Play Mode：四层粒子数 **20 / 7 / 21 / 1**，均正常播放，无错误。
- 强度为 0 时，PNG 与修改前逐字节一致。
- 原有 **1,005 个组件**快照保持一致；最终场景 YAML 仅新增一个 VFX prefab 实例与根引用，原有对象、相机、光源和上色数据不变。排除了 URP 在截图时自动添加的默认光源组件。
- 查看了主相机、临时近景和正交截图。详见本目录 Report.txt、RuntimeReport.txt、PlayMode.png、Closeup.png。

未执行独立 Player Build 或目标设备 GPU 基准。改变地形后需重新生成发射/碰撞网格；没有模拟真实沙粒堆积或动态地形。重建入口为 `Tools/BuildDesertVFX.ps1`，本次用 `-FixtureName MoebiusMergeValidation` 复用隔离导入缓存；检查截图后通过 `-ApplyExisting` 安装，安装前检查源场景哈希。
