# River 从 YL 迁入 Vincent-Branch

来源：本地 `YL` 分支，提交 `a356e164aca1400a3d82f5f5f0fb7ee7606b25f2`。
目标：本地 `Vincent-Branch` 工作区，基线提交 `18b3b64f6052b3a57ce06ec5b7a480c91108415b`。目标基线与当前已知的 `origin/Vincent-Branch` 一致。

## 使用

`Assets/Rendering/Moebius/Samples/Biomes/Prefabs/River.prefab` 可直接拖入场景。保留 YL 原有的层级、摆放、缩放、模型和配色；没有替换为新的水体效果，也没有改动游戏场景。

## 迁移范围

仅新增 6 个资源及对应的 6 个 `.meta`：

- `Samples/Biomes/Prefabs/River.prefab`
- `Samples/Biomes/Prefabs/River.fbx`
- `Content/Paint/Materials/T_Fprest_Block_Forest_Forest_openPBR_shader1_Paint 1_Paint.mat`
- `Content/Paint/Materials/River_Ground_YL.mat`
- `Content/Paint/Textures/T_T_Fprest_Block_Forest_Forest_openPBR_shader1_Paint 1_Paint_Color 7.png`
- `Content/Paint/Textures/I_Fprest_Block_Forest_Forest_openPBR_shader1_Paint 1_Color.png`

上述路径前缀均为 `Assets/Rendering/Moebius/`。其他依赖，包括 Forest.fbx、灌木和石头材质、已有绘画贴图及 NPR Shader，两分支内容相同，继续复用目标分支资源。复用资源的 `.meta` 也进行了比较。

YL 的 `Fprest_Block_Forest_Forest_openPBR_shader1_Paint 1.mat` 与 Vincent 的同名材质 GUID 相同，但颜色贴图内容不同。因此将 YL 版本复制为独立的 `River_Ground_YL.mat`，分配新 GUID，并只修改迁入 River 资源中的相应引用。Vincent 森林原有材质和配色保持原样。

## 验证

- Unity 6000.2.9f1 / URP 隔离项目中实际加载 Prefab 并从两个方向渲染。
- 检查全部 Mesh、材质、Shader 和启用的 Paint Color 贴图引用；无缺失脚本。
- 确认 River 使用独立的 YL 地面材质，没有引用 Vincent 的冲突材质。
- 使用到的 Shader 编译及验证错误数为 0；见 `Report.txt`。
- 原有 1,250 个 Moebius 文件的 SHA256 未改变。
- 迁入文件无缺失 GUID 引用、无重复 GUID；见 `AssetAudit.json`。
- `Overview.png`、`OtherSide.png` 为实际 Unity 渲染结果。仅为临时验证场景，没有新增或覆盖主项目游戏场景。

`TransferManifest.json` 记录复制路径、原分支提交、GUID 重映射和文件 SHA256。YL 分支提交没有修改。当前新增文件尚未暂存、提交或推送；工作区停留在 `Vincent-Branch`。

验证源代码仅位于 `Tools/MoebiusValidation/Editor/MoebiusRiverTransferValidation.cs`，不会在主 Unity 项目编译，也不新增 Editor 菜单。
