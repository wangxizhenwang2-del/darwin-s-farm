# 生态位系统（已实现规则，尚未接入每日模拟）

`SimulationController` 的 `ecologicalNichesEnabled` 默认为关闭。可在 Inspector 或通过
`SetEcologicalNichesEnabled` 设置。`SimulateDay`、`SimulateBlock` 和现有自动陆地迁徙
目前不会调用生态位转换规则；这些规则通过 `SimulationController.Niches.cs` 的独立入口供后续接线。
开关打开时，旧陆地迁徙会跳过河流与纯水地块，避免把陆生种群当作独立居民放进河道。
种群数值模拟器显示“启用生态位（预留）”开关，但开启后仍只运行原有每日模拟。

## 数据与水域

- `PopulationData.ecologicalNiche` 明确区分陆、海、空；原有 `habitatNiche` 数值继续保留。
- `WaterCoverage` 区分纯陆地、各种河流、湖和海。湖与海是纯水地块；河流地块同时提供河道
  和带归属的河岸，但**不另建一个陆地栖息地**。
- 河流地块的 `riverBanks` 为每段河岸指定方位与 `landOwner`。归属地必须是相邻的纯陆地地块；
  同一方位重复配置会被拒绝。河岸没有独立种群或植物库存，所属陆地地块的资源数值应包含
  这段河岸。河道的藻类库存则填在河流地块上，只算入水域。
- 河流地块的 `waterLinks` 只列真正接通的邻近水道，连接任一端声明即可。相邻湖／海地块
  自动连通；只要涉及河流，就必须有水路声明。连通且同海拔的河道、湖和海组成
  `WaterRegion`。连接、海拔或水面类型变化后须重新调用 `BuildWaterRegions`。
- 水域藻类库存、上限、每日恢复量来自各成员地块，按地块上限计算后累加；
  `WaterRegion.AvailableFood` 是当前可用的生产者食物量。
- 水域温度、湿度均取成员地块算术平均；`CalculateWaterFitness` 只使用平均温度。
  水生种群按整个水域的藻类资源判断食物压力，水域内部不做普通迁徙。
- 空中种群保存在所在 `BlockInfo.community`，所以与地面种群占用同一地块资源；
  到达水面地块时取用该地块藻类。`HasAirPopulation` 保证每地块最多一个存活空中种群。

## 河岸上的陆生种群

以“左陆地—南北向河流—右陆地”为例：把河流 `West` 岸的 `landOwner` 设为左地块，
`East` 岸设为右地块。`TryMoveToRiverBank(左, 河流, West, 种群)` 成功时，只把
`landPositionBlock` 和 `landPositionBank` 改为河流西岸；种群仍在左地块的 `community`，
数量、食物、容量和迁徙冷却均不变，也不触发迁徙事件。`TryReturnFromRiverBank`
可回到所属陆地。`OnLandPositionChanged` 把获准的位置变化通知给未来的 View。

左侧种群不能用同一接口进入东岸；那是跨栖息地迁徙，之后接入模拟时应走迁徙规则。
陆→水转换仅能从有本方河岸的河道进入；水→陆也只能登上有归属的河岸。
纯水地块没有河岸，按相邻陆地处理。当前位置只是模型状态，当前没有动画或寻路实现。

## 分化与迁徙

| 独立入口 | 规则 |
| --- | --- |
| `TryLandDepartureConversion` | 陆地已决定迁出后调用。能从本方河岸或相邻纯水地块入水时先判陆→水；失败后由调用方继续原陆地迁徙。无可进入水域时，若地面容量已满且空中槽位为空，判陆→空。 |
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
