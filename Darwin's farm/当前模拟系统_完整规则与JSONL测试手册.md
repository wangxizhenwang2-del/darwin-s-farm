# 《达尔文农场》当前模拟系统：完整规则与 JSONL 测试手册

适用范围：本工程当前 `Ye-Branch` 的陆地白盒模拟器、环境干预、正式物种演化、玩家操作及其可选模块。请把**此文档、完整 `.jsonl` 和运行该日志的工程版本**一起交给测试者。本文描述现行源码；实际运行参数以日志 `header.landRules`、`snapshot.tuning`、场景序列化设置和对应 `buildHash` 为准。数值若经后续调参变化，以那次日志和代码为准。

> 本文能指导整套系统测试和首次异常定位。当前 schema 4 日志没有导出全部内部开关、每个被拒候选及完整物质账本，因此单靠 JSONL 不能严格证明每条内部守恒式，或判定 V6.2 已开启；需要同版本 Unity 重放或专项账本测试补证。

## 1. 开局、时间和每日执行顺序

- 白盒设置加载当前地图与完整 `bioweb(13)` 关系；资源中有 15 种物种、20 条演化连接、25 条食物关系，不能因某测试只出现少数物种而删图。初始地块、数量、性状和种子由场景/设置决定，**不要用此文档的示例数值代替初始快照**。
- 默认时钟 1 秒/天，可调 1、2、4 倍速、暂停或手动下一天。速度影响等待时间，不应改变同一输入的日结果；操作在一天内的顺序影响结果。
- 每日顺序：环境进入新日并推进技术效果 → 各地块的植物、捕食、食物分配、生死及变异 → 连续濒危清除 → 正式物种演化 → 到期自然迁徙 → 物种状态与统计刷新 → 生态账本收尾 → 日终快照。水地的陆地生命循环会跳过；天空/海洋需各自开关。`snapshot.reason=day_end` 是这一整轮后的状态。
- 默认陆地变异间隔 1 天，自然迁徙间隔 7 天、迁徙冷却 7 天；这些都是可调参数。日数从 0 开始，迁徙只在 `day>0` 且整除间隔时检查。暂停或停止 Play 不会额外模拟一天。

## 2. 地图、环境与五种干预

- 陆地高度为 0–2，温度/湿度为 0–100，恢复量为 0–150000。陆地单格植物库存上限 100 万；水格 150 万。预设地貌决定初值。`snapshot.tiles[]` 包含坐标、高度、水域、温湿度、恢复量、植物库存、增长与消耗、技术阶段及目标。
- 每天先按恢复量增长库存，最多到上限。陆生动物可取的当日植物预算并非全部库存：正常恢复时先保留底线，再取当日增长及限定比例的旧库存；默认保留比例 10%、旧库存日提取比例 10%。零恢复时按现行预算路径处理。植物摄食只从预算和实际库存支出，不能凭预测增加植物。
- 温度/湿度科技每次 ±25 或 ±50：25 天进入、150 天维持、100 天返回。陆地恢复量每次 ±25000 或 ±50000，同为 25/150/100 天；水域恢复量即时变化、维持 50 天、返回 50 天。植物库存 ±25% 或 ±50% 立即生效；高度 ±1 立即生效。数值被合法区间/容量夹住。水域科技作用于连通水体；陆地环境工具通常作用于单格。
- 改环境后应对比操作前后快照及之后的 `phase`/剩余天数、`temp/humidity/recovery/plants`。生物能收费默认关闭；若场景开启收费，价格取 `Assets/Resources/BioenergySettings.asset`，拒绝操作不得扣钱。

## 3. 食物网、能量与出生死亡

