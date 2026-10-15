# Editor 清理记录

## 本次清理

1. 将 `Assets/Rendering/Moebius/Editor/Waterfall/MoebiusWaterfallSetup.cs` 和原 `.meta` 移到 `Tools/MoebiusValidation/Editor/`。它主要生成示例网格、材质、Prefab 和场景，现有资源运行不需要它。主项目不再编译该脚本，“Create Waterfall in Scene”和“Open Demo Scene”两个菜单入口随之移除。可直接拖入现有瀑布 Prefab、双击现有示例场景。
2. 删除 `Assets/TutorialInfo/Scripts/Editor/ReadmeEditor.cs`、其 `.meta` 和已空的 Editor 文件夹。它是 Unity 模板欢迎页的自定义 Inspector，会自动选择 Readme 和加载模板布局，不参与游戏或 Moebius 效果。Readme 内容资产及其 ScriptableObject 脚本未删除。
3. 删除瀑布材质 Inspector 中重复的“Create Waterfall in Scene”按钮，解除对示例生成脚本的依赖。
4. 更新 `Tools/PrepareMoebiusValidation.ps1`：只在瀑布 CLI 验证需要重建独立示例时加载归档的生成脚本。更新示例和验证工具的 README。

删除/迁移前已检查两个脚本的 GUID：Assets 中没有场景、Prefab、材质等序列化引用。原脚本和 `.meta` 备份保存在此文件夹。Tools 和 Artifacts 均在 Assets 外，不参与主项目编译。

## 保留的 12 个 Moebius Editor 脚本

| 文件 | 保留原因 |
|---|---|
| Inspector/MoebiusWaterShaderGUI.cs | 水材质的自定义 Inspector，Shader 通过 CustomEditor 直接引用。 |
| Inspector/MoebiusWaterfallShaderGUI.cs | 水帘、水花和兼容旧冲击材质的 Inspector；水材质也调用它显示 Impact 参数。 |
| Inspector/MoebiusRampThresholdDrawer.cs | NPR Shader 直接使用的阈值控件，保证 Shadow Threshold 小于 Light Threshold。关闭 Ramp 不会让这个代码引用消失。 |
| Inspector/MoebiusBillboardCameraInitialization.cs | 在未进入 Play Mode 时注册相机参数回调，保证 Scene/Game 视图中的 Billboard 方向和阴影正确。Runtime 初始化方法只在游戏启动时执行，不能替代它的编辑模式初始化。 |
| Water/MoebiusWaterPreview.cs | 编辑模式持续刷新水、瀑布和水花动画；不是一次性测试脚本。 |
| Water/MoebiusWaterSetup.cs | 创建水面、自动检测地面并烘焙/更新岸线贴图的实际工具。 |
| Waterfall/MoebiusWaterfallImpactSetup.cs | 在新场景绑定水帘和接收水面，添加自动跟随组件。运行时自动跟随不能替代初次绑定。 |
| Paint/MoebiusPaintWindow.cs | 绘画工具窗口和参数。 |
| Paint/MoebiusPaintTool.cs | Scene View 笔刷交互。 |
| Paint/MoebiusPaintMesh.cs | 射线命中、网格三角形与绘画 UV 查询。 |
| Paint/MoebiusPaintSession.cs | 绘画会话、笔刷绘制、撤销与结果保存。 |
| Paint/MoebiusPaintAssets.cs | 创建独立绘画材质、保存贴图、指定已有的中性 TransparentPaint 默认资源。 |

当前保留菜单为 `Tools > Moebius > Paint`、`Water Setup`、`Waterfall > Bind Impact to Water`。Renderer Feature、Shader/HLSL、运行时控制器、已有材质、模型、网格、场景及 Prefab 均未因本次清理被修改。

## 验证

独立 Unity 项目使用清理后的生产代码进行编译，未加载 Tools 中的瀑布生成器。检查其类型确实不存在，12 个保留工具类型正常加载，两个示例生成菜单消失，三个实际编辑菜单保留。直接打开并渲染现有瀑布场景，不重新生成资源；具体结果见 `Report.txt`，截图见 `After.png`。

CLI 依赖注入另作文件检查，确认瀑布验证会同时加载验证器和归档生成器，而其他验证不加载生成器。用户当前的 Unity Editor 没有被关闭或操作。
