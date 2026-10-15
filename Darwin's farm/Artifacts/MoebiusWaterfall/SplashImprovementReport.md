# Moebius 云状水花造型调整

原水花与参考图的主要差别是造型：四层网格均被横向拉宽、纵向压扁，轮廓圆瓣之间又过度重叠，因此像堆叠椭圆。原填色以表面法线区分颜色，圆弧表面形成明暗带，增加了椭球体的观感。参考图中的水花则是向上冒起的大块云团，有高低不同的轮廓、大小不同的圆瓣和少量浅色层次。

本次已直接调整现有四层 Splash：

- 轮廓由核心圆与更小的周边圆瓣组成，减少圆瓣相互遮盖；增加上部高度与肩部鼓起。
- 四层分别作为后方宽云团、左侧高喷起、右侧中等喷起、前景白色水花，改变原来相近的宽扁比例。
- 仍为封闭且有厚度的 3D Mesh；轮廓采样从 80 段增加至 120 段，纬向仍为 24 段。保留接缝/端点法线处理与变形余量。
- Splash Shader 的颜色选择由 normal.y 改为局部高度：浅色顶部与冷色底部，减少圆体表面的明暗带。原 Fill Color、Secondary Fill Color、Secondary Color Strength 继续控制配色。
- 四个样例材质停用内部噪声色块，前景改为偏冷白，后方保留浅青色。原顶点动画、四层时间偏移、轮廓线与投影阴影继续使用。
- 已增强的 Impact Foam 和 Surface Ripples 未修改。水帘、原 Renderer Feature、生产场景也未修改。

## 文件

项目根目录：`D:/GameAssignment/darwin-s-farm/Darwin's farm`。

| 文件 | 内容 |
|---|---|
| `Assets/Rendering/Moebius/Editor/Waterfall/MoebiusWaterfallSetup.cs` | 调整 CreateCloud；共用 ConfigureSplashLayers 配置四层。增加仅编辑器使用的 RebuildSampleSplash，原位更新样例资产。 |
| `Assets/Rendering/Moebius/Shaders/Materials/MoebiusWaterfallSplash.shader` | 修改填色 Mask，保留其他 Pass、动画和接收阴影。 |
| `Assets/Rendering/Moebius/Samples/Waterfall/Meshes/SplashCloud0.asset` 至 `SplashCloud3.asset` | 更新四个现有闭合云状网格。 |
| `Assets/Rendering/Moebius/Samples/Waterfall/Materials/SplashLayer0.mat` 至 `SplashLayer3.mat` | 更新配色、次色强度与内部 Noise 强度。 |
| `Assets/Rendering/Moebius/Samples/Waterfall/MoebiusWaterfall.prefab` | 更新四个现有子物体的位置、比例与资产引用。 |
| `Assets/Rendering/Moebius/Samples/Waterfall/README.md` | 补充新的轮廓与填色说明。 |
| `Tools/MoebiusValidation/Editor/MoebiusWaterfallValidation.cs` | 在隔离项目原位刷新样例 Splash，再执行原有验证。 |

交付的九个样例资产都核对了原 GUID，并与通过验证的资产哈希一致。样例场景通过现有 Prefab 引用得到更新，本次没有重写主项目场景。旧网格、材质、Prefab 及对照截图已保存在 `BeforeSplash`。资产绝对路径列表见 `FilesChangedSplashAssets.txt`。

## 验证

Unity 6000.2.9f1 / URP 17.2.0 隔离项目实际编译和渲染：**51 项 PASS，Shader/runtime/test errors = 0**。

- 四个 Splash 的可见、深度、原始法线、八面体法线轮廓比较均为 0 mismatches，执行了 ShadowCaster。
- 每次 1/60 秒采样均有连续动画变化；固定时间重复渲染相同。
- 水帘与前景 Splash 能接收移动物体的主光源阴影，零强度等于关闭接收。前景 Splash 阴影对照改变 14131 像素。
- 水面冲击传播、断续弧线、生命周期连续性、衰减与零强度验证继续通过。
- Shader 编译无错误，SRP Batcher 兼容；保存后重开引用正确。
- 检查 Overview、Closeup、CameraLeft、CameraPerspective，云团具有向上的高低层次，前景白色填色更清楚。截图仍保留原样例的构图，未复制参考原画的整个场景。

没有新增 Renderer、材质数量、粒子或运行时脚本。四层云团几何总量从约 15360 三角形增加到 23040 三角形；闭合网格有更细的圆瓣轮廓，因此增加了顶点成本。没有进行真实游戏 GPU 帧率基准。模型仍有相对浅的厚度，极端侧视不会与参考原画的固定二维轮廓完全相同。原正交视角水面外边缘描边斑点仍存在，本次未修改该模块。