- 食物关系必须同时满足**相同生态位**、营养级方向（1 吃 0；2 吃 0/1）和 `SpeciesData.preySpecies` 明确连线。捕食按高级到低级处理；猎物损失进入 `predationDeathsToday`，也是该日总死亡的一部分，不能重复相加。默认不启用动态猎物再生限额。
- 植食者按小/大植物生态位及竞争策略分配植物预算，移动能力影响觅食可及性，同时增加成年维持成本。捕食者使用实际获得的猎物能量。先支付成年需求，再用剩余能量支付幼体成本和体物质；余量进入有上限的储备。`allocatedFood` 是分配/获得量，`energyNeed` 是需求，`foodSatisfaction` 是满足程度；不能把理论容量当作已经吃到的食物。
- 当前陆地默认公式（所有系数可由 `landRules` 覆盖）：气候适应度 `f=clamp01(1-(|T-fitT|+|H-fitH|)/90)`；每只成年日需求 `A=(s+200/max(1,s))×(1+0.4×(m/100)^2)`，其中 `s` 是体型、`m` 是运动能力；幼体需求 `Y=0.4s+0.02s²`，形成体物质另需 `max(1,s)×preyEnergyPerSize`。必须看日志内该项参数，不能假定资源资产与代码默认相同。
- 潜在出生由数量、默认 0.1 的出生率、繁殖性状、气候适应及食物满足程度共同决定；实际出生还受成年消费后的剩余能量和幼体物质支付约束，并积累小数余量。死亡含饥饿（默认 `0.6×N×(1-satisfaction)`）、寒冷（`0.005×N×max(0,(50-T)/50)`）、气候失配（`0.01×N×(1-f)`）、自然成年死亡及可选出生压力；小数余量累积后落到整数。`births/deaths` 是实际当日整数，`deaths` 已包括捕食死亡。
- `k` 表示按本次分配食物估算的可支持规模；`environmentalK` 是可再生食物理论规模，不能直接解读为稳定种群目标。`foodAverage10`、`stockDays`、`renewalCoverage` 和三种性状压力帮助分辨资源问题与演化压力。连续 10 天低于 10 只的陆地群体会本地灭绝，发出 `ecology.kind=local_extinction`，遗体进入碎屑路径。
- 同种群体的旧“本地分裂”路径已停用；正式演化产生的是图谱中的新物种。不要把日志里没有本地分裂判成故障。

### 陆地参数索引（代码初始值；每次以日志为准）

| 规则组 | `LandRuleTuning` 初始值 | 测试意义 |
| --- | --- | --- |
| 摄食与维护 | `adultMaintenanceScale=1`、`forageAccessLossAtZero=.08`、`forageSizeScale=20`、`forageMovementScale=10`、`movementForagingGain=.002`、`movementFoodCost=.4`、`smallBodyMaintenance=200` | 运动和体型同时影响取食与消耗 |
| 植物竞争 | `competitionPressure=2`、`oppositeStrategyCompetition=.5`、`smallPlantNicheShare=.7`、`unspecializedPlantEfficiency=.7`、`smallPlantSizeScale=20`、`largePlantSizeScale=25`、`minimumPlantReserveFraction=.1`、`dailyPlantStockDrawFraction=.1` | 多群体竞争时的实际可分配植物 |
| 出生与死亡 | `birthRate=.1`、`youngLinearCost=.4`、`youngQuadraticCost=.02`、`densityBirthDampingStart=.85`、`starvationDeathRate=.6`、`coldDeathRate=.005`、`climateMismatchDeathRate=.01`、`baselineAdultDeathRate=.008`、`smallBodyFrailtyRate=.5`、`adultSizeMortalityScale=20`、`reproductionStressPerBirth=1.2` | 优先比较日志中实际出生/死亡与资源、气候趋势 |
| 捕食与储备 | `baseCaptureChance=.5`、`chaseScale=40`、`preyEnergyPerSize=40`、`reserveHuntFraction=.1`、`reserveDays=10` | 实际食物、捕食死亡和能量储备；资产可覆写值 |
| 正式演化 | `evolutionTodaySatisfaction=.9`、`evolutionRenewalSatisfaction=.8`、`evolutionPreyRenewalCoverage=.8`、`evolutionRequiredDays=5`、`evolutionRequiredDaysAtFour=10`、`evolutionTraitTolerance=5`、`evolutionCooldownDays=50` | 连续合格而非瞬时达标 |
| 迁徙/变异 | `flatMigrationThreshold=30`、`slopeMigrationThreshold=60`、`fourEntryMigrationFactor=.5`、`mutationGainThreshold=.00002`、`mutationGainDailyScale=.0002` | 资格和候选强度，不保证每次抽中 |

