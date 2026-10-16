# Forest / Rainforest 环境粒子

已分别接入：

| 场景 | 场景对象 | 效果 |
|---|---|---|
| `Forest_Moebius.unity` | **ForestVFX** | 浅黄绿花粉、零星落叶、少量手绘风线 |
| `RainForest.unity` | **RainforestVFX** | 偏冷的浅绿孢子、较少落叶、更缓的风线、低处薄雾 |

按当前场景中各三块启用的地面分别生成发射网格、地面碰撞和触发边界，不在地块之间的空隙发射。落叶来源按实际树冠网格筛选，发射点限制在相应地块内。没有改变原有地块、上色、相机、光照、NPR 或 Renderer Feature。

## 文件与复用

- 新增 `Assets/Rendering/Moebius/Runtime/Controllers/WoodlandWindController.cs`：两种环境共用的风控制器，支持可选第四层薄雾。
- 新增 `Assets/Rendering/Moebius/Content/VFX/Forest/`：三份材质、四份发射/碰撞网格、ForestVFX.prefab。
- 新增 `Assets/Rendering/Moebius/Content/VFX/Rainforest/`：四份材质、五份发射/碰撞网格、RainforestVFX.prefab。
- 修改上述两份场景：各增加一个对应的 VFX prefab 实例，所有原有非根列表 YAML 块保持一致。
- 新增 `Tools/BuildWoodlandVFX.ps1`、`Tools/MoebiusValidation/Editor/MoebiusWoodlandVFXSetup.cs`、`MoebiusWoodlandVFXPlayValidation.cs`：隔离构建、截图与实际 Play Mode 验证。主项目不增加 Editor 菜单。

复用现有 `MoebiusGrasslandMotes / Leaves / Wind` Shader，以及 Grassland 的 MotesAtlas、LeavesAtlas。薄雾使用现有 MoebiusDust Shader，但采用独立的低不透明度、冷色材质；复用 Highland 的 DustAtlas、DustNoise、WindAtlas 和水平粒子四边形。没有额外复制默认透明贴图或编写重复的 Shader。

现有 Grassland、高原控制器、粒子参数和材质均未由本次安装修改。

## 工作方式

原生 ParticleSystem 负责世界空间运动、Noise、旋转、碰撞和寿命淡入淡出。Position、Color、UV 顶点流将数据传给现有粒子 Shader，2×2 图集在出生时随机选一格，寿命中不跳帧。

控制器通过连续 Perlin 风力变化统一影响各层的水平速度，通过缓存的 MaterialPropertyBlock 传颜色、强度和边界；不执行逐粒子的 C# 更新。微粒不使用发光叠加，薄雾只以少量低矮柔边笔触暗示潮湿空气，没有增加全屏雾、RT 或渲染通道。

## 调整

选中 **ForestVFX** 或 **RainforestVFX**，编辑 **Woodland Wind Controller**。Inspector 统一为英文。

| 参数 | Forest | Rainforest |
|---|---:|---:|
| Base Wind Speed | 0.42 m/s | 0.22 m/s |
| Gust Strength | 0.6 | 0.35 |
| Motes Rate（所有地块合计） | 14/s | 8/s |
| Leaves Rate | 2.1/s | 0.9/s |
| Wind Wisps Rate | 0.5/s | 0.24/s |
| Mist Rate | 未绑定薄雾层 | 0.65/s |
| Random Seed | 9219 | 12311 |

Global VFX Intensity 和各层 Intensity 控制密度；小于 1 同时降低可见度，0 隐藏该层。颜色和尺寸可分别调整。雨林薄雾太淡时优先调独立 **RainforestLowMist** 材质的 Opacity，或调控制器的 Mist Intensity。Animate Edit Mode Preview 支持不进入 Play Mode 预览。

## 实际验证

使用 Unity 6000.2.9f1 / URP 17.2，两个场景均完成 Shader 编译检查、15 秒连续模拟、保存重载后的实际 Play Mode 和截图检查。

| 检查 | Forest | Rainforest |
|---|---:|---:|
| 15 秒峰值粒子数 | 134 / 上限 200 | 85 / 上限 210 |
| 穿地样本 | 0 | 0 |
| 越出地块样本 | 0 | 0 |
| 原有组件快照未变 | 1,236 个 | 967 个 |
| Play Mode 7.5 秒时各层粒子数 | 120 / 8 / 2 | 66 / 5 / 1 / 5 |
| Shader / 运行错误 | 0 | 0 |

两边 Global VFX Intensity = 0 的 PNG 均与修改前逐字节一致。新 prefab 和材质的外部 GUID 引用全部可解析。观察了原主相机、临时近景和全景，未见明显矩形边框或穿地。截图分别保存于 `Artifacts/MoebiusForestVFX` 和 `Artifacts/MoebiusRainforestVFX`。

## 限制

发射分布适配当前地形与树冠布局；单独移动、增删地形或树木后需重新生成。薄雾是少量网格粒子，非体积雾。原 RainForest 主相机构图和背景保留，Closeup / Overview 是未保存进场景的临时验证视角。未执行独立 Player Build 或目标设备 GPU 基准。

可重复生成入口：`Tools/BuildWoodlandVFX.ps1`；检查截图后用 `-ApplyExisting` 安装。本次使用 `-FixtureName MoebiusMergeValidation` 复用隔离导入缓存。安装前会检查两份源场景哈希，避免覆盖验证期间的新编辑。
