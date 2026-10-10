# Moebius Scene View Paint 实现报告

这是一个编辑时使用的 Scene View 笔刷工具。它给现有 Moebius Shader 写颜色与控制贴图，保留现有光照、空间渐变、排线、点描、高光、描边及 Renderer Feature。无需为物体添加碰撞器。

## 使用方法

1. 在 Hierarchy 中选择带 `MeshFilter + MeshRenderer`、使用 `Darwin/Moebius NPR` 的模型。多材质模型先选目标 Material Slot。
2. 打开 `Tools → Moebius → Paint`。
3. 首次使用点击 **Make Material Unique For This Object**。这一步由用户主动执行，只复制当前槽位的材质，保护原始资产。
4. 选择 Paint Layer 和分辨率，点击 **Create / Open Paint Texture**。已有贴图保留原尺寸，上限 2048；分辨率选项用于创建新贴图。
5. 设置 Size、Strength、Falloff、Spacing，以及 Color 或 Paint Value。点击 **Activate Scene Paint Tool**。
6. 鼠标移到模型上，笔刷显示中心、外半径、衰减内圈和表面法线方向。左键拖动绘制，Shift 或 Erase 擦除。Alt、右键、中键保留 Scene 导航；Esc 退出笔刷工具。
7. Ctrl+Z / Ctrl+Y 撤销、重做整笔；**Save** 持久保存，**Revert** 恢复到最近保存，**Clear Layer** 清除当前通道。

独立演示场景：`Assets/Rendering/Moebius/PaintDemo/MoebiusPaintDemo.unity`。其中 `Moebius Paint Surface` 使用从现有 Ivory 材质复制的专用材质，有一条保存好的柔边粉色笔迹，可以继续画。正式场景未被修改。

## 可绘制数据及原有行为

| Paint Layer | 写入位置 | 作用 | 擦除/清除 |
| --- | --- | --- | --- |
| Color | 新增 `_PaintColorMap`：RGB 颜色、A 覆盖量 | 局部修改进入 NPR 光照前的底色 | A 恢复 0，露出原底色 |
| Hatching | 现有 `_ControlMap.r` | 控制表面与投射阴影排线的局部强度 | R 恢复 1 |
| Detail | 现有 `_ControlMap.g` | 同时控制排线细节和点描，沿用已有耦合 | G 恢复 1 |
| Highlight | 现有 `_ControlMap.b` | 控制现有高光与高光边线 | B 恢复 1 |
| Shadow | 现有 `_ControlMap.a` | 控制原有明暗、阴影响应及与其关联的点描 | A 恢复 1 |

这里的 Mask 是现有效果的乘数。0 通常抑制效果，1 恢复材质原来的作用；不是直接添加一种新效果。材质本身的 Hatching / Stippling 开关和全局强度仍在 Material Inspector 中设置。工具不会绘制额外的投射阴影形状，也不会把已关闭的效果强行打开。

原有顶点色继续保留：R 控制排线、G 控制高光、B 控制明暗。此次没有更换顶点色通道，也没有增加第二套排线/点描/高光 Mask。主 Shader 沿用 ShaderLab Properties 自动生成 Inspector；本次没有增加 ShaderGUI 或修改原有参数 Drawer。

## 架构与笔刷计算

```text
Scene View 鼠标位置
    ↓ HandleUtility.GUIPointToWorldRay
选中 Mesh 的局部空间射线
    ↓ 编辑器只读 Mesh 数据 + 缓存 BVH + 三角形求交
命中位置 / 插值法线 / UV0 / Submesh
    ↓ 检查目标材质槽
世界空间笔刷范围，在 Mesh UV 中光栅化
    ↓ GPU Ping-Pong
颜色贴图 / 现有 Control Map
    ↓ MaterialPropertyBlock 实时预览
现有 Moebius Shader
    ↓
最终画面
```

射线通过物体矩阵转到 Mesh 局部空间，BVH 缩小候选三角形范围，再计算最近的射线交点。用三角形重心坐标插值得到 UV0 和法线；法线通过逆转置矩阵回到世界空间。读取采用编辑器 `MeshUtility.AcquireReadOnlyMeshData`，不要求 MeshCollider，也不改模型的 Read/Write 设置。

Size 是**世界单位直径**，半径是 Size / 2。GPU 将选中 Submesh 的 UV0 映射到工作贴图的裁剪空间，同时传入顶点世界坐标。像素根据它所对应的表面位置与笔刷中心的世界距离判断覆盖范围，因此不依赖顶点颜色密度。贴图分辨率和 UV 分配仍决定可画出的最小细节。

归一化距离 `d = distance(surfacePosition, brushCenter) / radius`；权重为：

```hlsl
weight = (1 - smoothstep(1 - falloff, 1, d)) * strength;
```

