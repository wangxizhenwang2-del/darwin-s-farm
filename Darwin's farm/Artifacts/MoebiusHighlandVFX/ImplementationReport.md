# Highland_Animals Moebius VFX

已将目标纠正为 `Assets/Rendering/Moebius/Samples/Biomes/Scenes/Highland_Animals.unity`。Grassland 场景中的粒子实例已移除，保留了用户后续调整的相机位置和其他场景数据。

## 使用

打开 Highland_Animals，选择 **HighlandVFX**，在 **Moebius Wind Controller** 上调整风向、速度、阵风、密度、颜色、粒子尺寸。默认进入 Play Mode 自动播放；**Animate Edit Mode Preview** 控制编辑模式的自动预览。

```
HighlandVFX
├── GroundDust
│   ├── AmbientDust
│   └── GustDust
├── FloatingDebris
├── WindStreaks
└── Surface And Bounds
```

## 这次适配的内容

- 按高原实际约 **42×42 米**的地形重新生成发射面和碰撞面。不是将草地发射器直接搬过去。
- 地表取自 **Ground - Paint Here** 的实际三角形高度；排除陡坡、岩石和植被主要占位。
- 5342 个地面发射小区块、541 个风线发射小区块。风线筛选考虑主相机可见性，并在默认风向下避开五只动物附近的路径。
- 使用偏沙土色的扁平尘土、干草褐色的植物碎屑、低饱和灰绿色的弯曲风线；三者共用连续变化的风。
- 所有旧的动物、岩石、地面材质、绘画贴图、NPR Shader 和描边 Renderer Feature 保持不变。
- 资源和离线工具统一改为 Highland 命名，没有另外保留一套 Grassland VFX 资源。

## 文件

场景：`Assets/Rendering/Moebius/Samples/Biomes/Scenes/Highland_Animals.unity`。

资源目录：`Assets/Rendering/Moebius/Content/VFX/Highland/`：

- `Prefabs/HighlandWindVFX.prefab`：四个原生粒子系统和共享控制器。
- `Materials/GroundDust.mat`、`PlantDebris.mat`、`WindStrokes.mat`。
- `Textures/DustAtlas.png`、`DustNoise.png`、`DebrisAtlas.png`、`WindAtlas.png`：实际 PNG 纹理，所有材质引用已指定。
- `Meshes/HighlandCollision.asset`、`HighlandEmission.asset`、`HighlandWindEmission.asset`：针对当前高原重建的几何数据。
- `Meshes/WindAndDustQuad.asset`：共用水平面片。

继续复用之前实现的 `MoebiusDust.shader`、`MoebiusDebris.shader`、`MoebiusWind.shader`、共享粒子 HLSL，以及 `Runtime/Controllers/MoebiusWindController.cs`。本次更正没有重写它们。

离线生成和验证：`Tools/BuildHighlandVFX.ps1`、`Tools/MoebiusValidation/Editor/MoebiusHighlandVFXSetup.cs`、`MoebiusHighlandVFXPlayValidation.cs`。它们在 Temp 隔离项目中工作，不会作为正常游戏的 Editor 工具编译。

## 原理与默认值

尘土是贴近真实地表的水平 Mesh 粒子，使用不规则图集和连续噪声扭曲；碰到地表时消散，深度交界可平滑淡化。碎屑使用四种植物轮廓的 Billboard 图集并缓慢旋转。风线使用细曲线图集，在水平面片上随风移动，带寿命、首尾和地块边缘淡出。均采用普通 Alpha Blend，没有发光、额外全屏 Pass 或运行时 RenderTexture。

Position、Color、UV 由 Unity ParticleSystemRenderer 传给 Shader；图集 UV 由 Texture Sheet Animation 选择，寿命淡出写入顶点色 Alpha。控制器通过缓存的 MaterialPropertyBlock 传配色和地块范围；运行时不逐粒子遍历或 Raycast。

当前场景默认：Wind Direction `(1,0.3)`、Base Wind Speed `1.3 m/s`、Gust Strength `0.85`、Gust Frequency `0.11`、Turbulence `0.12`、Global VFX Intensity `1`、Random Seed `2718`。

Ambient Dust Rate `5.2/s`，Gust Dust Rate 最大 `3/s`，Debris Rate `1.4/s`，Wind Streak Rate `0.5/s`。尘土长度 `1.5–2.9 m`，碎屑尺寸 `0.35–0.65 m`，风线长度 `4.5–7 m`，地块边缘淡出宽度 `1.5 m`。总粒子配置上限 `196`。

## 实际验证

- Unity 6000.2.9f1、URP 17.2 编译和实际截图通过。
- 五只动物原始贴图均加载成功；材质和物体未修改。
- 对比原有 **157 个组件**的序列化快照，无改变。
- 12 秒、60 步/秒的模拟中最多同时存在 **38** 个粒子；尘土低于地表超过 4 cm 的采样数为 **0**。
- 最大每步风速变化约 **0.0044 m/s**，风向反转和强度归零检查通过。
- `Before.png` 与 `IntensityZero.png` 文件哈希相同，归零强度恢复原画面。
- 保存后重载场景并进入真实 Play Mode，四个系统自动播放；7.5 秒时粒子数分别为 **17 / 10 / 7 / 2**。
- 已检查透视及正交视图截图；Shader 和运行错误为 **0**。

原始结果：`Report.txt`、`RuntimeReport.txt`；画面：`PlayMode.png`、`Orthographic.png` 和 4/8/12 秒截图。

发射与避让数据针对当前高原几何及默认构图生成；改变地形或独立移动主要环境后，应重新生成。没有进行目标硬件 GPU 性能基准。编辑器自动预览代码已编译，真实 Play Mode 已测试；未录制交互式编辑器操作。
