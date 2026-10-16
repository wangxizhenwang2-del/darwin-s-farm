# 生物与环境 UI 的 MVC 接入 Handout

适用范围：当前 `WhiteBox.unity` 与 V4.3 运行时代码。供把地块、种群测试文字替换成正式 UI 的开发者使用。核查日期：2026-10-10。本文按代码与 Scene 配置编写，未进行 Unity Play Mode 视觉验收。

## 1. 当前已有的 UI

WhiteBox 已挂载 `WhiteboxEcologyDebugController`、`WhiteboxEcologyDebugView` 和 `PopulationVisualPresenter`。运行时测试文字显示模拟日、地块数据及其上方的种群数据；种群文字会优先跟随对应的视觉队列。`WhiteboxBiowebTestSetup` 只负责测试种群播种，不是正式 UI 的数据接口。

测试 View 还包含环境科技的预览、投放、撤销控件代码，但 **WhiteBox 当前没有挂载 `EnvironmentTechnologyController`**，且 View 的该引用为空，因此这组控件在当前场景不会显示。正式 UI 接入时应显式挂载并引用该 Controller，或在正式场景的组装入口完成注入。

## 2. MVC 边界

```text
用户输入 → View（控件/选择状态） → Controller（校验、预览、提交） → Model
  Model → Controller（只读快照/事件/显示 DTO） → View（文本、图表、动画）
```

| 层 | 现有代码 | 应承担的职责 |
| --- | --- | --- |
| Model | `EnvironmentWorld`、`EnvironmentSnapshot`、`BlockInfo`、`PopulationData`、`SpeciesData` | 保存环境、地块和种群的真实数值；生态规则决定增减、迁徙、分化与进化。`SpeciesData` 是物种模板，`PopulationData` 是当前地块的种群实例。 |
| Controller | `SimulationEnvironmentController`、`SimulationController`、`MapSimulationBridge`、`SimulationInterventionController`、`EnvironmentTechnologyController` | 桥接地图与模拟、提供读接口、验证并提交命令、发布完成事件。`WhiteboxEcologyDebugController` 目前还承担测试显示数据组装。 |
| View | `WhiteboxEcologyDebugView`、`PopulationVisualPresenter`、`WhiteboxPopulation` | 显示和动画；只保存选中地块、展开面板等 UI 状态。正式 UI 可以替换测试 View，不应把数值结算规则搬进 View。 |

`BlockInfo` 和 `PopulationData` 目前有许多 `public` 可变字段。**C# 可以直接赋值不代表 UI 应当直接写。** 正式 UI 的编辑动作必须经过 Controller；读取种群时也建议通过新的只读显示 DTO 隔离这些可变对象。

## 3. 显示字段从哪里读

| 显示内容 | 当前可靠来源 | 刷新/语义 |
| --- | --- | --- |
| 模拟日、暂停/倍速 | `SimulationTime.currentDay`、`IsPaused`、`Speed` | `OnDayChanged`、`OnDayReset`；暂停或倍速按钮提交给 `SimulationTime` 的 `Pause`、`Play`、`DoubleSpeed` 或 `TogglePause`。 |
| 地块坐标、地块对象 | `MapGridManager.GetPlacedTiles()`；以 `MapTileInstance.Coordinate` 为 UI 键，`tile.Block` 取生态块 | `TilesChanged` 后重建地块列表；`MapSimulationBridge` 负责把地图变化同步到 Block。 |
| **实时地形**、海拔、温度、湿度、每日植物恢复目标、植物库存/容量 | `SimulationEnvironmentController.TryRead(coordinate, out EnvironmentReadSnapshot)` 或 `ReadAll()`；具体为 `snapshot.Environment.Terrain/Elevation/Temperature.Value/Humidity.Value/Recovery.Value` 及 `snapshot.PlantStock/PlantCapacity` | 订阅 `Changed`，首次打开时主动读取。实时地形应读 `Environment.Terrain`；当前测试标签使用 `tile.Biome`，它是建造预设，环境重分类后可能与实时地形不同。 |
| 水域类型 | `tile.Block.waterCoverage`；水陆判定也可读 `snapshot.Environment.IsWater` | 地块拓扑变化后刷新。`IsWater` 只有水/陆，河、湖、海等细分仍需 `waterCoverage`。 |
| 植物“Δ” | 当前测试 Controller 在 `OnBeforeDaySimulated` 缓存 `BlockInfo.plantBiomass`，在 `OnDaySimulated` 计算前后差 | 这是**当天净库存变化**，包含生长、消耗等影响；若 UI 要“实际自然生长”则读 `BlockInfo.plantGrowthToday`，名称应区别。 |
| 种群名、数量、生态位、K、适温、适湿、体型、运动、繁殖、营养级 | 遍历 `tile.Block.community` 的存活 `PopulationData`；名字优先 `species.SpeciesName`，派生种用 `lineageName`；实例键用 `PopulationId` | `OnDaySimulated` 后刷新，群落编辑后响应 `OnBlockCommunityChanged`。`carryingCapacity` 即当前 K，是模拟结果，不是编辑模板。 |
| 种群“Δ” | 当前测试 Controller 按 `PopulationData` 引用缓存前一天数量，再算差 | 这是**当前实例的净变化**；新建种群会显示 `+当前数量`，迁徙或进化拆分也会影响 Δ。不要把它标成“出生数”；出生/死亡另有 `birthsToday`、`deathsToday`。正式 UI 建议按 `PopulationId` 记录快照。 |
| 当前变异方向、可能进化目标 | `SimulationController.GetExpectedEvolutionDirection(block, population)` 与 `GetLikelyEvolutionTarget(block, population, day)` | 这是规则预测/说明，不是已发生的转化；测试 Controller 每秒重算一次。正式 UI 可在日结后计算并缓存显示结果。 |
| 迁徙、分化、进化过程 | `OnMigrationResolved`、`OnNicheConversionResolved`、`OnPresetEvolutionResolved` 的 `PopulationTransitionResult` | 事件包含源/目标、种群 ID、人数及新建/合并结果。`PopulationVisualPresenter` 已消费它们播放视觉过渡；其他 UI 可据此展示提示，最终人数仍读 Model。 |

