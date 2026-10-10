# Moebius 水体实现与使用说明

这是独立的 **Darwin/Moebius Water** 不透明水体材质：蓝绿色大色块、缓慢移动的细墨线、奶白岸边泡沫和小幅顶点波浪。使用 Unity 6000.2.9f1、URP 17.2.0、当前 Forward Renderer 和线性色彩空间验证。

## 打开测试场景

打开 `Assets/Rendering/Moebius/Samples/Water/MoebiusWaterDemo.unity`，进入 Play Mode。相机已经选择现有 Moebius Renderer。场景有细分水面、弯曲沙岸、六块岩石；没有新增运行时动画脚本。

`Overview.png`、`Closeup.png` 是实际 Unity 渲染截图。`Time0.png` / `Time3.png` 展示不同动画相位；`Flow0.png` / `Flow4.png` 和 `Foam0.png` / `Foam5.png` 分别隔离检查水流和泡沫。截图名中的数字是材质动画相位偏移秒数，不是性能测量。

## 给自己的场景添加水

1. 打开 **Tools > Moebius > Water Setup**。
2. 设置 Width / Depth；默认 Subdivisions Per Axis = 144，然后点击 **Create Water Surface**。选择保存网格的位置。工具创建水平水面和独立材质，使用默认水体配色。
3. 调整水面的位置和范围。保持水面水平，建议 Y Scale = 1。
4. 选中水面，Water Setup 自动填入 **Water Renderer**。水面网格的形状就是可见水区域；开放的网格外边缘不再被当作岸线。
5. 使用 Mask Resolution = 512、Distance Range = 4，点击 **Update Shoreline**。工具自动扫描同一场景的可见不透明地形、岩石和 Unity Terrain，不需要 Shore Objects 列表。生成的 PNG 与独立材质自动保存在 `Assets/Rendering/Moebius/Content/Water`，并赋给水面。后续更新在生成材质未被其他已加载场景对象共享时复用该材质及 PNG，不反复创建新资产。
6. 水体材质 Inspector 中调整颜色、波浪、水纹和泡沫。Debug View 可检查 Flow、Wave Height、Shore Distance、Foam 和 Normals。
7. 相机继续使用当前 Moebius Renderer，获得现有轮廓墨线和纸感处理。

已有足够细分的水平 Mesh，也可以直接拖入 `Content/Defaults/Materials/MoebiusWater.mat`。默认材质没有特定场景的岸边遮罩，因此先显示水纹和波浪；泡沫在完成该场景的烘焙后出现。普通只有四个顶点的 Quad 无法表现细致波浪。

烘焙不需要 UV、碰撞体或运行时 Read/Write。它使用编辑器只读网格接口。当前通用 Moebius Paint 工具仍用于 NPR 物体的 Albedo；水体的 Shoreline Mask 是距离数据，不能拿普通颜色涂层直接代替。

## 文件与结构

新建的运行时着色文件：

| 文件（相对 `Assets/Rendering/Moebius/`） | 作用 |
|---|---|
| `Shaders/Materials/MoebiusWater.shader` | Properties、完全不透明状态、Forward / DepthOnly / DepthNormals / ShadowCaster 四个 Pass |
| `Shaders/Includes/Inputs/MoebiusWaterInput.hlsl` | 所有材质常量放在同一个 UnityPerMaterial CBUFFER；遮罩采样和世界映射 |
| `Shaders/Includes/Features/MoebiusWaterWaves.hlsl` | 三组正弦波、解析法线、岸边波幅衰减、所有 Pass 共用的顶点入口 |
| `Shaders/Includes/Features/MoebiusWaterFlow.hlsl` | 世界 XZ 坐标沿方向平移，加连续低频扰动 |
| `Shaders/Includes/Features/MoebiusWaterRipples.hlsl` | 稀疏不规则曲线、水纹自身慢速漂移、导数抗锯齿和远距离淡出 |
| `Shaders/Includes/Features/MoebiusWaterFoam.hlsl` | 距离场泡沫、不规则外沿、细墨线边界、近岸小泡沫、推进/消退 |
| `Shaders/Includes/Features/MoebiusNoise.hlsl` | 原有 PrintHash / PrintNoise 的公共位置，算法保持原样 |

