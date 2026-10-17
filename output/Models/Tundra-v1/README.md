# 苔原地块简模 v1

基于 2026-10-15 的苔原概念图，在 Maya 2027 中生成的可编辑低多边形基础模型。

- `Tundra_Blockout_v1.ma`：Maya 原生场景，包含模型、基础材质、预览相机和灯光。
- `Tundra_Blockout_v1.fbx`：模型与基础材质导出文件。
- `Tundra_Preview.png`：该模型的 Maya Software 实际渲染预览。

尺寸：17 × 17 米，Y 轴朝上，地块底部中心位于原点，最高约 3.20 米。87 个网格，共 7,483 个三角面。

模型分为 Ground、Rocks、Snow、Shrubs、Grass、Water、Ink 七组。岩石、雪块、灌木和草丛可独立选择和调整。Ink 组是可隐藏的简化几何墨线与排线。预览相机和灯光位于独立的 Preview_Setup_GRP，未导出到 FBX。

已具备基础配色和自动展开的 UV，不依赖外部贴图。当前交付定位为造型与布局简模；精细手绘材质和 Unity 中的 Moebius 渲染接入可在此基础上继续完成。

验证：重新打开 Maya 场景并重新导入 FBX，尺寸、87 个网格和 7,483 个三角面一致；未发现非流形边或缺少 UV 的网格。详细结果见 `reopen-validation.json`。

建模参考：`../../概念设计/2026-10-15/苔原地块-v1.png`。
