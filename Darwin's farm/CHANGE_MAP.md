# Mutation V4 CHANGE_MAP（基线审计及用户追加约束）

基线：原工作树 `Ye-Branch` / `73a9c975` 有 3 个已跟踪源码差异和 12 个未跟踪邻地源码及 meta；新分支 `codex/darwin-mutation-v4-20261018` 已逐项复制，关键源码 SHA-256 一致。`WhiteBox.unity` 序列化 `preyEnergyPerSize=30`、进化冷却 50、变异间隔 1、迁徙间隔 7；项目声明 Unity 6000.2.9f1，后续编译和回放均使用该匹配版本。场景与运行时调参优先于类初值。

| ID | 当前实现 / 依赖 | V4 目标 | 最小修改点 | 风险与边界 |
| --- | --- | --- | --- | --- |
| D01 | `LandTraits.EvolveCommunityNewRules` 把气候、自然、玩家、V2 邻地混入单次权重签 | 定向预判→兄弟低频→自然，同群同日一次±1 | 拆内部调度；保留公开入口和旧预测调用 | 不用旧 V2 收益冒充新运动上涨概率；未冻结的独立长期自然选择不填数 |
| D02 | `Influence.RecordPlayerTechnology/UpdateInfluences/InfluenceNet` 给四类操作7日主次压力、5日残留 | 增量仅 L0 繁殖脉冲；库存仅体型但概率未冻结；温湿仅通过实际气候 | 保留公开 `RecordPlayerTechnology` 签名，内部改派；旧字段名保留兼容 | 旧字段可能被存档和确定性哈希读取；不能静默启用库存未知参数 |
| D03 | `EnvironmentTechnologyController.TryDeploy/TryCancel` 只在部署成功后通知生态，无取消通知；`EnvironmentWorld` 25/150/100 | 用实际 `ΔR` 建地块级事务，次日到 day+175，同向合并/反向重建/取消，16/32共享额度与15..85软限；每个种群另记已兑现次数 | 部署前后快照与取消通知；地块事务共享已用额度，种群记录事务键和已兑现次数；迁徙复制、同种合并不重置地块额度 | 多种群/迁徙/合并不得重复领取；边界裁剪、零变化、确定性哈希；保留现有序列化接口 |
| D04 | `ForecastLandGrowth` 一日预测仍被自然与邻地路径用 | 玩家/捕食定向不再受它的正收益门槛 | 新阶段绕过自然候选，保留旧方法 | 未冻结长期替代模型标 `BLOCKED_OPEN_SPEC` |
| D05 | `Influence.RefreshTrackedTarget/LeavesProtectedTarget` 已追踪合法相连目标且有±5保护 | L1/L2 `s/f` 兄弟独立双向 ±1、2%～10% | 在定向未命中后取目标并抽唯一签 | 目标已占格、保护边界、唯一席位 |
| D06 | `FoodWeb.CanEat` 要营养级及物种食物边；`LandLifeRules.AvailableSustainablePreyFood` 扣猎手竞争 | 捕食型体型协同、供需比驱动繁殖；节能体型回落 | 复用真实合法猎物和可持续净供给 | 本地无可信猎物更新时拒绝来源；不把所有 L1 当捕食者 |
| D07 | 原工作树 V2 邻地反事实已实接 `LandTraits/NeighborOpportunity/Migration`，默认开 | 旧 V2 邻地运动候选退出自然变异调度；物理迁徙安全判断可暂留；V4 上涨概率/反冲未冻结 | 去除 LandTraits 中 V2 上涨候选入口，不改迁徙物理判断 | 不能同时执行两套上涨机制；回归可能受影响，未冻结内容标待定 |
| D08 | `LandTraits` 有运动成本回落 | 各陆地等级新增合法兄弟目标 `m` 仅向下 −1、2%～40% | 自然阶段单签加入 | 捕食者不能阻挡 L0，±5停止，不能同日双变异 |
| D09 | `LandLifeRules.Simulate` 默认压力按预计出生，`enablePaidBirthStress` 会影响所有等级 | 仅合法食物网陆地捕食群用实付 `born` | 出生数确定后条件调用 `PaidBirthStressDeaths` | L0原公式不变；不要修改猎杀/资源付款 |
| D10 | `Evolution.TryPlanFormalEvolution` 可分出1～9只，食物门槛只看当天/历史供给 | 仅目标 L2 实付创始≥10、可持续猎物比、90日只读整数准入 | 在 `IsEligibleEvolutionTarget/HasEvolutionFoodSupport` 提交前增检查；沿真实供给方法 | 试算无真实资源、随机数状态或冷却副作用；需核查历史供给可信度 |
| D11 | `LandRuleTuning` 类和 WhiteBox 场景均为50日 | 新成功进化双方300日；迁徙继承、同种合并取较晚；不直接覆写旧存档已有的绝对冷却日 | 保留 `nextEvolutionDay` 字段，运行时规则冷却规范为300；现有 `PopulationTransfer.CopyForMove/Merge` 已按此传递 | D+299/D+300边界；不能改旧存档日期或场景字段名 |
| D12 | 真实生物网在 `Assets/BiowebV46/bioweb.json`（15物种/20进化边/25食物边）及 `Species` 资产；WhiteBox 由 `WhiteboxBiowebTestSetup` 装入 | 只按实际本地合法食物边与目标判定 | 不改 JSON、asset、GUID、scene | L1 中合法捕食者和 L2 要按 `FoodWeb.CanEat` 确认；Play 实际覆盖未知 |