新增编辑器文件：

| 文件 | 作用 |
|---|---|
| `Editor/Inspector/MoebiusWaterShaderGUI.cs` | 英语材质分组界面和 Water Setup 入口；不会进入游戏构建 |
| `Editor/Water/MoebiusWaterSetup.cs` | 创建可复用细分网格、从静态模型烘焙岸边距离场、配置独立水体材质；不会进入构建 |

**唯一修改的已有着色文件**是 `Shaders/Includes/Features/MoebiusPrint.hlsl`：把已有的两个噪声函数移入公共 include，再引用回来；函数代码和原有纸纹/描边计算保持原样。没有修改 NPR 光照、排线、角色 Shader、现有 Renderer Feature 或原场景。

新增资产：

- `Content/Defaults/Materials/MoebiusWater.mat`：无场景专属岸边数据的通用默认材质。
- `Samples/Water/MoebiusWaterDemo.unity`：海岸测试场景。
- `Samples/Water/MoebiusWaterSurface.prefab`：已配置演示岸边映射的水面；移到其他地方须重新烘焙。
- `Samples/Water/Meshes/WaterGrid.asset`、`Shore.asset`、`Rock.asset`。
- `Samples/Water/Materials/CoastalWater.mat`、`Sand.mat`、`PeachStone.mat`、`LavenderStone.mat`。
- `Samples/Water/Textures/ShoreDistance.png`。
- `Samples/Water/README.md`：英语操作说明。
- 每个 Unity 文件和新增目录都附有 `.meta`；GUID 随资产一起保留。

验证工具放在 Assets 外：`Tools/ValidateMoebiusWater.ps1`、`Tools/MoebiusValidation/Editor/MoebiusWaterValidation.cs`（及 `.meta`）、`Tools/MoebiusValidation/WaterValidationDepth.shader`。深度探针 Shader 只复制到隔离验证项目，用于读取 ShadowCaster 的真实深度；不会进入游戏。命令生成的测试图和报告保存在 `Artifacts/MoebiusWater`。

`Delivery.txt` 列出生成并交付的演示资产；`Files.txt` 列出本次源文件、资产、文档和 metadata。

## 水流怎么算

起点是 `positionWS.xz * FlowScale`，不使用相机屏幕坐标。启用水流时，沿归一化 FlowDirection 减去 `(_Time.y + AnimationPhase) * FlowSpeed * FlowStrength`。对平移后的坐标加入轻微连续 value noise 扰动。

这组坐标用于**真正可见的水纹**和可选的微弱颜色变化，所以水流并不是在平移纯色贴图。水纹以单元格为基础，每条笔触有不同的位置、长度、弯曲和出现概率，避免规则栅格。Ripple Speed 另外控制水纹自身的缓慢漂移；关闭 Flow 不会自动关闭这个独立控制。

水纹在 `floor/frac` 分格之前，先对连续坐标使用 `ddx/ddy` 计算像素跨度。随后结合曲线的解析斜率计算笔触覆盖，避免格子随机种子或绝对值距离的跳变进入抗锯齿宽度。细线采用像素覆盖积分，移动时覆盖率连续地转移到相邻像素；端点也使用连续坐标跨度。当前版本还检查四个相邻笔触候选，让线条跨格边缘时继续使用自己的空间种子，避免在边界突然消失或重选笔触。过于细密、不能被像素解析时渐隐。Noise 的随机种子来自空间单元，而不是帧号，因此没有每帧白噪声跳变。

