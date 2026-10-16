# 当前 Unity M 层基线：Task 5

Unity 6000.2.9f1，Windows同设备，随机算法DF-FNV64-1。完整改前来源保存在MODEL_BASELINE_BEFORE.md；没有把Python研究包参数替换为Unity正式值。

当前候选是共享同一S、成年优先、取消重复密度出生惩罚，饥饿按成年支付计算。出生完整支付规则保留。新植食K_food只读；K_eff仅为只读局部诊断。自然变异N/K默认仍用原environmentalK。跨日付款等待Task5D用户确认，原型未并入主工程。

carryingCapacity仍是旧“当日分配/每只成年成本”观测，不能当长期K。foodK为本轮反事实维护上限。捕食者新K_food尚未验收，输出-1。

实际运行源：runs/formal-T1-chicken50-run1.jsonl第2/3行，来自Unity ReadLandRules/ReadTuning。下表九位有效数字可往返单精度值，原始JSONL保留完整展开。测试结果见POPULATION_TEST_REPORT.md、REGRESSION_REPORT.md，三次验收见DETERMINISM_VALIDATION.json。

## LandRuleTuning

| 字段 | 实际Unity值 | 最近修改 | 来源 | 冻结或候选 | 对应测试 | 修改原因 |
| --- | --- | --- | --- | --- | --- | --- |
| ruleVersion | 5 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| useFoodCapacityForSelection | false | 新增开关 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 新增候选开关，默认false；A/B单独输入 |
| birthRate | 0.100000001 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| starvationDeathRate | 0.600000024 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| coldDeathRate | 0.00499999989 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| climateMismatchDeathRate | 0.00999999978 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| reproductionStressPerBirth | 1.20000005 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| baselineAdultDeathRate | 0.00800000038 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| smallBodyFrailtyRate | 0.5 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| adultSizeMortalityScale | 20 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| forageAccessLossAtZero | 0.0799999982 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| forageSizeScale | 20 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| forageMovementScale | 10 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| movementFoodCost | 0.400000006 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| smallBodyMaintenance | 200 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| youngLinearCost | 0.400000006 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| youngQuadraticCost | 0.0199999996 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| competitionPressure | 2 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 捕食竞争沿用；植物由W=A×U共同分配 |
| oppositeStrategyCompetition | 0.5 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 捕食竞争沿用 |
| smallPlantNicheShare | 0.699999988 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 改为可及性混合权重，数值未改 |
| unspecializedPlantEfficiency | 0.699999988 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 跨偏好层获取权重 |
| smallPlantSizeScale | 20 | 数值未改 | LandRuleTuning/LandLifeRules | 本阶段冻结/正式条件 | T1–T5/旧路线 | 沿用改前Unity值 |
| largePlantSizeScale | 25 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| movementForagingGain | 0.00200000009 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| minimumPlantReserveFraction | 0.100000001 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| dailyPlantStockDrawFraction | 0.100000001 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| densityBirthDampingStart | 0.850000024 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 历史序列化值保留，人口结算已不再调用 |
| baseCaptureChance | 0.5 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| chaseScale | 40 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| preyEnergyPerSize | 40 | 数值未改 | LandRuleTuning/LandLifeRules | 本阶段冻结/正式条件 | T1–T5/旧路线 | 沿用改前Unity值 |
| reserveHuntFraction | 0.100000001 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| reserveDays | 10 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| splitMinimumFoodSatisfaction | 0.949999988 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| splitMinimumRenewalPressure | 0.899999976 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| splitMaximumRenewalLoad | 1.20000005 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| splitHeadroomUse | 0.5 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| splitFounderFraction | 0.0500000007 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| directedSplitTraitFraction | 1 | 数值未改 | LandRuleTuning/LandLifeRules | 本阶段冻结/正式条件 | T1–T5/旧路线 | 沿用改前Unity值 |
| directedSplitTraitLockDays | 50 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| splitStockDays | 3 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| splitCooldownDays | 300 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| sameSpeciesMergeDays | 100 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| postMergeSplitCooldownDays | 200 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| evolutionTodaySatisfaction | 0.899999976 | 数值未改 | LandRuleTuning/LandLifeRules | 本阶段冻结/正式条件 | T1–T5/旧路线 | 沿用改前Unity值 |
| evolutionRenewalSatisfaction | 0.800000012 | 数值未改 | LandRuleTuning/LandLifeRules | 本阶段冻结/正式条件 | T1–T5/旧路线 | 沿用改前Unity值 |
| evolutionPreyRenewalCoverage | 0.800000012 | 数值未改 | LandRuleTuning/LandLifeRules | 本阶段冻结/正式条件 | T1–T5/旧路线 | 沿用改前Unity值 |
| evolutionRequiredDays | 5 | 数值未改 | LandRuleTuning/LandLifeRules | 本阶段冻结/正式条件 | T1–T5/旧路线 | 沿用改前Unity值 |
| evolutionRequiredDaysAtFour | 10 | 数值未改 | LandRuleTuning/LandLifeRules | 本阶段冻结/正式条件 | T1–T5/旧路线 | 沿用改前Unity值 |
| evolutionTraitTolerance | 5 | 数值未改 | LandRuleTuning/LandLifeRules | 本阶段冻结/正式条件 | T1–T5/旧路线 | 沿用改前Unity值 |
| evolutionCooldownDays | 50 | 数值未改 | LandRuleTuning/LandLifeRules | 本阶段冻结/正式条件 | T1–T5/旧路线 | 沿用改前Unity值 |
| neighborAttractionThreshold | 0.200000003 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| attractionMovementGain | 2 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| localMovementGain | 1 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| resourceTraitGain | 0.00300000003 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| lowStockDays | 3 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| highStockDays | 5 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| lowRenewalCoverage | 0.899999976 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| highRenewalCoverage | 1.10000002 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| flatMigrationThreshold | 30 | 数值未改 | LandRuleTuning/LandLifeRules | 本阶段冻结/正式条件 | T1–T5/旧路线 | 沿用改前Unity值 |
| slopeMigrationThreshold | 60 | 数值未改 | LandRuleTuning/LandLifeRules | 本阶段冻结/正式条件 | T1–T5/旧路线 | 沿用改前Unity值 |
| flatAttractionCeiling | 40 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| slopeAttractionCeiling | 70 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| fourEntryMigrationFactor | 0.5 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| mutationGainThreshold | 1.99999995e-05 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |
| mutationGainDailyScale | 0.000199999995 | 数值未改 | LandRuleTuning/LandLifeRules | 候选 | T1–T5/旧路线 | 沿用改前Unity值 |