Falloff 越大，柔边区域越宽；Strength 决定一次印章靠近目标值的程度。Mask 用 `lerp(oldChannel, targetValue, weight)`，其余三个通道保留。颜色使用覆盖量合成并保存未预乘的 RGB，擦除只降低 A。笔刷还排除明显背向命中法线的表面。

MouseDown 开始一笔并绘制第一枚印章。拖动按 `Size × Spacing` 的世界距离补充印章；在两次鼠标位置间插值并逐次重新射线检测，避免简单地跨过曲面或刷入别的材质槽。MouseUp 提交整笔。单次事件最多补 128 枚印章，防止异常长拖动卡住编辑器。

## GPU 与持久化

工作贴图使用两个 ARGB32 RenderTexture。每枚印章先把 Current 复制到 Spare，再以 Current 为采样源、Spare 为绘制目标，只光栅化选定 Submesh，最后交换两者。未覆盖像素保留复制值；不会把同一张 RT 同时作为读源和写目标。

贴图复制、恢复与绘制会还原原先的 RenderTexture 绘制目标，UV 绘制使用显式物体矩阵、贴图 Viewport 和独立裁剪状态。这样 Scene View 的 Handles 预览不会混入工作纹理；此项有实际 Repaint 后逐像素不变的回归检查。

拖动时通过目标槽位的 MaterialPropertyBlock 绑定工作 RT，材质资产不会保存临时 RT 引用。相机渲染前重新合并预览覆盖，以兼容当前空间渐变控制器的属性更新。这只是编辑器预览回调，不增加 Renderer Feature 或运行时 Render Pass。

CPU 不进行逐像素笔刷计算。每笔完成时做一次 GPU 回读并编码 PNG，用于整笔 Undo 快照；因此较大贴图在抬笔时仍有回读/编码开销。Save 把快照写为 PNG，导入为正式 Texture2D，再设置专用材质的纹理引用。

- 颜色贴图：sRGB，RGBA8，无压缩、无 Mipmap，A 保存覆盖量。
- 控制贴图：Linear，RGBA8，无压缩、无 Mipmap，四通道保存现有控制数据。
- 保存目录：`Assets/Rendering/Moebius/PaintedTextures/`，文件名带对象和材质名，使用唯一资产路径，避免覆盖旧文件。
- 切换对象、材质槽或颜色/控制工作区时，未保存内容会提示保存、取消或放弃。
- 保存场景、关闭工具、进入 Play Mode、脚本重载或正常退出 Unity 时，会保存未提交工作。
- 若外部修改导致材质变成共享材质或绑定被替换，正常保存会被阻止；关闭工具时将笔迹另存为 Recovery PNG，并在 Console 给出路径，不修改共享材质。

Undo 记录编辑器内部快照对象，每笔一次。Undo / Redo 上传对应 PNG 回工作 RT。Clear 和 Revert 也可撤销。**撤销历史属于当前工作区**，关闭/切换工作区后清理；Save 后再 Undo 改的是工作区，需要再次 Save 或保存场景才能更新持久资源。

## Shader 接入与兼容性

只新增可选颜色入口 `_PaintColorMap` 与默认关闭的 `_UsePaintColor`。颜色插入位置：

```text
Base Map × Base Color
    ↓ Existing Spatial Color Gradient
    ↓ lerp(existingBaseColor, paintedRGB, coverageA) [可选]
    ↓ Existing Paper Lift
    ↓ Existing NPR Lighting / Lambert Color Ramp
    ↓ Existing Hatching / Cast-shadow Hatching
    ↓ Existing Stippling / Highlight
    ↓ Final Output + Existing Outline
```

Paint Color 是底色层，不在最终屏幕颜色上覆盖。这样新颜色仍经过现有 NPR 着色、排线、点描和高光。A=1 时局部使用完整画入颜色，A=0 时保持原底色。

`_UsePaintColor < 0.5` 直接返回原底色，不采样新增贴图；采样后的 A≤0 也直接返回原底色。额外提供透明 1×1 默认贴图，并用 ShaderImporter 的默认纹理引用保存到 Shader `.meta`，避免空贴图默认 alpha 的差异。旧材质无需批量升级。

控制通道已经存在，不需要修改原有 Hatching、Stipple、Highlight 或 Lighting 代码。ShadowCaster、DepthOnly、DepthNormals 的程序、Outline Shader、Runtime Renderer Feature 都保持原内容。共享 CBUFFER 只增加一个颜色绘制开关，保证各 Pass 布局统一；绘制贴图采样函数只在前向颜色 Pass 引入。

## 文件职责

所有新笔刷 C# 都位于 `Assets/Rendering/Moebius/Editor/Paint/`：

