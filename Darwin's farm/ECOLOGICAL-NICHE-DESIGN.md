# 生态位系统（已实现规则，尚未接入每日模拟）

`SimulationController` 的 `ecologicalNichesEnabled` 默认为关闭。可在 Inspector 或通过
`SetEcologicalNichesEnabled` 设置。`SimulateDay`、`SimulateBlock` 和现有自动陆地迁徙
目前不会调用生态位规则；这些规则通过 `SimulationController.Niches.cs` 的独立入口供后续接线。
种群数值模拟器显示“启用生态位（预留）”开关，但开启后仍只运行原有每日模拟。

## 数据与水域

- `PopulationData.ecologicalNiche` 明确区分陆、海、空；原有 `habitatNiche` 数值继续保留。
- `BlockInfo.waterCoverage != Land` 表示含水。相邻、含水、同海拔的地块组成一个
  `WaterRegion`。若水域连接关系、海拔或含水状态改变，须重新调用 `BuildWaterRegions`。
- 水域藻类库存、上限、每日恢复量来自各成员地块，按地块上限计算后累加；
  `WaterRegion.AvailableFood` 是当前可用的生产者食物量。
- 水域温度、湿度均取成员地块算术平均；`CalculateWaterFitness` 只使用平均温度。
  水生种群按整个水域的藻类资源判断食物压力，水域内部不做普通迁徙。
- 空中种群保存在所在 `BlockInfo.community`，所以与地面种群占用同一地块资源；
  到达水面地块时取用该地块藻类。`HasAirPopulation` 保证每地块最多一个存活空中种群。

## 分化与迁徙

| 独立入口 | 规则 |
| --- | --- |
| `TryLandDepartureConversion` | 陆地已决定迁出后调用。有相邻水域时先判陆→海；失败后由调用方继续原陆地迁徙。无相邻水域时，若地面容量已满且空中槽位为空，判陆→空。 |
| `MoveWithinWaterRegion` | 同水域仅作位置调整，一次搬走整个种群。 |
| `TryWaterMigration` | 相邻的不同水域，只允许海拔差 ±1。向低处概率较高、向高处概率极低；概率乘来源水域的食物压力。普通迁徙保持物种身份，默认按现有迁徙比例分出一部分。 |
| `TryWaterToLandConversion` | 当前水域食物满载且邻近陆地时，极小概率海→陆。 |
| `TryAirMigration` | 空中槽位为空时，用陆地式适应度、食物空间与超载概率判定，忽略海拔差，普通迁徙保持物种身份。 |

所有生态位转化都按 `RoundToInt(原种群数量 × 0.1)` 拆分，结果必须 **大于 10**；
该数量不受普通迁徙的 100 只上限限制。子代生成唯一 `speciesId`、独立种群和
对应生态位，原物种继续留在原位。分化后的物种身份先用运行时 ID 表示，
尚未生成持久化 `SpeciesData` 资产。普通迁徙会继承该 ID，不产生新物种。

默认概率为陆→海 2%、陆→空 1%、海→低海拔水域 5%、海→高海拔水域 0.2%、
海→陆 0.1%。可通过 `SetEcologicalNicheProbabilities` 调整。跨水域普通迁徙数量
沿用现有 `overloadMigrationFraction` 和 `LimitMigrationAmount` 的整数下限与上限。
概率属于首次可调默认值，后续接入完整食物链模拟时再做数值平衡。

独立核对命令：`dotnet run --project Tools/NicheAudit/NicheAudit.csproj`。
