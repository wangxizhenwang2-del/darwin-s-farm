# Waterfall Impact Tracking and Splash Shape Report

## 已完成的效果

承接水面的落水线现在自动跟随水帘的世界位置、旋转、缩放和材质 Sheet Size。编辑模式和 Play Mode 都工作。当前 MoebiusWaterfallDemo 已保存绑定引用；其他场景第一次选择水帘与承接水面后点击 Bind Impact 即可，之后不必反复绑定。

Splash 保持四个封闭、不透明的 Mesh，但实际几何已替换。原有圆盘加外围小圆瓣的轮廓生成器被移除，改为分别设计的不对称轮廓及浅体积。瀑布水帘、水面 Shader、Impact 距离场及 Moebius 描边 Renderer Feature 没有重写。

## 修改文件

代码路径位于 `Assets/Rendering/Moebius/`：

| 文件 | 改动 |
|---|---|
| Runtime/Controllers/MoebiusWaterfallImpactBinding.cs（及新 .meta） | 新增 ExecuteAlways 组件，在 LateUpdate 和相机渲染前同步底边端点；参数不变时跳过 PropertyBlock 写入；失效或禁用后恢复原有覆盖。 |
| Editor/Waterfall/MoebiusWaterfallImpactSetup.cs | 原有绑定按钮新增组件、指定水帘、记录 Undo 和场景修改；仍保存可独立显示的材质端点作为 fallback。 |
| Editor/Waterfall/MoebiusWaterfallSetup.cs | 移除圆瓣拼接生成器，创建四种单独设计的轮廓和封闭体积；调整布局、缩放、颜色和各层动作参数。保留已有资产 GUID 和子物体名称。 |
| Shaders/Materials/MoebiusWaterfallSplash.shader | 加入 Splash Motion Role。可见着色、阴影和描边方式延续现有实现。 |
| Shaders/Includes/Inputs/MoebiusWaterfallSplashInput.hlsl | 将动作角色加入统一 UnityPerMaterial 缓冲。 |
| Shaders/Includes/Features/MoebiusWaterfallSplashMotion.hlsl | 后层固定底部、轻微抬升；主层稳定呼吸；前层沿水面轻微涨退。旧材质默认 Legacy，保留原动作。所有 Pass 共用形变。 |
| Editor/Inspector/MoebiusWaterfallShaderGUI.cs | 暴露英文 Motion Role 参数，更新自动绑定和形状说明。 |
| Samples/Waterfall/MoebiusWaterfall.prefab | 保存新四层布局。 |
| Samples/Waterfall/Meshes/SplashCloud0.asset 至 SplashCloud3.asset | 替换四个 Mesh 的实际几何；文件名称仍保留以避免破坏已有引用。 |
| Samples/Waterfall/Materials/SplashLayer0.mat 至 SplashLayer3.mat | 保存各层颜色与动作参数。 |
| Samples/Waterfall/MoebiusWaterfallDemo.unity | 在 Lower Moebius Water 上保存自动同步组件及水帘引用。 |
| Samples/Waterfall/README.md | 更新使用方法、参数及限制。 |

`Tools/MoebiusValidation/Editor/MoebiusWaterfallValidation.cs`：增加自动渲染回调、Play Mode 同步、材质隔离、禁用恢复与替换几何检查；固定时间 GPU 检查现在也读取 Renderer 的实际属性覆盖。

## 落水线如何实时同步

1. 从水帘材质读取 Sheet Size。
2. 在局部空间计算底边端点 `(-width/2, -height/2, 0)` 与 `(width/2, -height/2, 0)`。
3. 使用水帘 TransformPoint 转为世界坐标，自然包含所有父级位置、旋转和缩放。
4. 只把两个端点写入承接水面的第 0 材质槽 MaterialPropertyBlock，不逐帧改写共享材质资源。
5. 现有水面 Shader 按端点的世界 XZ 距离，继续生成中心泡沫和向外扩散的断续波纹。

场景中的组件引用是持久关联，实际端点为运行时覆盖。选中承接水面时，组件会画出水帘底边线。材质 Inspector 中的端点是初次绑定保存的 fallback，不代表移动后的即时值。关闭 Enable Waterfall Impact 仍可正常关闭整个表面效果。

## 新 Splash 的层次与形状

| 现有子物体 | 新角色 | 形状与颜色 |
|---|---|---|
| Splash Cloud Layer 1 | 后方左侧水花 | 向左弯起的较高水脊，淡青色；提供向上反作用力的暗示。 |
| Splash Cloud Layer 2 | 后方右侧水花 | 较低、向右展开的水脊，偏蓝绿色；与左侧错开高度及轮廓。 |
| Splash Cloud Layer 3 | 主冲击水花 | 最大、低而宽，底边接近平坦；上沿峰值大小不同，两端向外延伸。近白色，形成主要视觉色块。 |
| Splash Cloud Layer 4 | 前景贴水泡沫 | 水平 XZ 展开的浅体积，具有长短不一的伸出边缘，淡青白；部分浸入水面并覆盖主水花下缘。 |