水纹现在还支持独立的轻微手绘抖动：**Ripple Wobble Strength** 默认 0.035，**Ripple Wobble Speed** 默认 3。每条笔触使用自己的固定空间种子和连续时间，通过两组小幅正弦改变曲线弯曲，解析斜率也同步更新以保持抗锯齿。没有逐帧重新抽取随机数。Strength = 0 时跳过新增计算，恢复原曲线；Speed = 0 时冻结新增形状。该功能仅影响 Forward 的水纹墨线，不影响泡沫、顶点波浪、DepthOnly、DepthNormals、ShadowCaster 或 Renderer Feature。两项参数写入共享 UnityPerMaterial CBUFFER，并显示在材质 Inspector 的 Ripples 分组；默认材质和演示材质已开启。

实际相机渲染验证已通过：关闭 Flow、Ripple Drift、波浪、泡沫和颜色变化后，Strength = 0 的静止水纹在不同时间保持像素一致；Strength = 0.035 时水纹出现形状变化，并随时间独立变化。测试使用普通相机 Renderer 排除描边/纸纹干扰。`RippleWobbleStart.png` 与 `RippleWobbleHalfSecond.png` 是这项隔离检查的图像；主光照与其他材质未修改。当前编译及运行错误为 0，结果见 `AutomaticWaterReport.txt`。

默认启用 **Animate Edit Mode Preview**。新的 `Editor/Water/MoebiusWaterPreview.cs` 在有水面对象的已加载场景中自动请求持续重绘，不再依赖 Water Setup 窗口打开；刷新截止时间按节拍推进，避免稍早/稍晚的编辑器回调导致反复跳过刷新。扫描水面每秒一次，不每帧修改材质；它不会进入游戏构建，Play Mode 使用游戏本身的帧率。在 Water Setup 关闭预览可以暂停此刷新，设置在当前编辑器会话中保持。窗口的 **Game camera cadence** 显示实际相机渲染事件的平均频率及最长间隔，不是 GPU 耗时。加载或编译后可用 **Reset Frame Monitor** 清掉启动停顿。如果实际帧率低，连续的 Shader 时间也不能消除帧间跳动。

## 接收物体投影与本次卡顿排查

`Shaders/Includes/Features/MoebiusWaterShadows.hlsl` 使用波浪位移后的世界位置采样 URP 主光源 Shadow Map，支持主光源、级联与屏幕阴影变体以及软阴影变体。只将阴影可见度转换为静态世界 XZ 排线和很轻的底色压暗，没有额外 Lambert，也没有新增运行时 Render Pass 或 RenderTexture。新增 Inspector 的 Cast Shadows 分组：Receive Main Light Shadows、Shadow Ink Color、Shadow Ink Strength、Shadow Hatch Density。默认强度 0.65、密度 8 条/米。关闭接收或强度设为 0 时绕过新增效果；不采样额外点光源/聚光灯阴影。水体其他 Pass 的几何计算不变。

演示场景、Prefab 和新建水面 Renderer 已启用 Receive Shadows，默认及演示材质已开启主光源阴影。既有独立材质可以在 Cast Shadows 分组启用，投影物体与主光源也需要开启 Shadows。实时阴影和静态岸线遮罩是两份不同的数据：移动投影物体不必重新烘焙阴影；改变实际岸边位置仍需要更新岸线。

此次真实相机测试通过：阴影强度为 0 与关闭接收像素一致；开启后水面出现墨线阴影；移动投影物体及转动主光源均使水面阴影改变。图像为 `WaterShadowDisabled.png`、`WaterShadowInk.png`、`WaterShadowCasterMoved.png`、`WaterShadowLightTurned.png`。

为避免以往编辑模式 realtimeSinceStartup 混入相位测试，本次增加 `CaptureTimedWater`：实际绘制 WaterForward，显式设置 URP 矩阵及 `_Time`，并检查重复相同时间绘制像素完全一致。每隔 1/60 秒分别采样水流和泡沫 60 次，水流变化像素最少 2611、平均 2696.0；泡沫最少 283、平均 301.8；两组零变化间隔均为 0。数据在 `ContinuousWaterSamples.csv`。这说明测试参数下没有固定时间停帧，不代表用户场景的实际帧率。最终隔离持续相机渲染平均 59.0 次/秒，最长间隔 40.5 ms；编译及运行错误为 0。当前用户现场卡顿的最终原因仍需结合发生在 Edit Mode 还是 Play Mode 及实际帧率判断，不能把隔离测试当作用户现场性能证明。

