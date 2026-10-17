# Unity 运行报错修复（2026-10-17）

## V6.1 source graph hash mismatch

`Ye-Branch` 工作区的 `Assets/Resources/V61Bioweb13.json` 与已验证原图的解析结果相同，但原文件使用 CRLF，SHA-256 为 `0d09bef274b988adab33452b4e336cd1fdcebbcd5da4b87a074f4230f99932a7`。V6.1 校验的是**原始字节**，规范原图使用 LF，SHA-256 为 `763fdd910f882df696688cf96a794155daa468cc34543a6077cd120df65c3dd5`。旧文件已备份到 `C:\Users\17799\Downloads\V61_Graph_Byte_Backup_20261017`，当前工作区已复制精确原始字节；`.gitattributes` 的 `-text` 规则阻止 Git 以后转换此文件换行。

## 材质 Enum drawer

四个 Moebius 地表 Shader 的 `_DebugMode` 属性使用了 13 组内联名称/数值，超过 Unity 6 内置 `[Enum]` drawer 支持的 7 组上限。在 `new Material(source)` 和替换 Shader 时触发相同报错。四处已改用同一个编辑器枚举 `MoebiusSurfaceDebugView`，原有 0–12 数值、Shader 分支、材质参数、场景和 Prefab 均未变化。原 Shader 已备份到 `C:\Users\17799\Downloads\V61_Shader_Drawer_Backup_20261017`。

## 验证

- 在独立工作树使用 Unity 6000.2.9f1 重新导入并运行 `EnvironmentFiveToolsAudit.Run`，退出码 0，`PASS: Unity five-tools audit`。`Validation/unity-shader-graph-fix.log` 中没有材质 Enum drawer、V6.1 图哈希或 C# 编译错误。
- 当前 `Ye-Branch` 的生物网文件字节哈希已与规范一致。其 Unity 编辑器在文件替换时处于打开状态；须让编辑器完成 Asset Refresh 后重新进入场景，才可核对当前会话的 Console。隔离工作树实测不等于声称当前会话已完成刷新。
- `OnLocalSplitResolved` 的 CS0067 是保留旧事件接口的未使用警告，不是编译错误；不为消除警告而删除兼容入口。

V6.1 核心分支仍未合入本地 `Ye-Branch` 或 `main`；上述对当前 `Ye-Branch` 的局部修复也尚未提交。合并前先保存主工作区原有未提交内容。
