# Tundra 地块导入与配色

## 使用

- 预览场景：`Assets/Rendering/Moebius/Samples/Biomes/Scenes/Tundra_Colored.unity`
- 可复用地块：`Assets/Rendering/Moebius/Samples/Biomes/Prefabs/Tundra_Colored.prefab`
- 材质：`Assets/Rendering/Moebius/Content/Biomes/Tundra/Materials/`
- 地面绘画贴图：`Assets/Rendering/Moebius/Content/Biomes/Tundra/Textures/Tundra_Ground_Color.png`
- 导入模型：`Assets/Art/ImportedTundra/Tundra_Rocks_v2.fbx`

把 Prefab 拖入场景即可使用。它保持原始 17 × 17 米尺寸，底面 Y=0，没有额外缩放。预览场景的相机使用项目已有 Moebius Renderer。

## 配色与实现

参考图片的颜色关系，使用灰紫/暖灰岩石、灰绿色灌木、米金色枯草、乳白残雪、苔绿和浅土色地面，以及浅蓝水塘。模型本身的几何、物件摆放、雪片、裂纹线条和分组保留；没有按照图片重新建模。

Maya 2027 的 mayapy 通过现有 `Tools/ExportHighland.py` 导出静态 FBX，关闭嵌入 Script Node 执行，原始 .ma 不保存修改。Unity 导入不包含摄像机、灯光或动画，因此日常使用 FBX 不需要运行 Maya。

原模型共有 100 个 MeshRenderer、10,626 个三角面、19 个实际使用的材质分区。按原材质名称映射成独立 Moebius 材质，保留岩石各面的不同色块。复用现有 NPR Shader、排线、点描、阴影和 Renderer Feature。原有细线几何保留并关闭其阴影投射，避免细线产生额外微小投影。

原模型地面逐面配色呈现明显三角形拼块。本次只对地面制作了一张 1024² 的苔藓/地衣/浅土色 Albedo：使用低频色块与较细碎的确定性噪声变化，离线投影烘焙到原始 UV0，并填充 UV 边缘。贴图通过现有 `_PaintColorMap` / `_UsePaintColor` 使用，不增加 Shader 功能或运行时纹理计算。地面原来的各材质槽共用这张贴图，能继续用现有 Moebius Paint 工具修改。其他材质仍主要用 Base Color 调色。

水塘使用现有 `Darwin/Moebius Water`，开启低强度水纹和主光源阴影接收。保留浅池模型轮廓，不启用顶点波浪；没有为它生成新的岸线烘焙贴图或瀑布泡沫。

## 验证

在隔离 Unity 6000.2.9f1 / URP 17.2 项目里实际导入、渲染并重启 Unity 重新加载场景。NPR、Water、Outline Shader 的编译错误及测试错误均为 0。检查了源模型完整性、材质槽对应、Prefab 引用和地面原始 UV0 的 Paint 工具兼容性。

`Overview.png` 是实际 Unity 整体截图；`Detail.png` 是近景；`Reloaded.png` 来自重启后重新加载。`Report.txt`、`ReloadReport.txt` 和 `AssetAudit.json` 保存验证结果。验证范围为编辑器实际渲染和资源加载，没有进行独立 Player 构建或目标设备性能测试。

既有地块、场景、Shader、Renderer Feature 和运行时脚本没有修改。本次资产不需要新增运行时控制器。离线构建脚本位于 `Tools/BuildTundra.ps1` 与 `Tools/MoebiusValidation/Editor/MoebiusTundraImport.cs`，不编译进主项目，也不增加 Unity 菜单。

如需从源模型重建，可运行 `Tools/BuildTundra.ps1 -SourceModel <源 .ma 路径>` 在 Temp 中生成并验证；`-Install` 只允许首次安装，遇到已存在的 Tundra 资产会停止，防止覆盖后续手工编辑。