| 文件 | 职责 |
| --- | --- |
| `MoebiusPaintWindow.cs` | 菜单、目标/槽位/通道、笔刷设置、Save/Revert/Clear、生命周期保存 |
| `MoebiusPaintTool.cs` | 原生 EditorTool、Scene 鼠标交互、贴面 Handles 预览、连续印章、Shift 擦除、导航与退出 |
| `MoebiusPaintMesh.cs` | 只读 Mesh 数据、UV 检查、BVH 射线命中、UV/法线/材质槽插值 |
| `MoebiusPaintSession.cs` | 工作 RT、Ping-Pong、实时预览、整笔 Undo/Redo、保存/恢复、资源释放 |
| `MoebiusPaintAssets.cs` | 通道定义、材质保护与主动复制、PNG 导入设置、透明默认贴图绑定 |
| `MoebiusPaintBrush.shader` | 编辑器 GPU 笔刷和单通道清除，不参与运行时场景着色 |
| `MoebiusPaintValidation.cs` | 隔离项目内编译、渲染、Scene GUI 事件、Undo、安全与重启验证 |

其它新增/修改：

| 文件 | 改动 |
| --- | --- |
| `Shaders/MoebiusNPR.shader` | 新增两个颜色绘制 Properties、前向 include、底色混合调用 |
| `Shaders/MoebiusNPRInput.hlsl` | CBUFFER 增加 `_UsePaintColor` |
| `Shaders/MoebiusPaintColor.hlsl` | 新增可选颜色混合函数；关闭和零覆盖量均直接返回 |
| `Shaders/MoebiusNPR.shader.meta` | 绑定透明默认纹理，保留原 Shader GUID |
| `Defaults/TransparentPaint.png` | 透明安全默认颜色层 |
| `PaintDemo/MoebiusPaintDemo.unity` | 独立测试场景 |
| `PaintedTextures/` | 演示专用材质、颜色 PNG、控制 PNG |
| `Tools/ValidateMoebiusPaint.ps1` | 准备隔离工程，顺序启动 Unity Before / After / Reload 检查 |
| `Artifacts/MoebiusPaint/` | 修改前 Shader 快照、测试记录、图片与本报告 |

运行时只需要材质、正式纹理和 Shader。没有新增 Runtime C#。Player 程序集检查会确认 `Editor/Paint/` 不进入 Player 编译；本次没有执行完整游戏 Build。

## 安全与第一版边界

- 非 Moebius 材质拒绝绘制。原始材质必须先主动复制到绘制目录，即使它当前只被一个物体使用。
- 检查当前已加载场景中材质的所有槽位使用者，包括非激活物体；多个使用者会阻止绘制。保存前再次检查，避免工作期间新增共享使用者后误写。不要手动跨未加载场景复用绘制材质副本。
- 多材质模型通过 Submesh 命中确定槽位，GPU 只 DrawMesh 当前 Submesh。即使半径覆盖整个模型，也不会写其它槽位的工作区域。
- 当前支持静态 MeshRenderer；不支持 SkinnedMeshRenderer、变形后的动态顶点拾取。
- 需要有效 UV0 和法线；缺失时明确提示，不自动展开 UV，不改导入设置。UV 须在 0～1 内，Base Map 的 Tiling=(1,1)、Offset=(0,0)。
- 重叠 UV 共用笔迹；第一版没有 UV 岛边缘扩张、自动接缝修补或 UDIM。模型/UV 在工作期间被修改后，请关闭并重新打开工作区。
- 若其它脚本已经覆盖目标绘制贴图/开关，工具拒绝直接接管。当前空间渐变控制器的其它属性可以继续工作。
- 离开选中绘制表面的点击保留 Scene 选择；退出工具释放鼠标控制并恢复先前工具。

## 验证方式

在 Unity 6000.2.9f1 / URP 17.2 的隔离工程 `Temp/MoebiusBillboardValidation` 中执行真实编译、GPU 绘制与相机渲染。修改前 Shader 保存在 `Artifacts/MoebiusPaint/SourceBefore`。

依次运行：

```powershell
.\Tools\ValidateMoebiusPaint.ps1 -Label Before
.\Tools\ValidateMoebiusPaint.ps1 -Label After
.\Tools\ValidateMoebiusPaint.ps1 -Label Reload
```

Before 保存原始画面；After 比较关闭绘制、空覆盖量、无贴图时的逐像素一致性，检查笔刷、RGBA 隔离、擦除、Undo/Redo、共享材质、多槽位和 Shader 错误。还在真实 Scene View GUI 中发送 MouseDown / MouseDrag / MouseUp，验证整笔撤销、印章连续覆盖、Shift 擦除与 Alt 导航，并在 Repaint 上执行贴面预览。

Reload 使用另一个全新的 Unity 进程打开已保存场景，检查材质和 PNG 引用、颜色/控制数据、重启前后画面一致性及 Player 程序集排除 Editor 代码。具体 PASS 项目与错误数量见本目录的 `After/Report.txt`、`Reload/Report.txt`。

最终结果：After 39 项通过；Reload 5 项通过。Shader/runtime/test errors 均为 0。Lighting、Hatching、Detail、Outline、Print、SpatialGradient 文件和 ShadowCaster / DepthOnly / DepthNormals Pass 源码一致性检查通过，详见 Integrity.txt。