修改顺序：先 P0 冷却→付费出生压力→只读准入；再 P1 事务型增量脉冲；最后 P2 生态定向与兄弟节点。每批做 diff、编译、最小单测，失败即停在该批；之后固定种子 240/241 做 T2A/T2B，时间允许才 T2C。保持经济、灾害、海洋、天空关闭，所有生态付款照旧。不修改公开接口／事件签名、现有序列化字段名、GUID、资源；JSONL header 后续按用户要求增补模型版本字段，原有记录类型及 schema=4 保持。

未冻结：库存 `pmax/y` 与反向轮次、邻地运动上涨概率、迁徙反冲、长期自然选择替代公式。已有 V2 迁徙安全门槛可保留，但不视为 V4 上涨公式。

规则缺口：`s_escape` 只被命名为“比较实际猎手体型”，附件没有给可执行计算式。当前仅接入已写明的合法猎物 `s_hunt`，逃逸体型加成标记 `BLOCKED_OPEN_SPEC`，没有推定数值。

## 修改与验证结果

P0→P1→P2 均已在本 worktree 内修改；原工作树未用于修改或测试。新增事务状态保留既有序列化字段与公开入口，邻地 V2 物理迁徙检查仍在，V2 运动上涨候选退出变异调度。未改现有资源 GUID、食物网和场景配置。新 300 日冷却仅用于之后的正式进化；旧存档的绝对截止日未重写。

| 检查 | 结果 | 证据与范围 |
| --- | --- | --- |
| P0/P1/P2 编译、最小边界 | PASS | Unity 6000.2.9f1；`p0-compile.log`、`p1-boundary.log`、`p2-boundary.log`、`final-boundary-175.log`；末次覆盖第175日同向合并、第176日新事务、94/95只门槛、迁徙冷却继承及合并。`git diff --check` PASS。 |
| 旧 V43 编辑器审计 | FAIL | `p1-audit.log` 在“Day one must add one effective day on a reverse edge”失败；该旧夹具的资源/进度前提尚未适配新规则，不能记为 PASS。 |
| T2A，seed 240，275 日 | Unity 回放 PASS（所执行场景） | `replays/T2A-low/high/sequence.raw.jsonl`，三种部署均被接受；低/高档次日开始，环境恢复轨迹与25/150/100相符；同向、反向操作按地块事务处理。`T2A-low-repeat` 的 277 条模型及环境哈希完全一致。成功次数上限由边界测试确认，随机成功数不要求刚好16/32。 |
| T2B，seed 240/241 | 准入与付款 PASS；D+90/180 存活 FAIL | 足供给场景均于 D5 产生10只 L2，全部观察到的新生均满足完整食物付款；D+30 为9只，约 D40 灭绝，D+90/180 无存活 L2。低供给场景 120 日未准入 L2。`replays/T2B-*.raw.jsonl`。90日准入是附件指定的只读外生供给模型，不代表封闭食物网保证存活。 |
| T2C，seed 240 | FAIL | 两条既有玩家操作日程分别回放 1800/1500 日，操作均被接受，但没有正式进化：`replays/T2C-*.raw.jsonl`。未定位唯一原因；库存定向概率、邻地运动上涨概率仍未冻结。 |