`enablePreyDensityFeedback`、`enablePaidBirthStress`、`enablePriorityPlantBudgets`、`useCompensatedEvolutionFoodGate`、`enableDynamicPreyRenewalQuota`、`useFoodCapacityForSelection` 在代码初始配置中均为关闭；若测试场景或导出 `landRules` 为真，按对应专项配置分析。旧 `split*`、`sameSpeciesMergeDays` 等序列化参数可能仍出现在 `landRules`，它们的存在**不表示**已停用的本地同种分裂重新运行。动态猎物更新限额启用时还需记录 `dynamicPreyRefugeFraction` 与 `dynamicPreyAdjustmentDays`。

## 4. 性状变异与正式物种演化

- 自然性状候选含适温、适湿、体型、运动、繁殖；玩家定向影响可提供候选。每个群体在一个变异周期**最多选一个赢家**，候选仍可能因权重抽签、收益门槛、性状边界或定向保护不发生变化。`ecology.kind=mutation_choice` 列出候选及选中者；同时看前后 `fitT/fitH/size/movement/fertility`。自然候选在高 `N/K` 下受到强抑制，候选门槛默认 `0.00002`，并非每次有环境压力都必变异。
- 正式演化默认开启，只沿完整物种演化图中的合法连接；目标未占本格、总席位少于 5，五项性状都在目标容差内（默认各 ±5），且满足食物可持续性与冷却。默认当天食物满足至少 0.9、更新食物满足至少 0.8；捕食者还检查猎物更新覆盖至少 0.8。普通需连续合格 5 天，已有 4 席位需 10 天。
- `ecology.kind=evolution_trait_range` 只在某群体对某连接目标物种的五性状**进入或退出**目标数值区间时写一次，同一天多项变化合并为一条；模拟器逐地块日志同步显示。`detail` 给出目标物种、变化性状的当前数值与目标区间、当前保护中的运动/体型/繁殖性状及持续天数、仍在范围外的性状各差多少。首次观察时已在范围内的性状会作为“进入”报告；之后区间内连续停留不重复刷日志。区间到位只表示性状条件满足，不代表食物、席位、冷却及连续天数均已满足。
- 达成后从祖先中转化约 10% 的创始个体（不取尽祖先），并按新体型结算可用植物物质与碎屑；源和后代进入默认 50 天冷却。看 `transition.kind=PresetEvolution` 的来源/结果 ID、物种、数量，及 `ecology.kind=preset_evolution`。目标暂未出现时先核对图谱、性状、连续天数、食物、席位和冷却，不能只看人口。

## 5. 自然迁徙、邻地运动与 V6.2 可选规则

