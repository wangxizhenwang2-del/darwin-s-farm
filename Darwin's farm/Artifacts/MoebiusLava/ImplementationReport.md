# Moebius 岩浆 Shader 实现与验证

已交付可运行的 `Darwin/Moebius Lava` Shader、英文材质 Inspector、一个调好的通用岩浆材质，以及独立的岩浆池示例场景。新增整片岩浆接收投影功能后，独立 Unity 验证 44 项通过、0 错误。岩壳和开启发光的熔岩通道均可承接手绘排线阴影。

## 文件清单

以下路径相对于项目根目录 `D:/GameAssignment/darwin-s-farm/Darwin's farm/`。新增资源均附带稳定 `.meta` GUID。

| 新建文件 | 职责 |
|---|---|
| Assets/Rendering/Moebius/Shaders/Materials/MoebiusLava.shader | ShaderLab 参数、四个 URP Pass、Opaque 状态及材质面板关联。 |
| Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusLavaInput.hlsl | UnityPerMaterial 参数、可选 Control Map、顶点属性与不变形的顶点函数。 |
| Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusLavaPattern.hlsl | 岩壳胞元、低频边界扰动、漂移/形变、裂缝和描线距离场。 |
| Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusLavaFlow.hlsl | 适配现有水体平流函数，生成定向流动、次级流痕与热量变化。 |
| Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusLavaLighting.hlsl | 复用 NPR 光照/排线/点描，将冷却岩壳与三色热量色带、受限 HDR 发光合成。 |
| Assets/Rendering/Moebius/Editor/Inspector/MoebiusLavaShaderGUI.cs | 实际使用的英文材质面板与 Debug 枚举，不生成场景或执行运行时动画。 |
| Assets/Rendering/Moebius/Content/Lava/Materials/MoebiusLava.mat | 可直接使用或复制的默认岩浆材质。 |
| Assets/Rendering/Moebius/Samples/Lava/MoebiusLavaDemo.unity | 独立岩浆池示例，使用原来的 Moebius 相机 Renderer；不覆盖其他场景。 |
| Assets/Rendering/Moebius/Samples/Lava/Meshes/LavaPool.asset | 128 个三角形的静态、不规则水平方向池面。 |
| Assets/Rendering/Moebius/Samples/Lava/Materials/VolcanicRock.mat | 仅供新示例使用的 Moebius NPR 岸边石块材质。 |
| Assets/Rendering/Moebius/Samples/Lava/Materials/AshPaperGround.mat | 仅供新示例使用的 Moebius NPR 纸色地面材质。 |
| Assets/Rendering/Moebius/Samples/Lava/README.md | 英文使用说明、参数默认值和适用范围。 |
| Tools/MoebiusValidation/Editor/MoebiusLavaValidation.cs | Assets 外的可选示例生成器与 GPU 验证。 |
| Tools/ValidateMoebiusLava.ps1 | 隔离测试入口，不操作用户当前 Unity Editor。 |

修改的已有文件只有：

- `Assets/Rendering/Moebius/Editor/Water/MoebiusWaterPreview.cs`：让已有编辑模式预览服务也识别岩浆 Shader。原水体/瀑布判断和更新时间行为保持不变。
- `Tools/MoebiusValidation/README.md`：补充岩浆验证命令。

619 个原有 Moebius 文件的修改前/修改后哈希比较确认：除上述预览脚本外，原有 Shader/HLSL、材质、贴图、Mesh、Prefab、场景、Renderer Feature 均保持原文件内容。记录见 `ExistingFilesBefore.json` 与 `ExistingFilesChanged.json`。

## 架构和复用

采用项目现有的 `Shaders/Materials`、`Shaders/Includes/Inputs`、`Shaders/Includes/Features` 和 `Editor/Inspector` 组织方式。

复用的公共模块没有被改写：

- `MoebiusNoise.hlsl`：确定性的 PrintHash / PrintNoise，既不采样新噪声贴图，也不使用随机帧种子。
- `MoebiusWaterFlow.hlsl`：沿归一化 World XZ 方向进行平流和低频 UV 扰动。岩浆适配层将速度换算为世界米/秒。
- `MoebiusNPRLighting.hlsl`：直接调用 GetToonLighting，沿用主光源 NdotL、光照色带、阴影衰减和阴影 Mask。新模块以固定的克制色带常量适配公共函数，不暴露不适用的 Ramp Texture 或白色高光选项；这些不用的路径会被编译器消去。
- `MoebiusNPRHatching.hlsl`：沿用 SampleHatchLayers / GetHatchMask / GetCastShadowHatchMask 的笔压、不规则笔触、渐进交叉排线与抗锯齿。
- `MoebiusNPRDetail.hlsl`：沿用 StipplePattern 的确定性点描和远距离淡出。

没有新建 Outline Renderer Feature、Compute Shader、Fullscreen Pass 或岩浆专用 RenderTexture。项目已有的印刷色彩/纸感及深度法线描边继续由原相机 Renderer 处理。

## 岩壳 Mask 与裂缝

从片元世界坐标的 XZ 投影开始，将坐标按 Island Size 换算到胞元空间。先减去缓慢的岩壳漂移，再用两路低频 PrintNoise 扰动坐标。