## 波浪怎么算

三组波浪使用不同方向、波长倍率和速度倍率：

`height = Σ amplitude × sin(dot(worldXZ, direction) × 2π / length − time × speed + phase)`

三组振幅权重分别为 0.60 / 0.27 / 0.13。默认总振幅上限约 5.5 cm。使用 worldXZ 和世界 Y 位移，相邻水面只要共享相同参数、时间和岸边映射，波的数学相位就连续。

求导得到 dHeight/dX 和 dHeight/dZ，构造真实表面法线。岸边距离接近零时用 smoothstep 压低波幅；距离场 GB 保存的距离梯度也参与衰减项的法线计算。没有把彩色水纹伪装成几何法线。

Forward、DepthOnly、DepthNormals 和 ShadowCaster 共用这一套顶点函数。ShadowCaster 随后使用 URP 的 Shadow Bias / Shadow Clamping。DepthNormals 支持普通世界法线和 URP 八面体打包路径。演示水面默认不投射阴影，但这个 Pass 完整保留并单独测试。

演示网格 160 × 160 格，共 **25,921 个顶点、51,200 个三角形**。网格资产自身扩展了 Y 包围盒，在默认 Y Scale = 1 下覆盖材质面板支持的波幅，保存重载后也有效。

## 泡沫为什么用距离场

当前 MoebiusOutlineRendererFeature 在 BeforeRenderingPostProcessing 执行，并通过 ConfigureInput 请求 Depth 和 Normal。URP 因此可能执行深度/法线预通道，不透明水面自己的 DepthNormalsOnly / DepthOnly 也会参与。当前 Renderer 的 CopyDepthMode 也不能保证提供不含水面的岸边深度。

所以没有用 `_CameraDepthTexture − waterDepth` 这种不可靠办法。当前泡沫**完全不采样相机深度**，也没有额外运行时 Renderer Feature、RenderTexture 或全屏 Pass。

烘焙器只把实际地形和岩石视作陆地，开放的水面网格边缘和相邻水面接缝不会自动生成岸线。水位从平面网格的变换后顶点读取，不依赖对象原点；因此导入平面的局部 Y 偏移也能正确识别。它自动收集同一场景的可见不透明 Mesh，将高于水位的三角形投影为陆地，并按世界高度采样 Unity Terrain，尊重 Terrain 洞。低于水位的地面不算岸；水面必须放在水下地面上方。没有实际陆地时输出中性距离场，不产生泡沫。

用两遍精确欧氏距离变换生成水侧离岸距离，支持 X/Z 不同的世界像素尺寸。PNG 的 R 通道编码 0～DistanceRange 米，GB 编码距离梯度；以线性、Clamp、Bilinear、无压缩、无 Mip 的设置导入。映射范围在水面范围之外增加 Distance Range 对应的边缘，以便检测紧邻水面外部的实际岸边；扩展区域本身不会被标成陆地。

Shader 用距离减去带有慢速噪声的 FoamWidth，生成泡沫填色；同一个边界函数生成细墨线。近岸零星泡沫和小幅推进/消退独立可关。水纹画完后再叠泡沫，避免墨线穿过奶白区域。岸边波幅衰减仍然独立工作，即使关闭 Foam 也不会恢复岸边的大幅起伏。