## SimulationTuning

| 字段 | 实际Unity值 | 最近修改 | 来源 | 冻结或候选 | 对应测试 | 修改原因 |
| --- | --- | --- | --- | --- | --- | --- |
| reproductionScale | 0.100000001 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| overCapacityDeathRate | 0 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| starvationDeathRate | 0.600000024 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| levelOnePreyFraction | 0.0799999982 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| levelTwoPreyFraction | 0.100000001 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| mutationEnabled | false | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| migrationEnabled | false | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| ecologicalNichesEnabled | true | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| mutationInterval | 1 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| migrationInterval | 7 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| migrationCooldownDays | 7 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| sameSpeciesMergeDays | 100 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| postMergeSplitCooldownDays | 200 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| directedSplitTraitLockDays | 50 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| splitMinimumRenewalPressure | 0.899999976 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| splitFounderFraction | 0.0500000007 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| directedSplitTraitFraction | 1 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| resourceTraitGain | 0.00300000003 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| localMovementGain | 1 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| lowStockDays | 3 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| highStockDays | 5 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| lowRenewalCoverage | 0.899999976 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| highRenewalCoverage | 1.10000002 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| mutationStep | 1 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| selectionStrength | 1 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| migrationPopulationScale | 100 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| overloadThreshold | 0.800000012 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| baseOverloadProbability | 0.5 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |
| overloadMigrationFraction | 0.200000003 | 沿用 | SimulationController.ReadTuning/场景 | 原基线 | 回归 | 未为本轮改数值 |

## 公式和工具

成年能耗=(size+200/max(1,size))×(1+0.4×(movement/100)^2)；幼体能耗=0.4×size+0.02×size²；出生身体=max(1,size)×40。仍须完整支付幼体+身体。正式分化仍10%，五Bar容差5，通常连续5日，四群时10日；冷却与物质补足未改。新资源算法见DYNAMIC_K_FORMULA.md。

玩家温湿度、S/R、取消恢复、海拔和地块工具均未修改；明细见MODEL_BASELINE_BEFORE.md。正常路线仍从荒漠55/35/R50000开始，1000鸡的额外身体由初始S真实支付。机制夹具带mechanismFixture标识，第0日动物身体属于显式初始条件，不可声称是合法玩家进化路线。

固定环境夹具通过既有环境M接口只维持T/H/R，每次保留当前S；没有每日补库存。机制隔离夹具显式关闭变异/迁徙；旧路线保持正式默认开启。各次变化与回退见PARAMETER_CHANGELOG.md。

完整世界检查点恢复仍BLOCKED，缓存命中0，节省模拟日数0。只复用了输入和已有证据；只读单日复制不是恢复缓存。
