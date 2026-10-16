# Ye-Branch → Vincent-Branch

合入来源为本地 `Ye-Branch` 的 `e58cc14898cef2da680bbe892e9b17d082e70401`，目标基线为 `Vincent-Branch` 的 `18b3b64f6052b3a57ce06ec5b7a480c91108415b`。来源分支基于目标基线，合并没有文本冲突。此次保留完整 Ye 分支内容，包括地图、UI、模拟系统与 package 变更，没有只摘取模型。

## 地块整理

火山与雪山的主入口为：

- `Assets/Rendering/Moebius/Samples/Biomes/Prefabs/Volcano_Moebius.prefab`
- `Assets/Rendering/Moebius/Samples/Biomes/Prefabs/SnowMountain_Moebius.prefab`

原始 FBX 移到 `Biomes/Meshes/`；火山烘焙 Mesh 位于 `Biomes/Meshes/Volcano/`；材质分到 `Biomes/Materials/Volcano/`、`Biomes/Materials/SnowMountain/`；贴图位于 `Biomes/Textures/Volcano/`，原始灰度贴图单独放在 `Source/`。

保留两个雪山 Prefab 的不同用途：原 Building 中的已制作版本作为 `SnowMountain_Moebius.prefab`，原 Content 中的工具生成版本改名为 `SnowMountain_Template.prefab`。没有删除可能被引用的模板，也没有重置材质或重新烘焙。

新增两个独立预览场景：`Biomes/Scenes/Volcano_Moebius.unity` 和 `SnowMountain_Moebius.unity`。预览实例居中摆放，原 Prefab 的缩放和源场景摆放不变。

所有移动资源保留 `.meta` 与 GUID。Snow Mountain 的两个 Editor 脚本集中到 `Editor/SnowMountain/`；雪山和火山烘焙工具的路径同步更新，仍写入同一批 GUID 资源。删除了移空的 Building 目录及其中未被引用的孤立文件夹 metadata。共享 Shader/HLSL 继续留在原框架目录，不改渲染逻辑。

## River 保留

合并前对 20 个未提交文件做了临时备份，包括上次迁入的 River。保留用户已经将 `River.fbx` 移到 `Biomes/Meshes/` 的整理。River 的独立地面材质、模型、两张新贴图和原 Prefab 都保留，未使用 Ye 的森林材质覆盖它。

## 验证

使用完整 Assets（约 201 MiB）、Packages 和 ProjectSettings 的隔离 Unity 项目，解析并加载 Ye 新增的 package，完成全部项目脚本编译。

- 雪山、火山及原有 River 的模型、材质、Shader、绘画贴图、火山接触 Mask 与岩浆流场引用均正常。
- 相关 Shader 实际渲染后未发现编译错误。
- 原有 `Assets/Scenes/New Scene.unity`、`Assets/Scenes/WhiteBox.unity` 均能加载；检查到的脚本和 Mesh 引用无缺失。
- 新预览场景实际进入 Play Mode 4 秒，岩浆前后帧发生变化，运行错误数为 0。
- 迁移资源的二进制内容保持一致，全部 Assets 没有重复 GUID，新的雪山/火山依赖链没有缺失引用。

`Report.txt`、`RuntimeReport.txt` 为实际 Unity 输出，`Volcano.png`、`SnowMountain.png`、`LavaEarly.png`、`LavaLater.png` 为实际画面。`MovedAssets.json` 保存旧路径、新路径和原文件 SHA256。没有进行完整 Player 构建或全套地图/生态玩法回归测试。

本次生成本地 merge commit；不推送远端。验证代码仅放在 `Tools/MoebiusValidation/Editor/MoebiusYeMergeValidation.cs`，不会新增主项目 Editor 菜单。
