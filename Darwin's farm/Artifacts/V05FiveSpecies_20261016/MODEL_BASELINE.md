# 《达尔文农场》v0.5 Unity M 层数值基线

记录时间：2026-10-16（本阶段任何代码修改之前）。本文件只记录当前真实 Unity 工程与 `V05FiveSpeciesSandbox.unity` 的配置；`Tools` 中的 Python 或其它原型候选值不算已生效配置。Unity 6000.2.9f1，`SimulationController` 场景字段见沙盒场景 YAML；场景没有序列化 `landRules` 子字段，因此 `LandRuleTuning` 的实例初值经 `Sanitize()` 生效。若后续运行时读值与此不一致，以 Unity 运行时读值为准并记录差异。

## 基线与证据

固定操作语义是**指定日结算后**按输入数组顺序执行，下一日生态开始见效。初始仅 `(0,0)` 一块 55/35/50000 干燥荒漠；正式 Task 2/3 输入为 seed 240、1000 只鸡、初始 S=1000000，840 只新增鸡的身体物质 268800 从 S 转移，日 0 S=731200。鸡 71、彩鸡体型 14 已按用户许可同步自最新 `bioweb (13).json`；图源 SHA256 `763fdd910f882df696688cf96a794155daa468cc34543a6077cd120df65c3dd5`。禁用货币成本但保留物质支付。随机算法版本 `DF-FNV64-1`，按 seed、day、事件键、地块和种群 ID 哈希，不依赖 Unity 帧顺序。

前阶段最终基线三次独立 Unity 进程的完整 JSONL SHA256：Task 1 120 日 `711def674c487e423eba5d6d619d3dbaa997cae982a66a104c611450a5872732`；鸡→彩鸡并续至 500 日 `eea024eee1bba0ec381e2df9e71c2b4a0bab5892f835c3bfb8a8c9e338093753`；鸡→灵感菇狸并续至 1000 日 `ca6d53d2c507f41c3300a2e101e5e321fabb941af43c84b6e7e1e835ba462b93`。逐日双哈希与操作/事件见 `final-validation.json` 和三组 `*-final-run1/2/3.jsonl`。这些是**当前基线验收**；任何后续公式改变须重跑并建立新三次一致性，不与旧公式哈希求相等。

运行时核对：本阶段回放器只增加观测输出后，`task1-normal-actions-nextstage-run1.jsonl` 的第 2、3 行分别从真实 Unity `ReadLandRules()` 和 `ReadTuning()` 导出。`ruleVersion=5`、`preyEnergyPerSize=40`、`birthRate≈0.1`、`evolutionTraitTolerance=5`、`evolutionRequiredDays=5`、`flatMigrationThreshold=30`、`slopeMigrationThreshold=60`、`migrationInterval=7`、`migrationCooldownDays=7` 与下表一致。完整运行时字段保留在该 JSONL，浮点展开值为单精度表示，不是另一个参数版本。

下表每行依次给出 **实际 Unity 值 / 最近修改 / 来源 / 冻结或候选 / 对应测试 / 修改原因**。`未改` 表示本阶段开始时沿用既有代码，并非宣称该值永远不可调整。测试缩写：`T1` 基础确定性，`C` 彩鸡首段，`M` 菇狸首段，`D/P` 鹿/孔雀后段，`S` 空间 C1–C3。

## 生理、库存与捕食

