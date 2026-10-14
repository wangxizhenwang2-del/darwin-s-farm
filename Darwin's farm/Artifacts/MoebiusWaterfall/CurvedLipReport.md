# 瀑布顶部圆弧过渡

已更新当前 Waterfall Sample 的网格和材质，场景文件、Prefab 布局、水花网格及参数没有被覆盖。

## 修改内容

- `Assets/Rendering/Moebius/Editor/Waterfall/MoebiusWaterfallSetup.cs`：水帘顶部由轻微 Z 偏移改为半径 0.75 的四分之一圆弧；使用归一化弧长 UV 与解析法线。上层水面网格分成三段，中央为曲面留出对应开口，仍合并为一个 Mesh、一个材质和一个 Renderer。增加只更新顶部资源的 RebuildSampleLip 方法。新建示例也使用相同形状。
- `Shaders/Materials/MoebiusWaterfall.shader` 和 `Shaders/Includes/Inputs/MoebiusWaterfallInput.hlsl`：增加 Surface Length、Curved Lip Start V、Upstream Water Color 三个协调参数，保留 UnityPerMaterial 布局。它们是网格对应的着色数据，不会自动改变网格形状。已有平直水帘默认保持旧着色。
- `Shaders/Includes/Features/MoebiusWaterfallFlow.hlsl`：以真实曲面路径长度换算流动坐标，顶部接缝渐渐关闭侧边裁切，下面的侧边仍使用原有不规则线条。
- `Shaders/Includes/Features/MoebiusWaterfallLines.hlsl`：曲面高光集中在弯曲处，水平端逐渐匹配上层水色。原有水纹与底部颜色逻辑保留。
- `Editor/Inspector/MoebiusWaterfallShaderGUI.cs`：沿用英文 Inspector，显示参数并说明它们与网格的关系。
- `Samples/Waterfall/Meshes/WaterfallSheet.asset`、`UpperWaterGrid.asset`：更新几何，保留原 GUID 和所有引用。
- `Samples/Waterfall/Materials/WaterfallBody.mat`：设置路径长度 6.428097、圆弧起点 V=0.8167269、上层水色。`UpperWater.mat`：保持入口高度不变，水纹流向 -Z。仅上层几何波高设为 0，流动和涟漪仍开启。
- `Samples/Waterfall/README.md`：补充在其他场景中对齐入口的说明。
- `Tools/MoebiusValidation/Editor/MoebiusWaterfallValidation.cs`：增加曲面接缝、法线、流动长度、网格朝向和穿模检查，以及顶部近景截图。

## 形状与流动

水帘宽 5.8、高 6，竖直段长 5.25，顶部弧长 0.75 × π/2。沿圆弧使用 y = 3 − r + r sin(θ)、z = r(1 − cos(θ))，θ 从 0 到 π/2。圆弧底端与竖直水帘切线连续，上端与水平水面切线连续。底部落水线仍是局部 y=-3、z=0，无须重新绑定冲击区域。

流动坐标为 V × Surface Length + Time × Flow Speed。V 以弧长均匀分配，因此同一水纹沿曲面向下流动，弯曲处不会额外拉伸一套纹理。没有增加纹理采样、Renderer Feature、Render Pass、运行时动画脚本或水帘 Draw Call。

## 验证

在独立 Unity 6000.2.9f1 / URP 17.2 测试项目完成 78 项检查，Shader/runtime/test errors 为 0。

- 顶部接缝的最近顶点间距小于 0.00001 m；曲面与悬崖没有穿插。
- 相邻圆弧行最大法线转角 5.12°；首尾分别匹配竖直水帘和水平水面。
- 网格实际路径长 6.427706 m，着色路径 6.428097 m，误差约 0.39 mm。
- 上层水面从上方正确渲染；水帘 Forward、Depth、原始与八面体法线 Pass 轮廓一致，0 个不匹配像素。
- 四个 Shader 编译成功并保持 SRP Batcher 布局兼容；真实主光阴影正常。
- 水帘和水花在每个采样的 1/60 秒间隔连续变化；现有冲击泡沫、涟漪生命周期、断续弧线与实际 Play Mode 自动落水点跟随继续通过。
- 已人工检查顶部近景与整体截图：水面顺着圆弧转入水帘，没有顶部黑色直角接缝。
- 验证过的四个资源已复制回主项目，文件哈希匹配；GUID、原场景和 Prefab 文件保持不变。没有关闭用户的 Unity Editor。

备份位于 `BeforeCurvedLip`。近景为 `CurvedLipCloseup.png`，全景为 `Reopened.png`。新场景的上层水面仍需要对齐水帘入口；自动 Impact Binding 负责下层冲击位置，不负责移动独立的上层水面。
