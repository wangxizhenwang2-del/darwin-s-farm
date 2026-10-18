# WhiteBox 森林美术首版

当前已扩展至八类陆地，完整说明见 [BiomeArtwork.md](BiomeArtwork.md)。本页保留森林首版的设计与回退说明。

WhiteBox 的 `MapSystem > MapTileColorTransitionSystem > Forest Art Pilot > Preserve Forest Artwork`
已开启。关闭该选项后重新进入 Play，即可恢复旧版森林外观。

## 本版范围

- 仅森林：复用技美原地面材质的运行时副本，并从原模型投射绘制 UV，恢复贴图、排线和纸面质感。
- 保留森林树木的原始平面位置，不再为了十字通道移动大树或疏除小树。
- 保留现有地形起伏、地块碰撞、地块尺寸和寻路接口。小树作装饰，大树树干和大石保留避障。
- 原模型覆盖范围内的美术向边缘渐隐；边框、悬崖继续沿用原有拼接逻辑，其他生态类型保留原有底色。
- 森林边缘底色按原地面绘制贴图的平均色校准，避免旧版亮草绿与偏灰褐的绘制地面形成色圈。
  相邻地块也使用同一森林底色计算混合，保持接缝两侧颜色一致；可在 Forest Art Pilot 中调整 Forest Ground Blend Color。
- 美术强度与现有生态转换动画同步进入和退出，支持连续打断和停用清理。
- 建造预览使用相同的植被布局，并保持原有的有效/无效颜色与无碰撞行为。

本版未替换完整原始地形网格，也未接入独立的风沙、落叶等 VFX。其他生态类型仍沿用原来的实现。
技美的共享材质、贴图和 Prefab 不会被运行时逻辑修改；共享 NPR Shader 的新分支默认关闭，只对森林运行时材质副本启用。

## 验证

在一个新的 WhiteBox Play 会话运行 `ForestArtworkChecks.Run()`，结果见 `ForestArtworkChecks.Result`。
该检查会改变临时运行地图，验证美术 UV、源材质隔离、树木原始位置、回退时的地面几何与碰撞一致性、
四种旋转、不同地形邻接、高差接缝及建造预览。退出 Play 后恢复场景。

现有的 `BiomeTransitionChecks.Run()` 和 `PopulationTerrainChangeChecks.Run()` 应分别在新的 Play 会话运行，
用于验证生态过渡、连续打断、地形升降和障碍变化后的移动恢复。
