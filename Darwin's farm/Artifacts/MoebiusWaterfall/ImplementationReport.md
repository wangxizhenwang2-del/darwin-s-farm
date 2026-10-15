# Moebius 瀑布实现报告

> 本文记录最初版本。底部冲击/涟漪与阴影接收已于 2026-10-11 更新，当前实现、参数和验证结果请参阅同目录 `ImpactImprovementReport.md`。旧版独立椭圆贴片已在样例中停用。

已在当前项目实现并交付瀑布水帘、四层网格水花、冲击泡沫和局部下游水纹。使用 Unity 6000.2.9f1 / URP 17.2.0，沿用当前 Moebius 的不透明插画色块、细线、共享 PrintNoise 和深度/法线描边。没有新增 Renderer Feature、运行时 RenderTexture、粒子系统或移动脚本；原有 NPR 和水体 Shader 未修改。

打开 `Assets/Rendering/Moebius/Samples/Waterfall/MoebiusWaterfallDemo.unity` 查看完整样例。需要放到自己的场景时，拖入同目录的 `MoebiusWaterfall.prefab`，或使用 **Tools > Moebius > Waterfall > Create Waterfall in Scene**。完整英文操作说明位于样例目录的 `README.md`。

## 新增代码文件及用途

以下路径均相对于项目根目录 `D:/GameAssignment/darwin-s-farm/Darwin's farm`。

| 文件 | 用途 |
|---|---|
| `Assets/Rendering/Moebius/Shaders/Materials/MoebiusWaterfall.shader` | 水帘材质：青绿色底色、宽色带、浅色流痕、细墨线、顶部亮色、轻微摆动和边缘裁切。 |
| `Assets/Rendering/Moebius/Shaders/Materials/MoebiusWaterfallSplash.shader` | 水花材质：云状网格的两块填色、轻微呼吸变形、轮廓墨线。 |
| `Assets/Rendering/Moebius/Shaders/Materials/MoebiusWaterfallImpact.shader` | 局部冲击与下游材质：不规则泡沫环、亮色接触中心、破碎扩散环和放射短线。 |
| `Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusWaterfallInput.hlsl` | 水帘的 UnityPerMaterial 参数，以及共享 Core / PrintNoise 引用。 |
| `Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusWaterfallSplashInput.hlsl` | 水花的 UnityPerMaterial 参数。 |
| `Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusWaterfallImpactInput.hlsl` | 冲击泡沫与下游水纹的 UnityPerMaterial 参数。 |
| `Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusWaterfallPasses.hlsl` | 共用顶点结构、位置变形、变形后的法线计算和深度输出。 |
| `Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterfallFlow.hlsl` | 连续向下流动的坐标、稳定边缘噪声和水帘顶点摆动。 |
| `Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterfallLines.hlsl` | 程序化纵向笔触、宽色带、细线抗锯齿和颜色合成。 |
| `Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterfallSplashMotion.hlsl` | 云状水花的呼吸、横向/向上变形和不同层的时间偏移。 |
| `Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterfallFoam.hlsl` | 不规则椭圆接触区、泡沫宽度、边缘墨线与向外移动的水纹。 |
| `Assets/Rendering/Moebius/Editor/Inspector/MoebiusWaterfallShaderGUI.cs` | 三种材质的英文分组 Inspector、使用提示、编辑模式预览开关和创建入口。 |
| `Assets/Rendering/Moebius/Editor/Waterfall/MoebiusWaterfallSetup.cs` | 创建网格、示例材质、可复用 Prefab 和安全样例场景；编辑器中生成一次，运行时直接使用保存的资产。 |
| `Tools/MoebiusValidation/Editor/MoebiusWaterfallValidation.cs` | 隔离项目中的编译、渲染、连续时间、各 Pass 轮廓、SRP Batcher 和保存重开检查。不会进入游戏构建。 |
| `Tools/ValidateMoebiusWaterfall.ps1` | 启动独立验证项目并保存截图和检查报告，不关闭用户的 Unity 编辑器。 |