轮廓以明确的控制点和温和的 Hermite 曲线生成，避免等间距、同尺寸圆瓣。后方层只作辅助，主水花保持低重心；前景层与现有 Impact Foam 的范围重叠，使体积逐渐接到水面色块。

四个 Mesh 共 7,616 顶点、14,208 三角形；上一版本为 12,100 顶点、23,040 三角形。仍是四个 Splash Renderer，不增加水花层数或粒子。材质保持不透明颜色、少量冷暖层次和原有主光阴影排线。

## 推荐参数

| 层 | Motion Role | Pulse Speed | Pulse Amplitude | Wobble Strength | Splash Fill Color（RGB） |
|---|---|---:|---:|---:|---|
| 后左 | BackLift | 0.85 | 0.018 | 0.022 | 0.73, 0.88, 0.89 |
| 后右 | BackLift | 0.85 | 0.018 | 0.022 | 0.80, 0.92, 0.91 |
| 主体 | MainMass | 0.70 | 0.012 | 0.012 | 0.97, 0.98, 0.95 |
| 贴水前层 | SurfaceSpread | 0.65 | 0.025 | 0.018 | 0.88, 0.96, 0.94 |

Wobble Speed = 0.8；Internal Color Variation = 0；Secondary Color Strength 后层与主层为 0.28、前层为 0.22。Animation Phase 保持 0 / 1.7 / 3.4 / 5.1 秒。动画全部连续，没有按帧随机切换或量化时间。

Impact 参数延续上一轮：Radius 2.1 m，Turbulence 0.95，Foam Density 0.68，Ripple Speed 0.9 m/s，Spawn Interval 1.1 s，Count 6，Lifetime 6.6 s，Width 0.065 m，Intensity 0.85。此次没有重写涟漪算法或降低其可见度。

## 实际验证

使用 Unity 6000.2.9f1 / URP 17.2 的独立验证项目，结果为 **68 PASS、0 错误**：

- 四个相关 Shader 成功编译；UnityPerMaterial 布局仍兼容 SRP Batcher。
- 相机渲染前自动跟随父级位移、旋转、非均匀缩放，及水帘单独移动、Sheet Size 编辑。
- 真实 Play Mode 运行帧中，无手动 SynchronizeNow 调用也正确更新端点。
- 渲染的 Impact 场实际随端点移动；材质资源中的端点值未被同步组件修改；其他共享材质 Renderer 没有收到覆盖。
- 禁用组件恢复原覆盖，重新启用立即同步。
- 所有水花 Mesh 的绕序朝外，法线有效；前景层穿过水面高度。
- 水帘与四层水花在 Forward / DepthOnly / 原始及八面体 DepthNormals 的轮廓差异为 0 像素。
- 固定时间重复绘制完全一致；每个采样的 1/60 秒间隔均有动画变化。
- 水帘和贴水水花接收真实主光投射阴影，并跟随移动的遮挡物。
- 涟漪仍向外传播，生命周期结束前平滑消失，周期交接无明显跳变。
- 保存场景重新打开后 Mesh、材质和自动绑定引用有效。

实际截图：Reopened.png、Closeup.png、CameraLeft.png、CameraLow.png、CameraPerspective.png、ImpactTrackingMoved.png、PlayModeTracking.png。已逐张检查主要视角的 Splash 轮廓与水面连接。

## 剩余限制

- 落水线仍按 Sheet Size 推算，不识别任意 Mesh 底边，不进行碰撞检测；距离场使用世界 XZ，适合近水平水面。只改水帘高度不会让系统自动寻找新的水面。
- 移动整个瀑布 prefab 时水花子物体随父级一起移动；单独移动水帘时只自动同步 Impact，水花的位置需自行调整。改变水位后也需调整贴水 Mesh 高度。
- 已生成的 Shader 波纹跟着当前落水线整体移动，不记录过去的传播圆心。一片接收水面的第 0 材质槽只支持一条落水线。
- 使用 MaterialPropertyBlock 的承接水面 Renderer 不走 SRP Batcher；其他 Renderer 和 Shader 布局不受影响，也没有增加渲染 Pass 或 Draw Call。请避免其他脚本同时覆盖该槽的 PropertyBlock。这里未做目标设备的长期帧率测试。
- 特写的水面远边仍可见原有描边伪影，此次没有修改既有 Fullscreen Outline。水花属于封闭浅体积，侧面观感会与二维原画不同；仍需结合最终关卡构图调整。

修改前的样例资产与主要截图备份在 `Artifacts/MoebiusWaterfall/BeforeImpactTracking/`，保留原 .meta 供恢复参考。