| 参数 | 实际 Unity 值或公式 | 最近修改 | 来源 | 属性 | 测试 | 修改原因/作用 |
| --- | --- | --- | --- | --- | --- | --- |
| 成年能耗 | `(max(0,size)+200/max(1,size)) × (1+0.4×(movement/100)^2)` | 未改 | `LandLifeRules.AdultNeed`、`LandRuleTuning` | 候选公式数值 | T1/C/M/D/P | 体型小也有基础维持成本 |
| 幼体能耗 | `0.4×size+0.02×size²` | 未改 | `LandLifeRules.YoungNeed` | 候选 | T1/C/M/D/P | 出生支出 |
| 出生身体成本 | `max(1,size)×40` 每只；出生先扣能量与身体成本，死亡体物进入碎屑 | 30→40（上一轮） | `LandLifeRules.BodyMass`、`LandLifeRules.Simulate` | 40 为当前候选 | C/M/D/P | 30 时菇狸候选可持续猎物仅覆盖约 64%–74%，未改正式 80% 门槛 |
| `preyEnergyPerSize` | **40**，同时决定身体质量和猎物潜在能量；实际捕食获得量再乘 `min(1,hunter.size/(prey.size+15))` | 30→40（上一轮），`LandLifeRules` 无规则兜底同步 | `LandRuleTuning`、`LandLifeRules.BodyMass/PreyEnergy` | 候选，变更须单独回归 | C/M/D/P | 既影响正式捕食食物覆盖，也影响初始鸡、分化和出生物质支付 |
| 出生率与死亡 | 出生系数 0.1；饥饿死亡率 0.6，基线成年死亡 0.008，体型虚弱项 0.5，尺度 20，低温 0.005，气候不合 0.01，繁殖压力 1.2 | 未改 | `LandRuleTuning`、`LandLifeRules` | 候选 | T1/C/M/D/P | 控制群落稳定性 |
| 植物 S/R | 陆地容量 1000000；沙漠初始 R=50000；基础库存获取 10%/日、最低保留 10%；小植物生态位份额 0.7，无专精效率 0.7 | 初始 S 50000→1000000（仅正式输入） | `EnvironmentRules`、`LandRuleTuning`、场景与输入 | S 初始条件经用户确认；公式候选 | T1/C/M/D/P | 初始增鸡身体费用从 S 支付，R 限制长期承载 |
| 植物体型可及性 | 小植物层尺度 **20**，效率 `1/(1+(size/20)^4)`；大植物层尺度 25，效率 `(size/25)^4/(1+(size/25)^4)`；层归属由物种模板或分支策略决定 | 未改 | `LandRuleTuning.smallPlantSizeScale/largePlantSizeScale`、`LandLifeRules.NicheEfficiency/PlantNiche` | **候选公式数值**，层归属机制不改 | C/M/D/P | 体型变大时，可及植物层的食物效率发生变化；接近孔雀的彩鸡仍属小植物层 |
| 捕获概率 | `clamp01(0.5 × clamp(1+(猎手运动−猎物运动)/40,0.1,1.5) × clamp(((猎手体型+10)/(猎物体型+10))²,0.1,1.5))` | 未改 | `LandLifeRules.CaptureChance` | 候选 | M/D/P | 速度和体型影响成功 |
| 遭遇率与猎物偏好 | 遭遇 `min(1,10/max(1,prey.size))`；偏好=遭遇×捕获×猎物能量 | 未改 | `LandLifeRules.HuntPreference/HuntLevel` | 候选 | M/D/P | 大体型猎物较难遇见 |
| 捕食者拥挤干扰 | `competitionPressure=2`；不同策略互扰权重 0.5；猎物整只结算，分数余量累积 | 未改 | `LandRuleTuning`、`LandLifeRules.HuntLevel` | 候选 | M/D/P | 控制多猎手分食 |
| 猎食储备 | 当日额外狩猎目标成年需求的 10%；储备最多 10 日成年需求 | 未改 | `LandRuleTuning`、`LandLifeRules.HuntLevel` | 候选 | M/D/P | 防短时断粮 |

## 选择、压力、保护与正式进化

| 参数 | 实际 Unity 值或公式 | 最近修改 | 来源 | 属性 | 测试 | 修改原因/作用 |
| --- | --- | --- | --- | --- | --- | --- |
| 实时环境 K | 植食：R×生态位份额×尺寸效率×无专精效率，减同位竞争者需求，再除每只成年需求；捕食：可持续猎物能量除成年需求 | 未改 | `LandLifeRules.EnvironmentalCarryingCapacity` | 公式冻结，系数候选 | C/M/D/P/S | 真实 R/食物决定容量 |
| N/K 自然变异机会 | `0.5−0.495×clamp01((N/K)/0.95)`；K≤0 时比率按 1；每候选使用事件键随机 | 未改 | `SimulationController.LandTraits` | 已确认机制，数值候选 | C/M/D/P/S | 密度越高自然机会越低 |
| 长期收益判断 | 候选群增长差/人口必须 `>0.00002`；气候候选权重 `1+abs(差)/20`；自然策略权重 `clamp(收益×selectionStrength×100,0.05,5)`；每天最多选一项一步 | 未改 | `SimulationController.LandTraits`、`LandRuleTuning` | 已确认机制，阈值候选 | C/M/D/P/S | 净收益过滤自然策略变异 |
| 玩家压力 | 植物 +：体型 `+3`、繁殖 `+4.5`、运动 `−1.5`；植物 −：体型 `−4.5`、繁殖 `−3`、运动 `+1.5`；温度 +/− 给体型反向 `1.5`；湿度 +/− 给繁殖同向 `1.5` | 未改 | `SimulationController.Influence` | 候选 | C/M/D/P | 玩家工具成功后才记入，不能直接写五 Bar |
| 压力时段 | 操作后第 1–7 日有效，后 5 日残余；累积 potential 每日 `weight×0.1` 限 ±3；命中对应变异后 potential 减半 | 未改 | `SimulationController.Influence` | 候选 | C/M/D/P | 效果有惯性和时效 |
| 目标保护 | 运动/体型/繁殖进入目标模板 ±5 后受保护；目标改变时重置；连续 10 个增长记录平均 `<−0.01` 可逐日 FIFO 释放最早保护，先寻找区间内补偿路径 | 未改 | `SimulationController.Influence` | 已确认机制，数字候选 | D/P | 防止临门退化，衰退时可逐项解除 |
| 正式进化 | 五 Bar 每项与目标模板差≤5；同地块目标尚未存活，最多 5 个活种群；连续合格 5 日，恰有 4 群时 10 日；冷却 50 日 | 未改 | `SimulationController.Evolution`、`LandRuleTuning` | **冻结规则** | C/M/D/P | 不为成功放宽 |
| 分化与食物 | `round(源群×0.1)`、至少留 1；植物补足更大子代体量，缩小时多余体物入碎屑；当日满足 0.9、持续满足 0.8、捕食可持续猎物覆盖 0.8 | 未改 | `SimulationController.Evolution` | **冻结规则** | C/M/D/P | 严守事务和物质守恒 |
| 其它选择辅助 | 邻地吸引阈值 0.2、局部运动增益 1、邻地运动增益 2；库存天数低/高 3/5、恢复覆盖低/高 0.9/1.1；同种合并 100 日、合并后分化冷却 200 日 | 未改 | `LandRuleTuning`、`SimulationController.LandTraits` | 候选 | D/P/S | 空间和资源信号 |

