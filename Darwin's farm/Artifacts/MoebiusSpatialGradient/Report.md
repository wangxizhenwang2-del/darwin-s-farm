# 双空间控制点 Moebius 颜色渐变

## 使用

打开 `Assets/Scenes/MoebiusComicDemo.unity`，选择根对象 **Moebius Spatial Gradient Test**。

- Color A / Color B：直接选择两端颜色，默认橙珊瑚色 → 淡紫色。
- Point A / Point B：两个可在场景中移动的子对象；连接方向定义渐变轴，两点间距定义范围。可横向、纵向或斜向摆放，不依赖灯光。
- Transition Width：占两点距离的比例，以中点为中心。1 为整段过渡；0.3 为中间 30% 过渡，两端保持大块稳定色面。
- Strength：1 使用统一渐变底色；0 恢复各材质原底色。低于 1 时仍保留原材质色差，因此不能保证接缝完全连续。
- Targets：加入同一渐变的 Renderer。不同 Mesh、不同 Material、同一 Material 的不同物体均可加入。只有使用 Moebius NPR Shader 的材质槽生效。

测试使用墙顶相邻的两块现有砖。左侧保留 Sandstone 材质；右侧使用新增的 SpatialGradientCoolTest 材质副本，底色不同，笔触等参数相同，便于看清颜色连续性。未修改原 Sandstone / Ivory 等材质资产。两个砖块和控制点放在测试组下，可以一起移动。

组上的 `MoebiusSpatialColorGradient` 是运行时组件，并在编辑器中预览；它并非验证脚本。运行时移动控制点会更新渐变。取消勾选组件会还原绑定前的 PropertyBlock。

不使用组件也可以直接填写材质的 Spatial Color Gradient 参数，但跨材质时必须填写相同的控制点、颜色和范围；组件负责让这些值同步。

## 工作方式与边界

```text
当前表面世界位置
        ↓
投影到 Point A → Point B 轴，得到 0～1 的位置比例
        ↓
smoothstep（由 Transition Width 控制过渡区间）
        ↓
Color A ↔ Color B
        ↓
与 Base Map 相乘，再按 Strength 混入原底色
        ↓
原 Paper Lift / NPR 明暗 / 排线 / 点描 / 高光 / 相机描边
```

这里不是把渐变色乘到各材质自身 Base Color 上；那样会留下材质色差。Strength = 1 时，用共享渐变色作为 Base Color，仍保留 Base Map 的纹理图案。默认关闭，不参与的对象保留原效果。空间渐变启用且强度大于 0 时，绕过旧 Lambert Color Ramp，避免光照再次决定渐变色偏。

渐变配色与灯光方向无关，但原有明暗、阴影、排线和高光仍响应光照。接缝颜色连续不意味着删除砖缝：几何间隙和墨线仍保留。两侧不同的 Base Map、Paper Lift、阴影参数或其他材质设置也可能保留视觉差异。

这是选定对象共享的空间颜色场，不是自动扫描所有邻居的接触混色。需要过渡的对象应加入同一 Targets 组，控制点过渡段放在接缝附近；没有改动物体几何，也不会模糊空隙。

控制点跟随父对象，因此移动模型组时渐变可一起移动。若只移动模型、不移动控制点，模型会穿过固定在世界中的颜色场。两个控制点重合时安全退回原底色。

组件使用每材质槽的 MaterialPropertyBlock，不克隆运行时材质、不修改共享材质资产。组件管理绑定槽的 PropertyBlock，在绑定时保留原值、禁用时还原；不要让多个组件或其他脚本同时管理相同材质槽的 PropertyBlock。修改 Targets 或材质槽后，脚本应调用 RefreshTargets。PropertyBlock 会影响这些对象的 SRP Batcher 使用资格，因此只对需要共享渐变的对象使用；本次没有做帧率基准。

## 修改文件

- Shaders/MoebiusNPR.shader：新增空间渐变参数，读取已有 Base Map 后调用新函数；没有新增纹理采样。
- Shaders/MoebiusNPRInput.hlsl：增加共享材质参数，所有 Pass 继续使用一致 CBUFFER。
- Shaders/MoebiusSpatialGradient.hlsl：新增投影、smoothstep、双色混合；关闭/零强度直接返回原色。
- Shaders/MoebiusNPRLighting.hlsl：空间渐变优先于旧 Lambert 颜色 Ramp，原 NPR 明暗计算不变。
- Runtime/MoebiusSpatialColorGradient.cs：两个场景控制点、颜色和目标列表的同步与运行时更新，选中时显示控制点 Gizmos。
- Demo/SpatialGradientCoolTest.mat：专用于右侧砖块的测试材质副本。
- Assets/Scenes/MoebiusComicDemo.unity：加入测试组，将两个原砖块与控制点放入该组，为右侧指定测试材质。
- BiomeTests/Editor/MoebiusSpatialGradientValidation.cs、Tools/ValidateMoebiusSpatialGradient.ps1：隔离验证，不向游戏增加渲染 Pass。验证场景基于 SourceBefore 中保留的修改前场景，重复运行不会重建实际项目场景。

## 验证

Unity 6000.2.9f1 / URP 17.2 当前桌面环境，详见 After/Report.txt。

- 未启用空间渐变的 Comic Demo 与修改前逐像素相同。
- 不同材质测试中，Strength = 0、禁用组件与修改前逐像素相同。
- 独立底色测试改变光源方向，0 个变化像素；渐变配色不依赖光照。
- 不同材质接缝的底色连续；移动控制点改变渐变方向和范围。
- 整组与相机平移后底色图像最大通道差为 0/255，渐变可跟随模型组。
- 现有法线、NdotL、明暗、排线、点描、高光和投影调试遮罩逐像素相同。
- 控制点重合安全渲染，Shader / 运行 / 测试错误为 0。

截图所用 RenderTexture 仅存在于 Editor 验证代码中。游戏运行路径没有新增 RenderTexture、纹理采样、屏幕空间处理、Renderer Feature 或 Render Pass。ShadowCaster / DepthOnly / DepthNormals、排线、点描、相机描边代码均未重写。