尚未完成且不得冒充 PASS：库存 `pmax/y` 与反向规则、V4 邻地运动上涨与迁徙反冲、`s_escape` 的数值式、独立长期自然选择替代式。由于 T2C 无完整路线且 T2B 新生群在真实生态中灭绝，当前不能认定已通过下一轮玩法验收。

最终一次代码修正仅涉及第175日同向再部署的事务边界；完整 T2A/B/C 回放发生在该修正之前，修正后已用 `final-boundary-175.log` 重新编译并通过相应最小边界检查，未重复长程回放。

后续日志来源澄清：用户所附 `Darwin_Whitebox_20261018_044049.jsonl` 出自原工作树，header 的 `evolutionCooldownDays=50` 且仍有旧 `player` 权重候选，不作为 V4 验收。按用户新要求，worktree 的 WhiteBox JSONL header 增加 `mutationRulesVersion="MutationV4-P0P1P2-partial"`；新版本纳入复现确认码，旧 schema=4 日志仍按旧格式校验。Unity 6000.2.9f1 编译及 `MutationV4BoundaryChecks` 校验 PASS（`mutation-version-header-check.log`）。

## Mutation V4.1 增量接入（2026-10-18）

| 范围 | V4 基线 | V4.1 当前实现 | 修改点及边界 |
| --- | --- | --- | --- |
| L0 库存→体型 | 缺少库存定向来源 | 仅实际库存净变化建立地块事务，次日开始；低/高档 `pmax=.85/.98`、`rho=.985/.993`、时长120/170日、每群额度30/55；`q_real=clamp01(direction*(P-a)/abs(b))`，`q_memory=max(q_real,rho^age*q_memory)`，`p=pmax*q_memory*(1-age/y)`；同向合并不重开时钟，反向新轮，零净变化不触发；显式库存可在兄弟目标附近有上限过冲 | `EnvironmentTechnologyController.TryDeploy/TryCancel`、`SimulationController.StockGuidance`、`BlockInfo/PopulationData`、迁徙复制/合并和确定性哈希；旧字段名不变。 |
| 邻地→运动上涨 | 旧 V2 候选已退出，V4 缺上涨来源 | 最优一个合法相邻地；平地 `.65` 到 m40，一级坡 `.85` 到 m75；每群每边180日；保留真实迁徙物理和资源限制、兄弟目标 ±5 保护。目标 m15 的鸡会在 m20 停止，这属于当前正确行为 | `SimulationController.MovementGuidance`、定向唯一抽签入口；不恢复旧 V2 候选。独立目标鹿的 Unity 回放平地到 m40、一级坡到 m75。 |
| 兄弟运动回落 | 各等级 2%～40%，仅 −1 | L0 上限55%，L1/L2 仍40%，仅 −1 | `SimulationController.LandTraits`。 |
| 多来源调度 | V4 每日单个定向/自然席位 | 库存、邻地、捕食及既有增量等先构造候选，再按联合命中概率一次抽签；定向命中跳过自然阶段 | `SimulationController.LandTraits.TryDirectedMutation`；保留 V4 增量、温湿、L1/L2 协同、出生付款和300日进化冷却。 |
| L2 猎物存量新门槛 | V4 已有10只、可持续净供给和90日只读预测 | 本轮不新增猎物库存门槛；拒绝日志记录 `founders_below_10` 等既有原因，并追加只读 `legalPreyStock` 观察值 | `SimulationController.Evolution.HasSustainableL2FounderSupport`；新增门槛仅研究，未进生产。 |