上述新增 Unity 资产均附带 `.meta`。新增的 `Editor/Waterfall`、`Samples/Waterfall` 目录和样例子目录也附带目录元数据。

## 修改的现有文件

`Assets/Rendering/Moebius/Editor/Water/MoebiusWaterPreview.cs`：在原有水体检测中增加瀑布系列 Shader。它继续使用同一个编辑模式预览开关，只负责刷新已加载场景；Play Mode 动画仍由 Unity 时间驱动。没有改变水体 Shader 或现有 Renderer Feature。

## 样例资产

所有资产位于 `Assets/Rendering/Moebius/Samples/Waterfall/`，附带完整 `.meta`：

| 文件 | 用途 |
|---|---|
| `MoebiusWaterfall.prefab` | 七个 Renderer：一个水帘、四层水花、冲击泡沫、下游水纹。 |
| `MoebiusWaterfallDemo.unity` | 完整测试构图：上下水面、岩台、两块岩石、阳光和使用现有 Moebius Renderer 的相机。 |
| `Materials/WaterfallBody.mat` | 水帘默认参数。 |
| `Materials/SplashLayer0.mat` | 后层浅蓝水花，时间偏移 0 秒。 |
| `Materials/SplashLayer1.mat` | 浅青水花，时间偏移 1.7 秒。 |
| `Materials/SplashLayer2.mat` | 淡奶油水花，时间偏移 3.4 秒。 |
| `Materials/SplashLayer3.mat` | 前层近白水花，时间偏移 5.1 秒。 |
| `Materials/ImpactFoam.mat` | 下落接触区泡沫与水纹。 |
| `Materials/DownstreamRipples.mat` | 更窄泡沫带、更密水纹的下游区域。 |
| `Materials/LowerWater.mat` | 使用现有 Moebius Water Shader 的下层水面，波高 0.025 米。 |
| `Materials/UpperWater.mat` | 使用现有水体 Shader 的上层水面，波高 0.015 米。 |
| `Materials/PeachCliff.mat` | 使用现有 NPR Shader 的桃色岩台。 |
| `Materials/LavenderRocks.mat` | 使用现有 NPR Shader 的淡紫岩石。 |
| `Meshes/WaterfallSheet.asset` | 5.8 × 6 本地单位的细分水帘，顶部略弯曲；64 × 96 个网格单元。 |
| `Meshes/SplashCloud0.asset` | 第一层有体积的圆润云形网格。 |
| `Meshes/SplashCloud1.asset` | 第二层略不同的云形网格。 |
| `Meshes/SplashCloud2.asset` | 第三层略不同的云形网格。 |
| `Meshes/SplashCloud3.asset` | 第四层略不同的云形网格。 |
| `Meshes/ImpactPlane.asset` | 两个局部泡沫区域共用的平面网格。 |
| `Meshes/LowerWaterGrid.asset` | 下层水面细分网格。 |
| `Meshes/UpperWaterGrid.asset` | 上层水面细分网格。 |
| `README.md` | 英文设置、参数和限制说明。 |

## 水帘与细线

水帘使用青绿底色，加上较弱的蓝色色带。程序化笔触以横向坐标决定线条位置，纵向坐标决定弯曲与断续。纵向采样值是 `uv.y * height + time * FlowSpeed`，因此同一个笔触随时间出现在更低的位置，形成向下流动。

噪声使用当前项目的 `PrintNoise` / `PrintHash`，没有每帧重新播种。噪声坐标随连续时间缓慢移动；宽色带保持克制，细线宽度根据 `fwidth` 调整并在远距离淡出，避免过密线条产生摩尔纹。顶部浅色连接上层水面，底部浅色连接水花。水帘边缘仅裁切很小的一部分，顶点轻微摆动。

## 水花

水花由四个封闭、有浅厚度的网格组成。轮廓根据重叠圆形的外边界生成，形成圆润的起伏，而不是靠大量粒子堆叠。各层采用浅蓝、浅青、奶油白色，互相遮挡形成清楚的图形层次。Shader 轻微扩张/收缩并上下摆动，不随机闪烁。法线与缓慢的空间图案控制小幅两色分区；原有 Sobel 描边负责网格轮廓和层间遮挡边，材质可额外加少量轮廓墨线。

