# Moebius 脚本整理

这次整理限定在 Moebius 渲染系统及其 CLI 验证脚本；没有改动农场、地图、移动控制等游戏逻辑。

## 日常使用

- `Assets/Rendering/Moebius/Runtime/`：保留描边 Renderer Feature 和空间渐变组件。
- `Assets/Rendering/Moebius/Editor/Inspector/`：保留并集中放置材质阈值 Drawer。
- `Assets/Rendering/Moebius/Editor/Paint/`：只保留通用绘画工具及其笔刷 Shader、使用说明。
- `Assets/Rendering/Moebius/Billboard/MoebiusBillboardTestOrbit.cs`：仍放在对应示例旁边，属于示例相机控制。

## 可选验证代码

以下 6 个脚本移到了 `Tools/MoebiusValidation/Editor/`，主项目不再编译它们：

| 文件 | 作用 |
| --- | --- |
| MoebiusBiomePreviewBuilder.cs | 导入模型并生成群系测试资源 |
| MoebiusColorRampValidation.cs | 验证 Lambert 双色 Ramp |
| MoebiusHandDrawnValidation.cs | 验证手绘风格渲染 |
| MoebiusSpatialGradientValidation.cs | 验证空间控制点渐变 |
| MoebiusPaintValidation.cs | 验证通用绘画工具 |
| HighlandPaintValidation.cs | 准备和验证 Highland 测试资源 |

这些脚本不影响现有模型显示或日常绘画。它们仍可恢复、仍保留 `.meta`。对应 CLI 命令现在通过 `Tools/PrepareMoebiusValidation.ps1`，只在临时 Unity 项目中载入所需的单个验证脚本。

截图中的 `BiomeTests/Editor` 已移除。里面的 `MoebiusRampThresholdDrawer.cs` 没有删除，而是移到 `Editor/Inspector`；Shader 的两个 `[MoebiusRampThreshold]` 属性仍使用它维护阈值顺序。

## 完整性

7 个移动的 C# 文件内容哈希和原 GUID 均保持一致。删除的只是已空的旧目录，其文件夹 `.meta` 也保存在归档位置。Shader、HLSL、Runtime 代码、场景、材质、模型和已绘制 PNG 没有修改。

详细移动记录见 `Moves.json`。实际编译/球体绘画验证结果见同目录的 `Sphere/Report.txt`。

整理后的 Moebius 代码在独立 Unity 项目中编译成功；球体绘画的 14 项检查全部通过，Shader/runtime/test 错误为 0。临时项目准备脚本也验证了只载入选中的单个验证类，并保留迁移后的 Inspector Drawer。

原来的 `ValidateMoebius.ps1` 和 `ValidateMoebiusBillboard.ps1` 引用的旧入口类在当前源目录中已经不存在；本次不恢复这些旧原型。其它历史验证可能需要已移除的测试场景或素材，详见系统 README。