V4.1 的 Unity 短程固定种子回放文件位于 worktree 根目录 `replays/V41-*.raw.jsonl`。`V41-flat-240-fixed` 是临时绕过保护的诊断产物，已撤销对应代码，不可用于当前规则验收。此次冷却调整前的 WhiteBox 导出版本标识为 `MutationV4.1-stock-neighbor-20261018`。此处的 V4.1 测试结果与剩余风险以后续最终交付说明为准。

## 2026-10-18 冷却调整与 07:03 日志核对

用户将新正式进化冷却由 300 改为 100 游戏日。`LandRuleTuning` 默认和运行规范值、WhiteBox 场景序列化值均改为 100；`ConvertToPresetSpecies` 原有的祖先及后代 `day + evolutionCooldownDays` 路径不变。旧存档中的绝对 `nextEvolutionDay` 不追溯改写，迁徙继承及同种合并取较晚截止日的规则不变。新的 WhiteBox 导出版本标识为 `MutationV4.1-stock-neighbor-cd100-20261018`，与旧 300 日日志区分。

`Darwin_Whitebox_20261018_070351.jsonl` 仍是修改前规则：header 的 `evolutionCooldownDays=300`。鸡在 D255～D287 连续 33 日处于灵感菇狸五项性状的 ±5 区间，但日志未导出 `nextEvolutionDay` 及 L1 食物准入结果，不能从五项性状达标单独证明正式进化应当发生。D278 将相邻地块从高 0 连升至高 2 后，鸡保持 m38；高差 2 被邻地候选和真实迁徙视为悬崖，兄弟运动回落只在 `m > 35+5` 时提供 −1 候选，因此该次操作没有运动下降。

验证：`cooldown100-red.log` 在新 100 日检查上按预期失败；修改后 `cooldown100-green.log` 的 Unity 编译及 `MutationV4BoundaryChecks` PASS。固定 seed240 的 7 日 Unity Play 回放 `cooldown100-7day-240.raw.jsonl` 在 D5 产生正式进化，规则值 100，祖先绝对截止日 D105；后代截止日由 `ConvertToPresetSpecies` 同一路径写入，短程回放未单独导出后代该字段。`git diff --check` PASS。

## 达标未进化逐日诊断

五项性状全部进入目标 ±5 区间后，若当天未正式进化，新增 `formal_evolution_waiting` 生态事件，每个种群、目标、游戏日一条；已有 JSONL `ecology` 行沿用原字段。事件包含当前性状、首个未通过的正式准入门槛、连续合格天数、目标天数、绝对冷却截止日与剩余日、计划创始数、目标是否已存在及地块种群数。食物门槛失败时附上只读的植物或可持续猎物供需值。累计期显示 `consecutive_days`；成功进化的同一目标当天不记等待。该事件无论全局生态控制台开关如何均调用 Unity `Debug.Log`，其他生态事件的开关行为不变。WhiteBox 新导出标识为 `MutationV4.1-stock-neighbor-cd100-diag1-20261018`；旧 07:03 日志不含这些逐日事件。

## Ye-Branch 接入状态（2026-10-18）

当前正式进化新冷却为 100 游戏日，已覆盖上文历史阶段的 300 日描述；旧存档的绝对截止日不追溯改写。V4.1.1 的 L1 准入改用现有合法猎物库存与当日共享捕食反事实，L2 仍因缺少安全的 90 日只读生存预测而阻断。三物种生态候选 V2 仅作源码接入：实际付费出生压力每只上限 0.85，L1 配额模式按猎物剩余可捕食配额加权，WhiteBox 六项参数使用场景序列化值。Unity 编译通过不代表三物种平衡验收。