这是**静态岸线**。移动岸边、水面、改变水位或改变范围后，仍需点击一次 **Update Shoreline**，但不需要额外指定岸边。透明物体、Moebius Billboard 角色、禁用对象、Animator 或 Rigidbody 下的物体自动排除，其他场景对象不会混入当前水体。Unity Terrain 已支持；SkinnedMesh 不是烘焙输入。普通不透明桥梁或浮空装饰若没有上述组件，仍可能被投影成陆地，自动扫描不能推断它们的用途。

## 默认与建议参数

方向的 Vector 字段使用前两个分量，依次表示世界 **X、Z**。方向接近零时安全回退为世界 +X。以下是已交付默认值，所有开关都有独立强度或振幅：

| 分组 | 参数 | 初始值 |
|---|---|---|
| Appearance | Water Color | RGB (0.49, 0.72, 0.74) |
| Appearance | Enable Color Variation | Off |
| Appearance | Color Variation / Variation Scale | 0.025 / 0.35 |
| Flow | Enable Flow / Flow Strength | On / 1 |
| Flow | Direction / Speed | (0.2, 1) / 0.5 |
| Flow | Scale / Distortion | 1 / 0.2 |
| Waves | Enable Waves / Height | On / 0.055 m |
| Waves | Length / Speed / Direction | 3.2 m / 1.4 / (0.4, 1) |
| Ripples | Enable Ripples / Strength | On / 0.65 |
| Ripples | Ink Color | RGB (0.25, 0.39, 0.40) |
| Ripples | Density / Length | 1.5 / 0.65（单元宽度比例） |
| Ripples | Width / Speed / Distortion | 0.018 / 0.12 / 0.45 |
| Foam | Enable Foam / Intensity | On / 1；需要岸边遮罩 |
| Foam | Foam Color | RGB (0.94, 0.95, 0.81) |
| Foam | Width / Animation Speed | 0.38 m / 0.28 |
| Foam | Noise Scale / Irregularity | 2 / 0.9 |
| Foam Ink | Enabled / Strength / Width | On / 0.65 / 0.75 render pixels |
| Foam Ink | Ink Color | RGB (0.23, 0.33, 0.31) |
| Foam Patches | Enabled / Strength | On / 0.8 |
| Shore Motion | Enabled / Strength | On / 0.13 |
| Shore Mapping | Use Baked Shoreline | 默认材质 Off，演示材质 On |
| Shore Mapping | Mask | 默认无场景遮罩，演示 ShoreDistance.png |
| Shore Mapping | World Rect | 默认/演示 (-8,-8,16,16) / (-13,-11,26,22)，烘焙时自动设置 |
| Shore Mapping | Distance Range / Wave Fade | 4 m / 1 m |
| Advanced | Animation Phase Offset | 0 秒 |
| Advanced | Debug View | Composite |

Animation Phase 用于让不同水体有不同的动画相位，也用于可重复的测试。实际动画仍然由 `_Time` 驱动，无须每帧修改材质。关闭全部附加效果后保留纯 Water Color；Enable Flow = Off 时 Ripple Drift 仍是单独参数。

根据“移动太慢”的反馈，已把演示材质、默认材质和 Shader 新材质默认速度同步提高：Flow 0.12 → 0.5，Ripple 0.025 → 0.12，Wave 0.65 → 1.4，Foam 0.09 → 0.28。波幅仍为 0.055 m。此次仅调整速度，没有改变水色、线宽、泡沫形状或着色流程。`MotionReport.txt` 记录了更新后的独立图像预览：0.5 秒动画相位间隔产生 12,569 个变化像素，原速度为 9,169；这是画面差异，不是帧率或移动速度倍率。更新后的水体编译错误为 0。

之后根据“一卡一卡”的反馈，修正了上述水纹抗锯齿，并将默认及演示线宽从 0.012 调整为 0.018。保留已有速度、波浪和泡沫计算。原水体就使用连续 `_Time`，并不存在按 8 帧/秒更新水体的逻辑；全屏描边的 WobbleFrameRate 不控制水流，当前 LineWobbleSpeed 也为 0。较早的 `SmoothMotionReport.txt` 是设置动画相位后同步渲染得到的图像差异；URP 在编辑模式还使用 realtimeSinceStartup，所以那组数据不能作为精确的固定时间采样，更不能证明用户场景实测 60 FPS。