当前 `WhiteboxEcologyDebugController.Labels` 是测试用的**已格式化字符串**，`WhiteboxEcologyDebugView` 通过 `OnGUI` 绘制。正式 UI 若需要本地化、图表、单位转换或排序，应让 Controller 提供结构化、只读的 `Day/Tile/Population` 显示 DTO，而不要解析 `Labels.lines`。DTO 可包含地块坐标与 `PopulationId`，让 UI 元素稳定复用。

## 4. 用户操作怎样写

| 操作 | View 提交到 | 规则 |
| --- | --- | --- |
| 环境科技投放 | `EnvironmentTechnologyController.Preview(coordinate, choice)` → 展示 `EnvironmentTechnologyPreview` → `TryDeploy(coordinate, choice, out result)` | Controller 决定是否合法、作用范围、目标数值及持续时间；按钮不直接改温湿度、库存或海拔。撤销用 `CanCancel` / `TryCancel`。当前费用字段为 0，经济扣费尚不是这条 UI 链路的一部分。 |
| 低层环境干预/编辑工具 | `SimulationInterventionController.Preview(request)` → `TryApply(request, out result)`；测试/编辑器单格精确编辑为 `TryApplyEnvironment` | 正常玩家 UI 优先走科技 Controller。水域整域操作由 `SimulationEnvironmentController.TryApplyWater` 等专用路径处理；不可把水格当普通陆地单格改。 |
| 种群测试编辑器 | `SimulationInterventionController.TryApplyCommunity(coordinate, edits, out error)` → `SimulationController.ApplyCommunityEdits` | 这是**测试/编辑器**入口，可编辑种群性状、增加或移除记录；现有实现会保留已有 resident 的数量和身份，不能当作“直接设置现存数量”的 API。若正式产品要购买/投放生物，应增加专门命令与验证，而不是从 UI 改 `speciesAmount`。 |
| 地块放置/高度 | 现有建造流程经 `MapGridManager.TryPlaceTile` 等地图入口；科技改高度仍经环境 Controller | `TilesChanged` → `MapSimulationBridge.Synchronize` → 环境与生态重建。UI 不要直接改 `BlockInfo.elevation`、邻居或 `waterCoverage`。 |
| 时间控制 | `SimulationTime` 的公开播放/暂停/倍速方法 | 不直接修改 `currentDay` 或模拟计时字段。 |

## 5. 刷新和实现约定

1. 打开界面时做一次全量读取；之后订阅 `TilesChanged`、`SimulationEnvironmentController.Changed`、`SimulationController.OnDaySimulated`、`OnBlockCommunityChanged`，按坐标和 `PopulationId` 更新受影响条目；`OnDayReset` 清空 Δ 缓存。`OnEnable` 订阅，`OnDisable` 退订。
2. 环境 `Changed` 可能在拓扑/干预后立即触发，也会在日结后触发。日结中环境先更新、生态后结算；跨环境与种群的整屏统计应在 `OnDaySimulated` 后统一重读，避免混用半日状态。
3. UI 上的“当前值”“目标值”“预计值”分别取实时快照和 `Preview`，不可把预览值提前写进显示的真实状态。提交失败时展示返回的错误；成功后重读快照。
4. 动画与数值显示分离：迁徙等事件已经在模型提交后发出。过渡层可以暂缓目标 View 的人数显示，但不能延迟或回写模型；动画完成或中断后用权威快照校正。
5. 正式 UI 不依赖 `WhiteboxBiowebTestSetup` 或 `OnGUI` 的布局与刷新频率。保留测试 UI 作诊断；将格式化、筛选和快照缓存移入可复用的展示 Controller/Presenter。

## 6. 最小接入示例（伪代码）

```csharp
void OnEnable() {
    grid.TilesChanged += RebuildTiles;
    environment.Changed += RefreshEnvironment;
    simulation.OnDaySimulated += RefreshAfterDay;
    simulation.OnBlockCommunityChanged += RefreshCommunity;
    RefreshAll();
}

void ShowTile(Vector2Int coordinate) {
    if (!environment.TryRead(coordinate, out var env)) return;
    // env.Environment.Terrain / Temperature.Value / Humidity.Value /
    // env.Environment.Recovery.Value / env.PlantStock
    if (!grid.TryGetTile(coordinate, out var tile)) return;
    foreach (var population in tile.Block.community) {
        if (population == null || population.speciesAmount <= 0) continue;
        // 用 population.PopulationId 关联 UI 项；只读并生成显示 DTO。
    }
}

void OnTechnologyClicked(Vector2Int tile, EnvironmentTechnologyChoice choice) {
    var preview = technology.Preview(tile, choice);
    if (!preview.IsValid) { ShowError(preview.Error); return; }
    ShowPreview(preview);
    // 用户确认提交时：technology.TryDeploy(tile, choice, out var result)
}
```

代码入口：`Assets/Scripts/Simulation/WhiteboxEcologyDebugController.cs`、`WhiteboxEcologyDebugView.cs`、`Environment/SimulationEnvironmentController.cs`、`Environment/EnvironmentTechnologyController.cs`、`SimulationInterventionController.cs`、`PopulationVisualPresenter.cs`、`Assets/Scripts/map/MapSimulationBridge.cs`。