- 默认自然迁徙沿相邻地块；陆地平地要求运动 `m>30`，高差 1 要求 `m>60`，高差 2 不可迁。目标不能超过 5 个不同活种群席位，同种到达可合并。数量、迁出冷却与刚迁出地块的立即返迁限制继续生效。基础 V6.1 模式沿现有超载、目标资源和概率规则运行；**没有 `transition` 并不能证明本日没有候选**。
- 基础标准人数：来源 `N<95` 则 0；`N>=1000` 则 100；中间取整数 `(N+5)/10`，故实际自然迁出限制为 10–100 只。先生成计划，再拆分、落地/同种合并，失败则回退，来源加目标总人数守恒。`transition.kind=Migration` 可核对 `amount`、`sourceBefore/After`、`targetBefore/After` 和 `outcome`。
- 独立开关 `enableNeighborMovementFormula` 可启用 V6.1 邻地运动候选；默认关闭。V6.2 使用**另一个**内部开关 `enableV62Mobility`，默认关闭、当前普通导出界面无开关，且 schema 4 `header` 不记录它或系数。因此普通 Play 导出不得标成 V6.2 已验收。
- V6.2 开启后，长期运动诱导和真实自然迁徙**共用只读的邻地生态收益预测**。它先按上述人数找出目标可持续食物可支付成年维护的最大迁出人数，少于 10 人的路线无效；草食走植物预算，捕食走合法猎物关系，已有目标居民及本轮计划到达者参与竞争。预测不实际扣除能量/人口，运动维护成本只算一次。
- V6.2 每只日净增长价值 `V=clamp01(0.5+(预计可支付出生−预计死亡)/人数)`；目标 `V−0.5≥−0.01` 且 `Δ=V目标−V来源>0.002`，归一化收益 `D=1−exp(−(Δ−0.002)/0.04)`。平地连接系数 1；一级坡地运动诱导系数 `0.6+0.4m/100`，收益随 `m` 增大递减，并加本地性状变化收益。候选仍须过变异收益门槛、定向限制，竞争唯一席位；邻地候选不乘自然 `N/K` 强抑制。
- V6.2 迁徙还乘目标食物/需求与气候连续性、实际可承载人数比例、平坡系数（1 / 0.5）、目标已有 4 席位时的修正，以及 `D` 后抽签。系数是待 Unity 长期验证的候选值，玩法门槛已确定。`neighborAttraction` 表示邻地收益信号，不是迁徙概率。专项测试须记录启用方式与候选参数，并用相同开关重放。
- 玩家手动迁移是另一条操作：它可迁移整组并按经济设置收费，不应拿自然迁徙的 10–100 只约束判断它。

## 6. 其他模块与当前默认状态

- 天空生态、海洋生态分别有开关，默认关闭；随机突发事件默认关闭；经济收费默认关闭。日志/场景实际设置高于“默认”描述。完整食物网资产仍在，不因默认关闭而裁剪。部分水体环境和玩家操作仍可测试，但不能把默认陆地日志当成海洋/天空生态长期验收。
- 玩家经济初始生物能资源值 2000；收费关闭时合法操作通常免费，也没有日收入。收费开启后可按资源价格支付，物种解锁奖与按全图物种数量对数的日收入进入账户。`snapshot.bioenergyBalance` 可核对，价格/倍率读取当前资源资产，不依据截图猜测。
- 玩家操作包括扩地、拆地（受原点、人口和连通约束）、改水、采集、手动迁移、储存与释放、环境五工具及可用的定向影响。储存按当前规则制作 50 只副本并可释放，不应误认原地必定扣 50；采集按配置比例及最低数量。旧手动定向演化入口已禁用。成功操作进入 `action`；失败操作未必有成功记录，应同时保留测试备注/Unity 错误。
- 非法操作不得改变 `modelHash`、数量、资源或余额；地图增删、水体改形和技术持续效果要检查受影响坐标与邻接、连通性。

## 7. JSONL 格式、哈希与复现边界

每份完整文件从 `header` 起，以 `end` 止，一行一个 JSON 对象。停止 Play 会自动结束导出；也可在“生物模拟器”点击“提前结束记录并导出”。历史缺少 `end` 的文件可用于观察，不能当作已完整校验的证据。