当前版本增加相邻笔触覆盖和持续 Edit Mode 预览。`AutomaticWaterReport.txt` 与 `LiveWaterFrames.csv` 记录隔离项目预热、首张截图完成后的连续实际相机渲染，无相位覆盖；只在测量首尾读取截图。它们验证自动岸线、Terrain、相邻水面，以及连续渲染是否报错。测得频率仅属于隔离测试，用户当前场景的帧率可用窗口内的相机监测观察。

此前自动岸线版本的隔离测试预热后平均 **59.1 次相机渲染/秒**，最长间隔 **38.5 ms**，编译及运行错误 **0**；这是历史测试值。本次按截图要求调整为只在实际地形/岩石附近生成泡沫，三条开放水面边缘保持干净。当前结果见 `AutomaticWaterReport.txt`：包括开放边缘、真实沙岸与岩石、网格外实际岸边、无岸水域、Terrain 高度与洞的检查。演示 `CoastalWater.mat` 和 `ShoreDistance.png` 随验证通过的结果更新，并保留原 GUID；没有改动演示场景或其他物体材质。水流 Shader、速度及泡沫自身的动画算法没有改变。

## 验证与实际边界

实际验证清单与结果见 `Report.txt`。包含真实相机渲染、隔离水流/泡沫的图像差异、四个 Pass 的 GPU 位移输出、ShadowCaster 深度读取、DepthNormals 八面体变体、局部点光源阴影变体、关闭/零强度等价性、材质重载、网格包围盒和距离场精度。

验证在独立临时项目中进行，没有自动打开或切换你原项目当前的编辑场景。已交付场景和材质的 `.meta` 一同复制回原项目；原项目打开后由 Unity 导入。没有进行整款游戏的 Player Build、目标移动设备性能测量、VR 或其他图形 API 测试。

性能方面：岸边纹理启用时每个波浪顶点一次采样，Forward 需要泡沫时每像素一次采样；没有 Noise Texture、Compute 或水体专用全屏 Pass。Depth/Normals/Shadow 的波浪计算是必要的一致性成本。使用多个大面积 51,200 三角形水面或很高分辨率时，应降低细分、控制覆盖面积，并在目标设备测量。这里没有声称固定帧率或 GPU 毫秒数。

距离 PNG 采用 8-bit R，4 m 范围的距离量化约 1.57 cm；当前自动边缘扩展后，512²、26 × 22 m 演示映射范围的栅格约 5.08 × 4.30 cm。极窄泡沫、巨型水域或细小岸边需要更合适的范围/分辨率。范围应覆盖泡沫及零星泡沫的最大外延，默认值足够覆盖默认宽度。

烘焙是以上水位的投影覆盖为基础，岩石等悬挑形状的泡沫范围可能略宽于真正的水位截面；适用于当前静态沙岸和岩石测试，不是任意几何的体积布尔求交。不同水位的多层水面应使用独立材质和遮罩。波浪不提供碰撞或浮力数据。

这版针对湖面、河面和海岸这样的**水平水体**。第一张参考中的垂直瀑布流线和瀑布底部飞沫，需要针对瀑布网格另作映射和造型；没有把当前水平水面当作已实现瀑布。当前 Water Color 刻意保持插画纯色，不接收 PBR 光照、高光、反射或折射。

```text
World XZ + Time
    ├─ 三组波浪 → 岸边衰减 → 顶点位置与法线 → 四个一致的 Pass
    ├─ 流向平移 + 低频扰动 → 稀疏手绘水纹
    └─ 烘焙岸边距离 + 慢速形状变化 → 奶白泡沫 + 墨线边缘
Water Color → 可选微弱变化 → 水纹 → 泡沫 → 现有 Moebius 轮廓/纸感 → Final Output
```