在 3×3 邻域中查找抖动后的胞元特征点，取最近和次近距离 F1、F2：

```text
gap = F2 - F1
gap 较大：胞元内部，冷却岩壳
gap 较小：胞元相交边界，熔岩裂缝
crust = smoothstep(threshold - softness, threshold + softness, gap)
molten = 1 - crust
```

threshold 同时受 Crust Coverage、Extra Crack Width、Island Size 和每个岛块的固定变化影响。扩大裂缝会降低岩壳面积；提高 Coverage 会增加岩壳面积。Extra Crack Width 以世界米换算到胞元空间，但实际通道宽度也受胞元形状、覆盖率和扭曲影响，不是处处等宽。

轮廓墨线直接检测 gap 与 threshold 的接近程度，用 fwidth 换算为渲染像素宽度。它属于表面颜色，不会伪造法线，也不会影响真实几何的阴影。

## 漂移、形变与视觉分离

岩壳整体只以 0.035 m/s 漂移。每个胞元通过固定 ID 得到自己的相位和频率，并以连续正弦轻微改变特征点位置；低频域扰动也缓慢变化。因此不同边界会逐渐张开、收拢、弯曲，不是把一张贴图整体平移。

排线/点描坐标以最近岛块的位置为参考，笔触会随该岛块缓慢移动。相邻胞元的图案切换发生在熔岩通道中，完全熔化区域排线与点描权重为零。

这里没有把 Mesh 物理切成碎块，也没有让岩壳顶点抬高。视觉漂移和分离来自表面 Mask；碰撞形状仍是原 Mesh。

## 熔岩流动

下面的流层单独使用 0.28 m/s 的定向平流。利用既有 WaterFlowCoordinates 获得流动坐标，再将其投影到沿流向/横流向轴。低频热量块、次级噪声和稀疏弯曲流痕表达熔岩运动。

液体速度与岩壳速度分开，两层还有不同的空间频率与形变方式。将岩壳 Drift 和 Deformation 设为 0，可让岩壳停住而熔岩继续流；把 Molten Flow Speed 设为 0，可让熔岩流痕停住而岩壳继续变化。全部使用连续 `_Time.y`，没有 floor(time)、量化帧率或 C# 每帧改材质参数。

## 颜色、光照与发光

冷却岩壳用深灰紫与暗棕色的小幅变化。熔岩按通道中心热量与流层热量合成三段调色：冷却边界的深红、主体橙色、最热部分的金黄。Color Banding 让它保留大片稳定色面，少量 smoothstep 用于边界和抗锯齿；不使用 Ramp Texture。

岩壳调用既有 NPR 光照，表面光线夹角影响颜色与基础排线。投射阴影独立处理：复用 GetToonLighting 已采样的 castShadowMask，在岩壳、熔岩颜色和 HDR 发光合成后，统一叠加世界坐标锚定的排线阴影。阴影核心增加交叉笔触，底色只轻微压暗。关闭 Crust Hatching 不会关闭投影，阴影不会随岩壳漂移。熔岩的橙/金色主要由热量决定，不额外再乘一次 Lambert 将它压暗。

HDR 发光只在 Hot Region × Molten Mask 内相加。默认强度 0.22，不依赖 Bloom；关闭发光仍保留完整的橙/金调色。发光不会自动照亮附近物体，也没有新增 GI/烘焙 Meta Pass。Bloom 若将来使用，应作为已有后处理设置，而非 Shader 必需组件。

## 推荐参数

| 参数 | 默认值 | 常用调整范围 |
|---|---|---|
| Crust Coverage | 0.80 | 0.65–0.85 |
| Island Size | 1.8 m | 1.5–2.8 m |
| Boundary Distortion | 0.65 | 0.35–0.8 |
| Extra Crack Width | 0.12 m | 0.06–0.18 m |
| Boundary Softness | 0.025 m | 0.015–0.04 m |
| Molten Flow Speed | 0.28 m/s | 0.15–0.4 m/s |
| Flow Direction | XZ `(0.25, -1)` | 归一化后的世界方向 |
| Flow Distortion / Detail Scale | 0.32 / 0.65 | 0.2–0.45 / 0.45–0.9 |
| Crust Drift / Deformation | 0.035 m/s / 0.45 | 0.02–0.06 / 0.25–0.55 |
| Heat Contrast / Color Banding | 1.35 / 0.8 | 1–1.6 / 0.65–0.9 |
| Hot Region Threshold | 0.72 | 0.65–0.82 |
| Emission Intensity | 0.22 | 0.1–0.35 |
| Hatch Strength / Density | 0.42 / 10 lines/m | 0.25–0.5 / 6–12 |
| Hatch Pen Width | 0.7 pixels | 0.55–0.9 |
| Stipple Strength | 0.1 | 0–0.15 |
| Contour Width / Strength | 0.85 pixels / 0.7 | 0.6–1.1 / 0.45–0.8 |
| Crust NPR Lighting Influence | 0.65 | 0.4–0.8 |
| Receive Main Light Shadows | On | 独立开关 |
| Shadow Ink Strength | 0.75 | 0.5–0.85 |
| Shadow Hatch Density | 8 lines/m | 6–12 |
| Shadow Ink Color | RGB `(0.10, 0.075, 0.12)` | 暗灰紫色 |