| 记录 | 主要内容 | 用途 |
| --- | --- | --- |
| `header` | schema、场景、Unity、案例/测试者、采样、`landRules`、随机种子/版本、`buildHash`、确定性标记 | 识别版本及规则输入 |
| `snapshot` | `reason/day/tuning/modelHash/bioenergyBalance`；地块的环境、植物、阶段；群体的 ID、物种、人数、性状、当日生死、食物、能量、容量、迁徙/演化状态 | 找首次状态异常 |
| `action` | `day/time/source/operation/x/y/detail` | 确定玩家输入及同日顺序 |
| `transition` | `kind/outcome`、来源/目标坐标与群体 ID、数量、前后人数 | 查迁徙/物种转化守恒 |
| `ecology` | `day/x/y/populationId/species/kind/detail` | 查变异、演化、灭绝等原因线索 |
| `end` | `reason/day/recordedDays/logSha256/replaySha256` | 文件完整性和结果确认码 |

`logSha256` 校验日志字节内容；`replaySha256` 校验规范化输入与轨迹（备注、测试者、导出时间和倍速等非模拟输入被排除）。**哈希相同说明被纳入规范化范围的轨迹相同，不说明玩法正确，也不等于所有内部账本都已验证。**“天数＋坐标＋操作名称＋随机数”不足以唯一决定结果；还需要完整操作参数及顺序、初始状态、工程/规则版本、开关和确定性随机流。

在工程根目录运行：

```powershell
python Tools/ReplayLogAudit/verify.py "tester-a.jsonl" "tester-b.jsonl"
```

先验 `logSha256`，只有相同 `input`（场景/版本/种子/初始快照/有序操作等）才比较 `result`；`MATCH` 表示已记录轨迹相同，`DIVERGED` 指首个不同记录与字段。不同 `buildHash`、初始地块或操作顺序不能直接互判。Unity 内可在同场景、同工程和种子的 Play 模式选“在当前 Play 场景重放日志”；它从初始快照核对，首个不同快照会报告日数和模型哈希。对低层调试脚本直接修改地图、未记录的内部开关和未知操作，日志重放覆盖有限。

## 8. 完整系统测试清单

每个案例从**同一个初始快照**开始，记录案例编号、种子、`buildHash`、实际开关、操作时间/顺序、预期现象，并导出完整 JSONL。先做默认陆地模式，再单独做启用模块；把固定种子复跑与探索性长期测试分开。

| 模块 | 最小操作/观察 | 核心检查 |
| --- | --- | --- |
| 基线复现 | 无操作跑 70 天，两人同版本同种子各跑一次 | 完整 `end`、输入相同、`MATCH`/确认码相同；倍速不同仍一致 |
| 生存/植物 | 观察富食、缺食和恢复量变化的地块 | 植物不越上限、不无故增减；分配、满足度、出生/死亡与资源方向一致 |
| 捕食/食物网 | 观察至少一组合法捕食关系与无关系组合 | 只有合法目标受捕食；`predationDeaths≤deaths`；无凭空猎物 |
| 性状变异 | 长跑并记录气候、资源、群体与 `mutation_choice` | 每周期至多一项获选；候选被拒/未选有迹可循 |
| 正式演化/灭绝 | 满足图谱、性状和持续食物条件；另观察持续濒危 | 合法目标、席位、冷却与数量；10 天濒危后灭绝，物质路径一致 |
| 自然迁徙 | 观察第 7、14…天、平地/坡地、同种目标、四/五席位 | 人数 10–100、门槛、冷却与合并；前后总人数守恒 |
| 环境五工具 | 各执行正负档位并跨 25/150/100 天 | 目标/阶段、库存上限、水体连通范围、合法拒绝及余额 |
| 玩家经济/操作 | 在收费开/关配置分别采集、扩拆、改水、手迁、储放 | 只有成功操作入账，失败不扣费；手迁与自然迁徙区分 |
| V6.1 邻地 / V6.2 | 用专项入口分别启用并记录开关/参数 | V6.2 同一预测用于运动与迁徙；与基线分别比较，不混称验收 |
| 天空/海洋/随机事件 | 仅在明确启用的专项场景测试 | 核对相应规则与完整图谱，不从默认陆地日志推断通过 |