## 玩家环境工具

| 参数 | 实际 Unity 值或公式 | 最近修改 | 来源 | 属性 | 测试 | 修改原因/作用 |
| --- | --- | --- | --- | --- | --- | --- |
| 温湿度 | tier 1/2 对目标值 ±25/±50；25 日进入、150 日保持、150 日恢复；有效操作可以提前撤销，立即转入恢复轨迹，不瞬间归零 | 未改 | `EnvironmentRules`、`EnvironmentWorld.TryCancelClimate` | **冻结工具规则** | T1/C/M/D/P/S | 保留及时收手的手感 |
| S/R 植物工具 | tier 1/2 库存按现值 ×(1±0.25/0.5)，R 调整 ±25000/±50000 且上限 150000；R 保持 50 日、恢复 50 日；保持期内可撤销并进入恢复 | 未改 | `EnvironmentRules`、`EnvironmentTechnologyController`、`EnvironmentWorld` | **冻结工具规则** | T1/C/M/D/P/S | 工具可影响供应，但身体物质不得无偿生成 |
| 操作冲突 | 同格技术槽按既有序列和回退处理；未被接受的操作必须写拒绝日志，不能视为成功路径 | 未改 | `EnvironmentTechnologyController`、`EnvironmentWorld.EnforceSlots` | 冻结 | C/M/D/P/S | 避免无效输入混入回放 |

## 濒危与空间

| 参数 | 实际 Unity 值或公式 | 最近修改 | 来源 | 属性 | 测试 | 修改原因/作用 |
| --- | --- | --- | --- | --- | --- | --- |
| 濒危注销 | 群体 `<10` 连续 10 日；注销时身体与储备回碎屑 | 未改 | `LandLifeRules.Simulate`、`SimulationController.LandEvents` | **冻结规则** | C/M/D/P | 后段新种群必须先观察稳定 |
| 自然迁徙时机 | enabled，7 日一检；本地冷却 7 日；同种不返源直到环境变化；目标同种居民阻止计划进入 | 未改 | 沙盒场景、`SimulationController.Migration` | 已有机制 | S | 只验证和做回放接口 |
| 迁徙资格与数量 | 高差≥2 阻断；平地运动须>30、坡地>60；低于 10 只不迁；迁徙 `clamp(round(N×0.1),10,100)` 且不超过 N | 未改 | `LandRuleTuning`、`SimulationController.Migration` | **冻结数量/连通规则** | S | C1/C2 核心断言 |
| 迁徙概率/容量 | 吸引度=邻地自由食物/(自由食物+迁群成年需求)×气候适配×连续性，乘运动/100；四群入口系数 0.5；目标容量修正 `clamp01(1−目标同种居民数/K)`；现有自然迁徙路径禁止已有同种进驻 | 未改 | `SimulationController.Migration`、`LandRuleTuning` | 候选概率，容量公式已存在 | S | C2 验证实际路径和合并语义差异 |
| 地图约束 | `(−1..1,−1..1)` 3×3，初始 1 块，最多再放 4 块陆地 | 本轮任务新增验收范围 | 用户指令、`V048ScenarioReplay.ValidateRestrictedV05World` | **冻结测试范围** | S | 不扩建地图系统 |

## 变更记录规则

本文件在任何实验改动前保存。后续只改未冻结公式参数时，另在 `PARAMETER_CHANGELOG.md` 写旧/新值、Unity 回归与回退文件。正式成功需第 0 日三次独立完整 Unity 进程逐日模型和环境双哈希一致。检查点仅在完整状态恢复与全程模拟的后续逐日双哈希完全一致后可用于探索；否则记 `BLOCKED`，节省模拟日数为 0，绝不以人口/五 Bar 片段充当检查点。

## 本阶段执行后的版本说明（不改写改前基线）

上表的「目标同种居民阻止自然迁徙」与「自然路径未使用容量修正」是**修改前事实**。本阶段只修正这两项迁徙路径差异：允许迁入已有同种群并合并，同时在自然迁徙机会中乘已有 `TargetCapacityCorrection`。`PARAMETER_CHANGELOG.md` 有回退文件和实测事件。小植物层尺度 20→30→40 的探索均失败，已经逐字节恢复到 20；当前正式 Task 4 和失败路径的运行时 `landRules` 仍为 20。`preyEnergyPerSize` 始终是 40。修改迁徙规则后，三组固定输入各三次独立 Unity 回放通过，见 `nextstage-fixed-validation.json`；新的 C1/C2 和选定失败路线各三次验证见 `nextstage-final-validation.json`。