## 冲击泡沫与下游

两个水平网格使用同一个局部效果 Shader。UV 映射到中心为零的区域，椭圆距离加连续移动的空间噪声决定裁切边缘。距离边缘的一段区域成为浅色泡沫带，中心增加一个小的实色接触区；边缘细线使用距离值与 `fwidth` 生成。

水纹相位是 `radius * density - time * OutflowSpeed`，所以可见环线向外扩散。角度噪声打断整圆，短线沿放射方向增加流动感。下游网格沿河道方向拉长，使圆形坐标呈现拉长的扩散纹。效果只存在于这两个网格，不改变整个水体。

## 时间、Pass 与性能

全部运行时动画使用 `_Time.y + _AnimationPhase`，没有帧数取整，也不依赖固定频率的贴图更新。编辑模式只是让已有预览系统持续重绘。相同材质/相同 phase 会同步动画；需要不同节奏时复制材质并调整 phase。未添加 Runtime Controller，也没有每帧设置 MaterialPropertyBlock，因此不额外破坏 SRP Batcher。

三个新 Shader 都有可见颜色、DepthOnly、DepthNormalsOnly、ShadowCaster Pass。顶点变形函数和裁切函数共用，深度/法线不会使用未变形的旧轮廓。法线 Pass 支持原始法线与八面体打包编码。材质采用 `UnityPerMaterial`。样例的水效果默认关闭投射阴影，以保持图形清晰；需要时可开启 Renderer 的 Cast Shadows，ShadowCaster 已提供。水帘与水花填色采用插画逻辑，没有另外加入 PBR 光照或主光源接收阴影。

## 默认参数

水帘默认向下速度 1.4 本地单位/秒、线密度 9 条/本地单位、墨线宽 0.65 渲染像素、线条扭曲 0.15、边缘变化为宽度的 0.015、顶点摆动 0.035 本地单位。水花呼吸速度 1、幅度 0.045、摆动 0.045 本地单位；四层使用不同时间偏移。冲击泡沫宽度为半径的 0.18，边缘噪声强度 0.26、形状速度 0.7、水纹扩散速度 0.5。修改位置、缩放和材质即可适配不同瀑布。

## 验证与交付

`Report.txt` 记录 45 项通过检查，Shader / runtime / test errors 为 0。三个新 Shader 编译正常且 SRP Batcher 兼容。七个 Renderer 在 12 个连续的 1/60 秒采样间隔中，每个间隔都产生了像素变化；相同时间重复渲染则逐像素一致。七个 Renderer 的可见颜色、深度、原始法线、打包法线轮廓均为零像素差异。ShadowCaster 也执行过编译与绘制检查，保存的场景重开后所有网格与材质引用正常。

这些采样验证的是动画计算连续，不代表用户电脑实际运行一定达到 60 FPS。未做目标设备的性能基准或完整 Player 构建。

样例已从隔离项目复制到主项目，44 个生成文件及元数据逐文件核对哈希一致。主项目原有场景未改动。`Overview.png` / `Closeup.png` 是使用现有 Moebius Renderer 的实际 Unity 截图；`Delivery.txt` 是生成资产清单。

## 限制与可继续改进的方向

瀑布不自动寻找岩台、碰撞点或水位。需要把 Prefab 原点放在下层水面附近，并手动调整层的位置和尺寸。水帘预期使用本地 XY、UV V 向上；替换网格后应同步 Sheet Size。泡沫宽度是缩放后的网格半径比例，不是岸线烘焙的米制距离。

水平泡沫网格要放在下层水面的最高波峰之上，避免穿插；样例使用 0.12 / 0.15 米高度。低视角可能看出微小间隙，可通过降低水面波高或微调位置解决。自定义网格需要给 bounds 留足顶点动画余量。更精细的美术构图可以替换岩台和云形网格；本次提供的是可维护、可复用的完整样例，没有模拟真实喷雾、折射或流体碰撞。