长跑建议在 Day 0、迁徙周期、环境转折日和首次异常日留快照；采样设置会影响诊断精度。**总群体数的日变化**须结合出生、死亡、迁移、演化和玩家操作解释；不要仅凭两个日终快照推断单一原因。

## 9. 首次异常怎么定位

1. 校验 `end` 与 `logSha256`，记录 `header.buildHash/seed/landRules`、初始快照、实际内部开关。文件不完整先标为“观察资料”。
2. 若多人结果不同，跑校验脚本找**首个** `DIVERGED`；先核对输入和操作顺序，再看该日的操作后快照、日终快照、`transition` 和 `ecology`，不要从最终灭绝日倒推参数。
3. 固定 `Day + (x,y) + populationId`：植物与气候看 `tiles`；人口生死看 `births/deaths/predationDeaths/foodSatisfaction/energyNeed/allocatedFood`；变异看 `mutation_choice`；正式演化/迁徙看 `transition` 的前后人数、冷却和席位。
4. 有明确守恒违反、非法捕食、同输入首个不同结果，先归为**实现/复现问题**。守恒成立但长期生态表现不理想，再讨论数值参数。无法从日志分辨的拒绝原因，用同版本 Unity 重放、生态账本或专项诊断确认。

| 症状 | 优先查看 |
| --- | --- |
| 种群突然衰亡 | 环境阶段、植物增长/消耗、满足度、寒冷/失配、捕食死亡；区分主动操作和自然过程 |
| 植物/能量异常 | 实际库存与分配、容量、捕食链、生态账本；JSONL 未导出全部账本残差 |
| 变异长期不发生 | 周期、候选收益、`mutation_choice`、`N/K`、定向保护及开关 |
| 演化不出现 | 图谱连接、五性状容差、连续合格天数、食物更新、席位与冷却 |
| 迁徙不出现 | 检查日、人数、运动门槛、高差、邻接、席位、冷却、食物及概率；日志不列出所有失败候选 |
| 迁徙/演化后人数不对 | `transition` 的 source/target before/after、`amount/outcome`，再查玩家操作及当日生死 |
| 生物能不对 | 收费开关、资源资产、成功 `action`、解锁/日收入、操作前后余额 |

提交问题时附：完整 JSONL、此 Markdown、工程提交号及 `buildHash`、实际开关/参数、案例步骤、期望与实际、首次异常 Day/坐标/群体 ID、相关记录原文、两个 `end` 哈希。若只有一份日志，写明尚未做同输入复跑。

## 10. 源码索引和证据等级

- 每日调度：`Assets/Scripts/Simulation/SimulationController.cs`；陆地生命与植物预算：`Assets/Scripts/Simulation/LandLifeRules.cs`；变异：`SimulationController.LandTraits.cs`；正式演化：`SimulationController.Evolution.cs`；迁徙：`SimulationController.Migration.cs`；V6.2：`SimulationController.V62Mobility.cs`、`V62MobilityRules.cs`。
- 环境：`Assets/Scripts/Simulation/Environment/EnvironmentRules.cs`、`SimulationEnvironmentController.cs`、`EnvironmentTechnologyController.cs`；食物网：`Assets/Scripts/Simulation/SimulationController.FoodWeb.cs` 与物种资产；经济：`Economy/BioenergyAccount.cs`、`PlayerEconomyController.cs`、`Assets/Resources/BioenergySettings.asset`。
- 导出/重放：`Editor/PopulationSimulatorWindow.LiveExport.cs`、`PopulationSimulatorWindow.LiveReplay.cs`、`Tools/ReplayLogAudit/verify.py`；专项测试可查 `Editor/V62MobilityRegressionChecks.cs` 和环境审计脚本。

区分四种结论：**代码存在**、**专项自动测试通过**、**Unity Play 实机长期验收通过**、**已合并主线**。日志哈希只证明该日志的完整性/已记录轨迹，不自动提升到后两种结论。
