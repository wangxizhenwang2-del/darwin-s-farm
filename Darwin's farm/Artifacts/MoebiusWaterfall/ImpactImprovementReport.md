# 瀑布底部冲击、涟漪与阴影改进报告

> 后续已增强可见度。本文的参数与测量记录为最初改进版本，当前参数、最新截图和验证数据见同目录 `VisibilityImprovementReport.md`；样例 README 也已同步更新。

2026-10-11，Unity 6000.2.9f1 / URP 17.2.0。

已直接修改当前项目。保留水帘与四层云状 Splash Mesh 的形状、运动和原有配色；底部冲击改为现有 Moebius Water 的可选颜色模块。样例已更新，下层水体开启该模块，其他水体默认关闭。水帘和水花新增主光源阴影接收，以排线表达遮挡。原有 Renderer Feature、描边 Shader、Runtime 控制器和生产游戏场景未修改。

项目根目录：`D:/GameAssignment/darwin-s-farm/Darwin's farm`。

打开场景：[MoebiusWaterfallDemo.unity](<D:/GameAssignment/darwin-s-farm/Darwin's farm/Assets/Rendering/Moebius/Samples/Waterfall/MoebiusWaterfallDemo.unity>)。

## 原来为什么像装饰椭圆

原实现使用两个独立水平 Mesh：Impact Foam 和 Downstream Outflow。Shader 根据 UV 中心距离计算环线，网格缩放将其拉成椭圆。重复径向周期产生相似的环，外缘又有常驻浅黄色泡沫带。固定贴片外轮廓和多个完整环叠加后，视觉重点落在“椭圆边框”，而不是落水后向外传播的短暂水波。贴片还需要抬高来避开水面，低角度容易显示分层。

本次样例停用这两个子物体，保留资产供旧场景参考。新冲击使用原来的下层水面 Mesh；没有新增 Mesh、Decal、Renderer Feature、运行时 RenderTexture 或 C# 时间控制器。水面顶点仍按原来的 Waves 模块运动。

## 修改文件

以下文件路径相对于上面的项目根目录；完整绝对路径列表见 `FilesChangedImpact.txt`。

| 文件 | 修改内容 |
|---|---|
| `Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterfallFoam.hlsl` | 替换旧 UV 椭圆算法；实现线段距离、中心泡沫、连续噪声、独立波纹生命周期、断续弧线和衰减。提供共享颜色合成函数。 |
| `Assets/Rendering/Moebius/Shaders/Materials/MoebiusWater.shader` | 增加默认关闭的 Waterfall Impact 参数；在原泡沫合成之后、原阴影合成之前应用冲击颜色。新增泡沫/涟漪 Debug View；几何和深度 Pass 保持原样。 |
| `Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusWaterInput.hlsl` | 在 UnityPerMaterial 内引用共享冲击参数。 |
| `Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusWaterfallImpactParameters.hlsl`（新增） | 接收水面与旧 Impact 材质共用的常量缓冲参数声明。 |
| `Assets/Rendering/Moebius/Shaders/Materials/MoebiusWaterfallImpact.shader` | 旧独立 Mesh 入口复用新冲击函数，保留 Shader 名称和旧属性；不再用于当前样例。 |
| `Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusWaterfallImpactInput.hlsl` | 旧 Mesh 的裁切改为共享世界空间冲击 Mask，并接入共享参数。 |
| `Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterfallShadows.hlsl`（新增） | 复用现有 WaterShadowStripe，采样主光源 shadow attenuation；适配竖直水帘的排线坐标。 |
| `Assets/Rendering/Moebius/Shaders/Materials/MoebiusWaterfall.shader` | Forward 加入主光阴影变体、阴影参数和颜色合成；水帘生成与变形保持原样。 |
| `Assets/Rendering/Moebius/Shaders/Materials/MoebiusWaterfallSplash.shader` | Forward 增加同样的阴影接收，保留云状网格变形和颜色模块。 |
| `Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusWaterfallInput.hlsl`、`MoebiusWaterfallSplashInput.hlsl` | 各自 UnityPerMaterial 增加阴影接收参数。 |
| `Assets/Rendering/Moebius/Editor/Inspector/MoebiusWaterShaderGUI.cs` | 水体 Inspector 调用共享冲击参数分组。 |
| `Assets/Rendering/Moebius/Editor/Inspector/MoebiusWaterfallShaderGUI.cs` | 英文冲击分组、绑定入口、生命周期提示；水帘/水花增加 Receive Shadows 分组。 |
| `Assets/Rendering/Moebius/Editor/Waterfall/MoebiusWaterfallImpactSetup.cs`（新增） | 编辑器绑定窗口：从 Sheet Size 与 Transform 求水帘底部端点，写入接收材质并复制水流方向。共享材质会先另存副本。没有 Update 或运行时动画。 |
| `Assets/Rendering/Moebius/Editor/Waterfall/MoebiusWaterfallSetup.cs` | 新建样例时停用旧贴片、绑定下层水体，设置一致的向下游流向。 |
| `Assets/Rendering/Moebius/Samples/Waterfall/MoebiusWaterfall.prefab` | 两个旧叠加网格设为 inactive，原水帘与四层 Splash 保留。 |
| `Assets/Rendering/Moebius/Samples/Waterfall/MoebiusWaterfallDemo.unity` | 更新现有样例以使用下层水面冲击；保持样例资产引用。 |
| `Assets/Rendering/Moebius/Samples/Waterfall/Materials/LowerWater.mat` | 开启冲击、保存端点和推荐参数；Impact Phase = 5 秒使初始预览已有波纹。 |
| `Assets/Rendering/Moebius/Samples/Waterfall/README.md` | 当前英文操作步骤、默认参数、阴影要求和限制。 |
| `Tools/MoebiusValidation/Editor/MoebiusWaterfallValidation.cs` | 增加实际 GPU 波纹传播、循环连续性、衰减、零强度、真实投影阴影和多相机截图检查。 |
| `Tools/MoebiusValidation/WaterfallWorldProbe.shader`（新增） | 仅验证项目中使用：输出世界 XZ，以测量实际波纹传播距离；不进入游戏资产。 |
| `Tools/ValidateMoebiusWaterfall.ps1` | 将坐标探针复制到隔离验证项目。 |

新增 Unity 代码/include 都有 `.meta`；三个交付样例资产的 GUID 与原项目逐一核对一致。没有替换原水帘或云状 Mesh 文件。

## 传播数学

冲击起点 A、终点 B 是水帘底边的世界空间端点。对水面像素 P，先求最近线段位置：

```hlsl
u = saturate(dot(P - A, B - A) / max(dot(B - A, B - A), epsilon));
d = length(P - (A + u * (B - A)));
```

计算使用 XZ。这样宽瀑布产生沿底边展开的波前：中央沿底边平行传播，两端向外弯曲，避免所有波纹挤在单一圆心。A、B 重合时安全退化为点源。波源只按水流方向产生每秒 0.08 米的轻微偏移。

每隔 Spawn Interval 产生一个波纹 ID。每道波纹的出生时刻是 `ID × Interval`，年龄是 `Time - BirthTime`，半径是 `ImpactRadius × 0.28 + Age × RippleSpeed`。环线距离场是 `d - radius + distortion`，零值附近为传播中的细线。这是每秒增加传播距离，不是缩放静态贴片或滚动环形纹理。

最多八道，默认六道。队列只保留最近 Count 个 ID；实际 Lifetime 限制为 `min(Lifetime, Count × Interval)`，确保出队前已经不可见。出生和死亡都使用 smoothstep，循环换 ID 时没有直接裁断可见笔画。

## 连续扰动与断续弧线

复用现有 `PrintNoise` 和 `PrintHash`，没有额外纹理采样。

- **Distortion**：世界 XZ 低频噪声改变距离场，每个 ID 带不同但固定的 seed。Noise 值映射到正负 Distortion 米，让传播轮廓自然弯曲。不会每帧重新随机选种子。
- **Broken Arcs**：另一组稳定空间 Noise 经过阈值和 smoothstep 得到断裂 Mask。Arc Breakup 控制留白比例；Arc Length Variation 控制噪声空间尺度，从而改变段落长度。不同 ID 使用不同图案。
- **宽度变化**：低频 Noise 让线宽在默认宽度的 0.65–1.3 倍之间变化；距离/年龄衰减同时让线变细。
- **抗锯齿**：`fwidth(distanceField)` 给出一个像素覆盖的世界距离，用于细线覆盖率。亚像素线不会硬开/关；过远/掠射视角的极细波纹主动淡出，避免远处密集闪线。

它不会生成完整亮黄色边框。默认 Ripple Color 是低对比浅青色，Intensity = 0.52；浅色弧线不取代原水色。

## 中心湍流和外围消散

中心泡沫使用同一个底边距离，受 Impact Radius 限定。两组低频 Noise 连续随时间平移并缓慢相对变化，生成大小不同的浅青色块、空洞和柔和变化的边缘；中心还有较小的滚动泡沫补充。没有量化时间、帧号随机种子或每帧随机跳变。

中心区域延伸到原有云状 Splash 下方，泡沫颜色采用与水花相近的浅青色，边缘露出一些不规则色块，连接立体水花与水平水面。云状 Splash Mesh、层数和呼吸动画没有重写。

外围涟漪同时乘年龄 Envelope、距离 Envelope 和 Broken Arc Mask。默认 6 米最大传播距离，从 2.7 米开始距离淡出；默认 6.6 秒生命周期最后 45% 逐渐消散。边界处强度趋于零，并非到达固定距离后突然裁切。远处恢复原有水体短线；中心模块之外仍使用原 Water Shader。

颜色顺序：原水色/水流短线 → 原岸线泡沫 → 新中心泡沫与传播弧线 → 原水面投影阴影 → 输出。Impact Off 直接返回原颜色；Turbulence 和 Ripple Intensity 同时为零也得到相同像素。

## 瀑布承接阴影

水帘和 Splash 在可见颜色 Pass 采样现有 URP 主光源阴影，深阴影增加少量交叉排线，底色只轻微变暗。竖直水帘选 XY 等主平面作为排线坐标，避免全部压在 XZ 上。参数是 Receive Main Light Shadows、Shadow Ink Color、Shadow Ink Strength、Shadow Hatch Density。默认开启，强度 0.55，密度 8 条/米。

仅主光源阴影。光源和 URP Renderer 的 Shadows 必须开启，投影物体须启用 Cast Shadows。没有新增全屏 Pass。既有水帘/Splash ShadowCaster、DepthOnly、DepthNormals 继续使用原变形与裁切。

## 推荐参数与调节

| 参数 | 默认值 | 作用 |
|---|---:|---|
| Impact Radius / Half Depth | 1.05 m | 泡沫区域沿底边两侧的深度；沿线宽度由 A/B 决定 |
| Turbulence Strength | 0.8 | 中心泡沫与边缘变化强度 |
| Turbulence Noise Scale | 0.85 | 中心块形密度，越大块形越小 |
| Foam Density | 0.58 | 泡沫色块覆盖比例 |
| Foam Motion Speed | 0.65 | 连续滚动变化速度 |
| Impact Foam Color / Ink Strength | RGB 0.82/0.93/0.93 / 0.12 | 浅青泡沫与克制内部线 |
| Ripple Speed | 0.9 m/s | 向外传播速度 |
| Ripple Spawn Interval | 1.1 s | 相邻波纹出生间隔 |
| Ripple Lifetime | 6.6 s | 寿命，受 Count × Interval 上限约束 |
| Ripple Max Distance | 6 m | 已完全淡出的距离 |
| Ripple Count | 6（上限 8） | 同时保留的波纹数量 |
| Ripple Width | 0.024 m | 基础线宽，随噪声和衰减变化 |
| Ripple Distortion / Noise Scale | 0.2 m / 0.7 | 波前弯曲幅度与空间密度 |
| Arc Breakup | 0.67 | 越高，断裂留白越多 |
| Arc Length Variation | 0.55 | 越高，分段更短、更密 |
| Ripple Color / Intensity | RGB 0.66/0.82/0.84 / 0.52 | 低对比浅青色波纹 |
| Distance Fade | 0.55 | 最后 55% 距离范围逐渐淡出 |
| Lifetime Fade | 0.45 | 最后 45% 寿命逐渐淡出 |

普通水材质的 Enable Waterfall Impact 默认关闭。仅下层样例材质开启。若想波纹更明显，先将 Intensity 调到 0.65；不建议重新使用高亮黄色或把 Arc Breakup 降到接近零。

## 实际验证

使用隐藏 Unity 进程在隔离项目实际编译、渲染现有 Waterfall Sample，没有关闭或操作用户打开的编辑器。最终 **51 项 PASS，Shader/runtime/test errors = 0**，原始日志为同目录 `Report.txt`。

- 四个 Shader 编译无错误，Unity 查询均为 SRP Batcher compatible。
- 中心和涟漪在原水 Mesh 上可见；固定时间两次渲染完全相同。
- 每次 1/60 秒推进均发生平滑变化，冲击模块采样帧平均变化约 2294 像素。该测试证明没有离散时间停顿，不代表运行帧率恒定 60 FPS。
- GPU 单波传播测量：平均距冲击线 1.44 → 2.51 米，间隔一秒，测试速度 1 m/s。误差包含波源偏移、噪声段落权重和探针量化。
- 默认循环接缝前后最大通道变化 1/255；单波寿命终点与新波出生时强度均接近零。
- 断续波纹在测试距离带内占 10% 像素；该统计是带内覆盖率，不是完整环线弧长百分比。配合实际图像检查确认留有断口。
- 零湍流/零波纹强度与关闭模块逐像素一致。
- 水帘与四层云状 Mesh 的 Forward、DepthOnly、原始法线、八面体法线轮廓对比均为 0 mismatches；执行了 ShadowCaster。
- 真实主光源 + 临时 ShadowsOnly 方块：水帘和前层 Splash 阴影分别改变 23197、9198 像素；移动投影物体后阴影变化；Shadow Strength = 0 与关闭接收逐像素相同。
- 观察 Overview、Closeup、CameraLeft、CameraLow、CameraPerspective：落水位置与底边相符，原黄色闭合外圈消失，新增波纹没有贴片边界或独立 Mesh Z-Fighting。掠射视角细线会变弱。
- 保存场景重新打开，所有 Mesh/Material 引用正确。交付场景、Prefab、LowerWater 材质与验证资产哈希一致，原 GUID 保留。

主要截图为 `Reopened.png`、`IntegratedImpact.png`、`CameraPerspective.png`；阴影对照图文件名含 ShadowOff / ShadowOn / ShadowMoved。原效果截图在 `BeforeImpact` 中。

## 性能和剩余限制

样例从七个瀑布效果 Renderer 减为五个，省去两个旧贴片绘制。冲击合成没有新增 Draw Call、纹理采样或材质逐帧更新；增加的是局部水面像素的程序噪声、最多八道波纹距离运算。默认六道，冲击范围之外提前返回。水帘/Splash 新增主光源阴影采样和少量排线运算。没有在真实游戏负载下进行 GPU Profiler 或设备帧率基准，因此不能保证净 GPU 时间一定减少。

多视角截图中，正交相机下原水面远离冲击区域的外边缘仍有黑色描边斑点；同样斑点出现在 `BeforeImpact/Closeup.png`，不是本次冲击贴片造成。遵照保留现有描边系统的范围，本次没有修改该模块。没有在用户编辑器中进行长时间交互相机巡游，不能声称所有视角和设备都无闪烁。

冲击是水平水面的插画表达，不是碰撞或流体模拟；不会检测障碍并自动绕行。每个材质一次支持一个底边冲击区域。端点为静态作者数据：移动、缩放或旋转瀑布后使用 **Tools > Moebius > Waterfall > Bind Impact to Water** 重新绑定，不需要烘焙岸线。运行时移动的瀑布需要额外的位置更新方案，本次没有添加这样的控制器。云状水花本身保持原造型，并未改成真实喷溅粒子。
