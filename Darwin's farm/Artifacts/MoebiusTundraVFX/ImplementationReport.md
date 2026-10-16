# 苔原粒子效果

已集成到 `Assets/Rendering/Moebius/Samples/Biomes/Scenes/Tundra_Colored.unity`，场景根节点为 `TundraVFX`。独立特效 Prefab 位于 `Assets/Rendering/Moebius/Content/VFX/Tundra/Prefabs/TundraVFX.prefab`，可与苔原地块一起拖入其他场景。

## 四层表现

| Particle System | 效果 | 发射与运动 |
| --- | --- | --- |
| ColdGroundHaze | 淡蓝白色贴地冷雾 | 从地面上方发射，避开大岩石和池塘，缓慢沿风移动 |
| SnowPatchSpindrift | 被阵风卷起的雪粉 | 发射 Mesh 来自实际 11 片地面残雪与 4 个岩石雪帽，随阵风增强，离开雪片后逐渐淡出 |
| SnowFlakes | 稀疏飘雪/冰晶 | 在地块上方发射，斜向下落，轻微摆动旋转；四种固定图案包含不规则碎晶和细小交叉雪花 |
| ColdWindStreaks | 长而稀疏的浅色风线 | 横向掠过开阔区域，端部渐隐，保持现有手绘曲线风格 |

冷灰蓝与乳白色统一配色；保持大片地面可读。苔原的特征是冷风、零散雪粒与积雪附近的卷雪，不使用森林落叶或雨林滴水图案。

## 实现与复用

复用 `MoebiusWindController` 的连续 Perlin 阵风、强度控制、边界渐隐和 Edit Mode 预览。每个粒子的位移、碰撞、旋转和生命期交给原生 Particle System。没有新增运行时脚本、Shader、Renderer Feature 或全屏效果。

薄雾/雪粉使用现有 Moebius Dust Shader、DustAtlas 和 DustNoise；风线使用现有 Moebius Wind Shader、WindAtlas 和共享水平 Quad。雪粒复用支持深度柔化的 Moebius Grassland Leaves Shader，但使用新的独立 `SnowflakesAtlas.png` 和冷色描边材质。Shader 的历史名称不限制其用途。

新增资产集中在 `Content/VFX/Tundra`：4 个材质、5 个发射/碰撞 Mesh、1 张 512² 雪粒图集和 1 个特效 Prefab。发射区域与静态碰撞代理从当前模型离线生成；后续若改变地块几何或内部物件布局，需要重新构建这些区域。常规风向、速度、密度调整不需要重建。

## 参数

选中 `TundraVFX`，调整已有 **Moebius Wind Controller**。由于复用通用控制器，字段名与效果的对应关系如下：

| Inspector 字段 | 苔原含义 | 默认值 |
| --- | --- | --- |
| Global VFX Intensity | 总密度与可见度；0 完全隐藏 | 1 |
| Base Wind Speed | 共享基础风速 | 0.85 |
| Gust Strength / Frequency | 阵风增幅与变化频率 | 0.8 / 0.12 |
| Turbulence Strength | 连续小幅摆动 | 0.17 |
| Ambient Dust Rate | 冷雾每秒发射量 | 1.5 |
| Gust Dust Rate | 阵风雪粉最大发射量 | 2.4 |
| Debris Rate | 雪粒每秒发射量 | 8 |
| Wind Streak Rate | 风线每秒发射量 | 0.65 |
| Dust Size Range | 冷雾与卷雪尺寸（米） | 1.3–2.4 |
| Debris Size Range | 雪粒尺寸（米） | 0.14–0.26 |
| Wind Length Range | 风线长度（米） | 3.6–5.1 |

Dust Intensity 控制冷雾与卷雪，Debris Intensity 控制雪粒，Wind Streak Intensity 控制风线。打开 Animate Edit Mode Preview 可在非运行状态预览。进入 Play Mode 后使用原生游戏时间更新。

## 验证

Unity 6000.2.9f1 / URP 17.2 隔离项目内实际完成：

- 15 秒、900 步原生粒子模拟：峰值同时 59 个粒子，总上限 120；四层都成功发射。
- 检查到的穿地和越出地块样本均为 0。
- 最大逐帧风速变化约 0.0031，风向反转检查通过。
- 总强度 0 的截图与关闭粒子 Renderer 的对照完全一致。该对照临时冻结池塘程序动画，避免水纹自身随时间变化造成误判；没有把冻结状态保存到场景或材质。
- 重启 Unity、重新加载保存的场景，实际进入 Play Mode 7.5 秒：冷雾 6、卷雪 7、雪粒 44、风线 1，全部正常播放。
- 使用到的粒子 Shader 编译及运行检查错误数为 0。
- 原有 324 个场景组件快照保持一致；场景仅增加独立特效实例。地块 Prefab、已有配色和其他生态场景不修改。

截图与详细输出见同目录 `PlayMode.png`、`Closeup.png`、`Report.txt`、`RuntimeReport.txt`。没有进行独立 Player 构建或目标设备性能采样；以上数量来自实际编辑器模拟，不代表目标设备帧率。

离线构建/验证脚本位于 `Tools/BuildTundraVFX.ps1`、`Tools/MoebiusValidation/Editor/MoebiusTundraVFXSetup.cs` 与 `MoebiusTundraVFXPlayValidation.cs`，不增加 Unity Editor 菜单。