## 新 Mesh 的使用方式

复制 `Content/Lava/Materials/MoebiusLava.mat`，赋给普通水平或缓坡 MeshRenderer；也可新建材质选择 `Darwin/Moebius Lava`。默认不要求 UV、不要求额外细分网格，不需要新增脚本。相机选择现有 Moebius Renderer 即可获得 Mesh 描边。

使用世界 XZ 投影，所以尺寸和流向不会随 UV 接缝或物体缩放被拉乱。旋转/移动 Mesh 时投影仍锚定世界，适合固定岩浆池、河道和缓坡。陡直墙面会拉伸投影；移动整个岩浆物体并希望图案跟随物体的用法暂不适用。未为此增加第二种坐标模式。

可选 Control Map 沿用 R 排线、G 细节、B 高光、A 阴影约定；B 控制流动高亮和发光，热量调色仍由程序决定。可选顶点色使用 R 排线、G 高光、B 阴影。Control Map 单独使用原始 UV0，默认关闭；开启时才需要适合该贴图的 UV。现有 Paint 工具针对 NPR Painted Albedo，本次没有扩展为岩浆绘画工具。

## 验证结果

独立 Unity 6000.2.9f1 / URP 17.2 项目实际编译、渲染，并人工查看了全景、近景与顶部输出。第一轮发现受光岩壳缺少笔触；第二轮保留轻微受光笔触、加大岛块形状变化，并正确以 sRGB 输出目标保存 Game 风格截图。

最终 44 项检查通过、0 个测试/Shader 错误，原始结果见 `Report.txt`：

- 默认 GPU 测试区域中岩壳约占 68.4%，同时存在大块冷却区域与熔岩通道。
- 固定时间重复绘制像素完全一致；岩壳在八秒内明显移动和变形。
- 12 个连续 1/60 秒采样中岩壳每次都变化，变化像素最小 14087、平均 14118.1；Composite 采样也没有停帧。这个结果说明着色动画连续，不等于宣称目标设备已达到固定 60 FPS。
- 两层可以独立停止、独立运动；零流向有稳定的 -Z 回退。
- 替换 Mesh UV0 为全零时默认 Composite 像素完全一致。
- Coverage 和 Crack Width 的面积变化符合预期。
- Emission Strength=0 与关闭发光逐像素一致；发光不改变完全冷却的岩壳。
- 排线与点描都可独立关闭；完全熔化区域不存在排线。
- Forward、DepthOnly、原始与八面体法线 Pass 轮廓一致；动画不改变真实法线缓冲。
- 现有 Outline Renderer 正常；开启发光、关闭基础岩壳排线时，投影仍改变 8340 个完全冷却岩壳像素和 2666 个完全熔化通道像素，并随投影物移动。
- 阴影强度为 0 与关闭阴影逐像素一致；关闭接收后，投影物开/关不改变岩浆区域像素。阴影密度可以独立调整，岩浆 Mesh 仍可向地面投影。
- 新岩浆和原 NPR/Water/Waterfall/Splash Shader 均无编译错误；新 Shader 的 SRP Batcher 兼容检查通过。
- 无运行时控制器；新场景保存后可重开；原瀑布示例也可正常打开。
- 已将经过验证的新资源及其 GUID 复制回主项目，并校验文件哈希。

相关截图：`Overview.png`、`Closeup.png`、`TopView.png`、`CameraLeft.png`、`DimLight.png`、`EmissionOff.png`、`CrustMask.png` 和 `CompositeLater.png`。

阴影更新修改了 MoebiusLava.shader、MoebiusLavaInput.hlsl、MoebiusLavaLighting.hlsl、MoebiusLavaShaderGUI.cs、MoebiusLava.mat 和验证代码，主场景没有加入测试物体。材质 Inspector 的 Receive Shadows 栏包含开关、墨色、强度和密度。内部移除了原先仅限岩壳的重复投影叠加，保留方向光照，并使用一次已有阴影采样对最终颜色统一画线。示例见 `ShadowExample.png`；备份见 `BeforeReceiveShadows/`。接收的是 URP 主方向光阴影，与项目现有水体/瀑布的范围一致。

日志另有原项目 `Moebius NPR` 的 Debug Enum 参数过多导致的 Inspector Drawer 警告，见 `Warnings.txt`。它不是岩浆 Shader 的编译错误，本次保持旧 Shader 不变；新岩浆 Debug 控件使用枚举类型避免该问题。

性能上每个片元只用一个 3×3 胞元搜索，复用共享噪声、掩码和笔触函数；不新增噪声/流动贴图。Control Map 默认关闭。岩浆由一个普通 MeshRenderer 绘制，深度/法线/阴影由既有 URP 管线按需执行。尚未对目标硬件的大面积填屏、移动端或整场景 Draw Call/GPU 耗时做基准测试，没有作未经测量的 FPS 承诺。
