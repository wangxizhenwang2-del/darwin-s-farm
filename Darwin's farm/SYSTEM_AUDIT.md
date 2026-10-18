# 《达尔文农场》数值系统历史基线审计（Mutation V4 接入前）

> 本文是 2026-10-18 00:57 的只读基线记录。Ye-Branch 后续已接入 Mutation V4/V4.1.1 与三物种生态候选 V2；当前执行规则以源码及 `CHANGE_MAP.md` 的最终状态说明为准，本文公式不可直接用于新版本验收。

> 依据：2026-10-18 当前工作区的陆地模拟源码，包含尚未提交的邻地运动改动。本文只记录实际调用路径；水、空生命循环不纳入陆地公式。既有《当前陆地种群生死与变异计算账本》和《当前模拟系统_完整规则与JSONL测试手册》作为历史索引，凡与源码冲突均以下列源码为准。未启动 Unity，故不能把源码默认值等同所有场景的实际运行值。

下文只写文件名时，源码均位于 `Assets/Scripts/Simulation/`；环境源码位于其 `Environment/` 子目录。

## 0. 记号、参数来源和结算顺序

`P`=陆格植物库存，`Pmax`=库存上限，`R`=每日恢复量；`N`=当前群体只数；`T,H`=地块当日温、湿，`t,h,m,s,f`=群体适温、适湿、运动、体型、繁殖；`F`=气候适应度；`A,Y,M`=单只成年需求、幼体食物需求、新生身体物质；`C`=群体实际获得的植物或猎物能量；`Q`=储备；`clamp01(x)=min(1,max(0,x))`。数值单位是代码中的能量／生物量单位，并非现实卡路里。除非注明，`N` 指捕食后、出生前的数量。

| 顺序 | 当日实际执行 | 源码 |
| --- | --- | --- |
| 1 | `OnBeforeDaySimulated` 推进环境持续效果，发布当日 `T/H/R`；水域另按连通水体长植物 | `SimulationController.SimulateDay`；`SimulationEnvironmentController.Advance` |
| 2 | 每块陆地植物增长；记录捕食前 `N`、清当日计数；可选计算可再生猎物配额 | `LandLifeRules.Simulate`；`FoodWeb.GrowPlants` |
| 3 | 先营养级 2、后级 1 捕猎；从猎物活体扣整数并给猎手能量／储备 | `LandLifeRules.HuntLevel` |
| 4 | 所有草食群竞争同一植物库存的当日预算，实际支出；捕食者取猎物能量与原储备 | `LandLifeRules.AllocatePlants`、`FoodWeb.SpendPlants` |
| 5 | 逐群结算成年满足率、可付出生、非捕食死亡、储备和人数，更新最多 10 日历史；随后结算猎手可持续食物历史 | `LandLifeRules.Simulate` |
| 6 | 各群生成并抽取**至多一个**性状变异候选，立刻改 1 点；刷新地块指标 | `SimulationController.EvolveCommunityNewRules` |
| 7 | 全图清理连续濒危群，检查正式物种进化；仅在迁徙日计划并提交迁徙；更新物种和环境 K、收尾账本 | `SimulationController.SimulateDay`、`RemoveEndangered`、`RunPresetEvolution`、`RunMigration` |

`LandRuleTuning` 字段初值及 `Sanitize()` 是代码默认；`SimulationController.landRules` 可由场景序列化、`ApplyLandRules`、模拟器实时调参覆盖。`WhiteBox.unity` 明确覆盖 `preyEnergyPerSize=30`（类初值 40），其余列出的生态参数多与类初值相同；`V05FiveSpeciesSandbox.unity` 未列出 `landRules` 整块。当前运行场景、运行中调参和存档值不能从只读源码确定。下面的“默认”均指类初值，遇到具体回放应取当次实际设置。`enableNeighborMovementFormula` 当前源码初值 **true**；旧测试手册所写默认关闭已过时。`enableV62Mobility` 为另一内部开关，初值 false。`enablePriorityPlantBudgets`、`enablePaidBirthStress`、`enablePreyDensityFeedback`、`enableDynamicPreyRenewalQuota`、`useFoodCapacityForSelection`、`useCompensatedEvolutionFoodGate` 类初值均 false。见 `LandRuleTuning.cs`、`SimulationController.cs`、`SimulationController.LandTraits.cs`、`SimulationController.V62Mobility.cs` 及上述场景序列化块。

## 1. 生态账

### 1.1 植物生产、预算与实际消耗

陆格每日先算 `g=min(Pmax−clamp(P,0,Pmax), max(0,R))`，`P+=g`，记录 `plantGrowthToday=g`。默认陆格 `Pmax=1,000,000`，`R` 合法范围 0～150,000；雨林等预设的初值由当前生境配置决定。`R>0` 时：

```text
L = min(0.1 Pmax, 0.5 P)                  # 不可吃的库存底线
standing = max(0, P−g)
draw = min(max(0,standing−L), 0.1 standing)
Bplant = max(0, min(P−L, g+draw))         # 全群当日可分配预算
```

`R<=0` 时 `L=0, draw=standing`，故 `Bplant=P`。实际植物消耗 `Eplant=Σ草食群 C_i <= Bplant`，日末 `P←max(0,P−Eplant)`。0.1 底线及 0.1 日提取系数均可由 `LandRuleTuning` 覆盖。库存多不代表当天全可吃。来源：`SimulationController.CalculatePlantGrowth`、`FoodWeb.GrowPlants/SpendPlants`、`LandLifeRules.PlantFoodBudget`。

### 1.2 成年、幼体需求和植物竞争

默认 `A(s,m)=(max(0,s)+200/max(1,s))×[1+0.4×(clamp(m,0,100)/100)^2]`；`Y(s)=0.4s+0.02s²`；`M(s)=max(1,s)×40`，但 WhiteBox 场景 `M(s)=max(1,s)×30`。预计完整群需求 `D=N×A+N×0.1×(f/100)×F×(Y+M)`。`energyNeed` 字段是**每只成年** `A`。`F=clamp01[1−(|T−t|+|H−h|)/90]`。来源：`LandLifeRules.AdultNeed/YoungNeed/BodyMass/Demand/Fitness`。

默认实际分配器不是独立的 70%／30% 两份库存；所有草食群共用 `Bplant`。对群 `i`：

```text
adultNeed_i = N_i A_i
birthNeed_i = N_i·0.1·clamp01(f_i/100)·F_i·(Y_i+M_i)
reach_i = clamp01[1−0.08·(20/(20+max(1,s_i)))/(1+clamp(m_i,0,100)/10)^2]
e_small=1/(1+(max(1,s_i)/20)^4)
e_large=(max(1,s_i)/25)^4/(1+(max(1,s_i)/25)^4)
w_i=F_i·reach_i·(1+0.002m_i)·[0.7 e_small q_small + 0.3 e_large q_large]
```

群的偏好层由 `splitStrategy` 决定，否则物种模板 `baseSize>=25` 偏大层；偏好层的 `q=1`，非偏好层 `q=0.7`。分配先处理**所有成年需求**，再处理幼体与身体需求；每轮对未满足需求 `u_i` 提议 `min(u_i, remaining×w_i u_i/Σ_j w_j u_j)`，扣除已分配额后再循环，最多 `群数+1` 轮。`w_i` 同时体现气候、体型效率和运动；多群共享预算构成实际竞争。`competitionPressure=2`、`oppositeStrategyCompetition=0.5` 在当前默认植物分配路径**没有参与**；它们仍用于捕食者争同一猎物。若显式开启 `enablePriorityPlantBudgets`，才把预算按 0.7/0.3 分两层，依次给偏好小层成年、偏好大层成年、跨层成年，再同序给出生需求。来源：`LandLifeRules.AllocatePlants/AllocatePlantStage/AllocatePlantsPriority/PlantNiche/NicheEfficiency`。

### 1.3 捕食与猎手食物

只捕食 `FoodWeb.CanEat` 许可的物种（营养级与物种食物边同时满足）。对猎手 `i` 和猎物 `j`，默认：

```text
chase = clamp(1+(m_i−m_j)/40, 0.1,1.5)
capture = clamp01[0.5·chase·clamp(((s_i+10)/(s_j+10))²,0.1,1.5)]
Eprey = (猎物身体生物量/猎物只数；未初始化则 M(s_j))·min(1,s_i/(s_j+15))
pref_ij = min(1,10/max(1,s_j))·capture·Eprey
density_j = 1                                   # 默认
intent_ij = N_i·pref_ij/Σ合法猎物 pref_ik ·min(1,10/max(1,s_j))·capture·density_j
target_i = max(0, N_i A_i + 预计出生_i(Y_i+M_i)+0.1 N_i A_i−Q_i)
claim_ij = min(intent_ij, max(1,ceil(target_i/Eprey)))，条件为意图、目标和 Eprey 均>0
```

同猎物的 `Σclaim` 加猎物原捕食小数余量，先取不超过活体数的整数 `kills=floor(min(N_j,Σclaim+remainder_j))`；如开启可再生猎物配额，再以配额截断。猎手分赃权重 `claim_i/[1+2·max(0, pressure_i/max(1,N_j)−1)]`，其中 `pressure_i=claim_i+Σ其他猎手 claim_k×(策略相反?0.5:1)`；按权重分获所杀猎物身体物质，经体型转换成可食能量，未转换部分入碎屑。猎手能量入储备；后续日账先支付成年、再付新生。开启 `enablePreyDensityFeedback` 且猎物为草食时，`density_j=N_j²/(N_j²+half²)`，`half=max(1,0.58×max(1,参考R)/A_j)`，参考 R 优先 `predationReferenceRecovery`。开启 `enableDynamicPreyRenewalQuota` 时，配额 `clamp(floor(max(0,G_unhunted+(N−0.35 N_eq)/45)),0,N)`；`N_eq` 是只读零净增长估计，无可求平衡时缓冲项为 0。这两项默认关闭。来源：`FoodWeb.CanEat`、`LandLifeRules.CaptureChance/PreyEnergy/HuntPreference/HuntLevel/PreyDensityFactor/RenewablePreyHarvestQuota`。

### 1.4 出生、死亡、数量和动态 K

捕食后的群体以 `available=C+Q` 支付。`S=clamp01(available/(N A))`（`N A=0` 时取 1），预期出生机会 `B*=N×0.1×(f/100)×F×S`，`newbornCost=Y+M`。实际出生是完整付款的整数：

```text
possible=floor(旧出生余量+B*)
born=min(possible, floor(max(0,available−N A)/(Y+M)))
新出生余量=clamp(旧出生余量+B*−born, 0, 0.999)
```

默认非捕食死亡压力为：

```text
Dstarve = 0.6N(1−S)
Dcold   = 0.005N·max(0,(50−T)/50)
Dclimate= 0.01N(1−F)
Dadult  = N[0.008·20/(20+max(1,s)) + 0.5/(1+max(1,s))²]
Dstress = B*·1.2·clamp01(f/100)·Y/max(1,s)
Dother  = Dstarve+Dcold+Dclimate+Dadult+Dstress
```

可选 `enablePaidBirthStress=true` 时，`Dstress` 换成 `born×1.2×clamp01(f/100)×Y/max(1,s)`。`died=min(N, floor(min(N,旧死亡余量+max(0,Dother))))`，死亡余量减去 `died`。储备 `Q'=min(max(0,available−min(available,N A)−born(Y+M)), N A×10)`。捕食先扣 `kills`，最终 `N'=N+born−died`；当日 `deathsToday=kills+died`，不可再把 `kills` 加一次。出生与非捕食死亡小数分别跨日保留，但无食物付款的出生余量封顶 0.999，死亡压力不形成超过当日活体的“死亡债”。连续 10 日 `N'<10` 后整群本地注销，遗体和储备入碎屑。最近至多 10 日人口趋势为 `average[(当日 N'−捕食前 N)/max(1,捕食前 N)]`，含捕食，不含随后进化／迁徙。来源：`LandLifeRules.Simulate`、`RemoveEndangered`。

`carryingCapacity=当日分配食物/A`，是当日获食口径。显示用 `environmentalK` 默认对草食群为
`max(0,R×层份额×体型层效率×(偏小层?0.7:1)−同层其他草食群ΣN_j A_j)/max(0.0001,A_i)`；捕食者把分子替为 `AvailableSustainablePreyFood`（猎物最近净补充减其他猎手预计消耗，再乘猎物能量）。开启 `useFoodCapacityForSelection` 时草食 `environmentalK` 改为二分搜索：在可持续供给 `min(max(0,R), max(0,Pmax)×(1−min(0.5,保底比例)))` 下，保持其他群数量，求该群成年需求可被共享分配器支付的最大整数人数。**两种 K 均非硬人口上限，也不直接乘到当日出生公式。**来源：`LandLifeRules.EnvironmentalCarryingCapacity/FoodCarryingCapacity/AvailableSustainablePreyFood/RefreshEnvironmentMetrics`。

## 2. 变异进化账

### 2.1 五项性状与每日唯一抽签

变异默认开启、间隔 1 日、`mutationStep=1`、`selectionStrength=1`；当前执行仅用 `mutationStep>0` 作为开关，获胜后固定 **±1**。边界：`t/h/m∈[0,100]`，`s∈[1,100]`，`f∈[10,90]`。每个到期的陆地群按下表产生候选，**同一性状同一方向的权重相加**，然后以权重比例抽一个，最多一项落地；无候选则不变。当前代码每次把旧 `mutationSelectionProgress` 清零，**不存在旧账本所述的多日累计进度门槛**。来源：`SimulationController.EvolveCommunityNewRules/AddMutationCandidate/TraitLimit`。

| 候选 | 产生条件和权重（默认值） | 拒绝／冲突 |
| --- | --- | --- |
| 气候 `t/h` | 当前性状不等于当日 `T/H`，仅向环境方向 ±1；权重 `1+|环境值−当前值|/20` | 不按净收益或 `N/K` 抽资格；到位则无候选。仍与其他候选争同一次抽签。 |
| 自然策略 `m/s/f` | 分别试 `±1`，令 `G(v)=预计出生−预计五类死亡−预计被捕食`；`gain=[G(v)−G(现值)]/max(1,N)`，须 `gain>0.00002`；以 `p_nat=0.5−0.495·clamp01[(N/K)/0.95]` 先抽资格（`K<=0` 时比率取 1），通过后权重 `clamp(gain×selectionStrength×100,0.05,5)` | `selectionStrength<=0`、越界、目标保护禁止、残留玩家压力净方向相反、未过收益门槛或资格抽签失败均拒绝。接近／超过 K 时 `p_nat≈0.005`，并非绝对零。邻地开关开时普通自然运动候选被专门运动路径取代。 |
| 玩家定向 `m/s/f` | 合法环境工具提交时记录 7 日主／次压力，首日 `day+1`；到期日净压力 `net=Σ(weight+potential)`，`potential` 每活跃日加 `0.1weight` 且夹在 ±3；净值符号决定方向，权重 `|net|` | 反向压力先相消；净值 0、边界或目标保护禁止则无候选。获胜且含玩家来源时同向潜力减半。活跃结束后潜力清零，再保留 5 日残留 `Σweight`，只用于阻止相反自然／邻地候选；残留本身不提名玩家候选。 |
| 邻地运动 V2（当前源码开关初值 true） | 合格食草邻地的日人均可持续增长差 `Δ=g目标−g本地>0.002` 且目标长期增长 `>=−0.01`；`D=1−exp[−(Δ−0.002)/0.04]`，坡度系数 `c=1`（平）或 `0.6+0.4m/100`（一级坡）；`B=3·D·c·(1−m/100)/100`。若尚未达到迁徙运动门槛，资格概率 `min(0.25,B/(B+0.045))`，通过后给 `m+1` 权重 `selectionStrength`。本地 `m+1` 可持续生存收益 `>0.00002` 也可按 `p_nat` 提名，权重 `clamp(收益×selectionStrength×100,0.0001,5)`。 | 只对草食有完整邻地反事实；目标须真实双向共享边、陆地、高差≤1、有席位、迁出反事实人数 10～100，且预测包含现有居民与迁入群。`B` 是资格信号，不直接改 Bar；达实际迁徙门槛后邻地加分停用。捕食者这条候选因缺完整反事实而拒绝。 |
| 运动回落 | 无需保持运动迁徙资格时，`m−1` 若本地收益为正可按自然资格提出；另在 `m>max(30,已有邻地所需运动下限)`、成年维护确实节省、降 1 点的本地生存值下降不超过 0.001 时，以 `min(0.16,0.025+0.125(m/100)²)` 抽资格，成功权重 0.1 | 玩家残留反向压力、目标保护或 `m=0` 会阻止。邻地机会仍合格时不等于必然回落。 |

`G(v)` 的食草预测重跑共享植物分配并以成年需求算饱食；捕食者按捕猎潜力变动调整本日食物近似。`预计被捕食=Σ合法猎手 N_h·min(1,10/max(1,s))·capture(h,v)`。预测出生 `N·0.1·f/100·F·饱食`，死亡沿第 1 节默认五式；这是一日比较式，**不复制实际整数捕猎、完整付款和后续多日人口**。邻地 `g=clamp01[0.5+(预计可付出生−预计死亡)/N]−0.5`；可付出生受可持续食物扣成年需求后的 `(Y+M)` 付款约束。邻地反事实用 `StandardMigrationAmount(N)` 人组成目标群，居民参与同一个可持续分配器。见 `SimulationController.LandTraits.ForecastLandGrowth/SustainableSurvivalValue`、`SimulationController.NeighborOpportunity.EvaluateRealNeighborOpportunity`、`NeighborMovementOpportunity.Evaluate`。

目标追踪：合法相连且本格未有的目标里，按五项绝对差之和选最近；手动指定合法目标优先；旧目标距离不比最佳远超过 2 时保留以防抖。`m/s/f` 进入目标模板 ±`evolutionTraitTolerance`（默认 5）后，普通候选不得跨出；`t/h` 不保护。最近 10 日平均人口增长 `<−0.01` 时，若保护区内没有任何一项收益超过 `0.00002×N` 的可行补偿，按保护建立先后每天最多释放一项；释放只开放将来候选，不立即减点。V2 邻地 `m+1` 在尚未达到物理迁徙门槛时可越过运动目标保护边界。来源：`SimulationController.Influence.RefreshTrackedTarget/LeavesProtectedTarget/ReleaseTargetProtectionFIFO`、`SimulationController.LandTraits`。

**长期停滞条件：**气候两项已追到当日值；`mutationEnabled=false`、`mutationStep<=0` 或间隔未到；策略候选无正一日收益／未过门槛；`N/K` 高导致自然资格极低；玩家压力已过期或净抵消；目标保护挡跨界；邻地不合法／收益不足／抽签未中；其他候选（尤其气候）持续占用唯一席位；正式进化的持续食物、席位、冷却条件不满足。`selectionStrength=0` 时气候和有效玩家候选仍可能存在，故不能笼统称五项全停。来源同上。

### 2.2 正式进化与“分化”

当前**不执行旧 5% 同种本地分化**；`ReadSplitGate` 永远返回 `retired_formal_species_evolution`，`OnLocalSplitResolved` 仅留兼容观察者。正式进化是从祖先抽人建立**目标物种**，不是五 Bar 达标当日自动改名。每个合法连接（含资产反向记录的连接）分别累计合格天数；失格当天该目标计数归零。条件：陆地同生态位、目标本格未存在、源群至少 2 只、源群冷却已过、本格活群少于 5、五项各距目标模板 ≤5；目标食物门槛连续默认 5 日，已有 4 席位则 10 日。来源：`SimulationController.Evolution.ConnectedTargets/RunPresetEvolution/IsEligibleEvolutionTarget/EachTraitWithinTolerance`。

创始请求 `requested=min(N−1, floor(0.1N+0.5))`，不受迁徙 100 只上限；目标每只新增身体质量 `extra=max(0,M目标−源每只身体质量)`，可付人数 `affordable=floor(可支库存/extra)`（`extra=0` 时为 `requested`），实际 `amount=min(requested,affordable)`，至少 1，祖先至少留 1。默认食草目标的 `D目标=amount·A目标+amount·0.1·f目标/100·F目标·(Y目标+M目标)`；按祖先减员、目标加入后的**同一植物分配器**，当天 `C今天/D目标>=0.9`、可再生 `C_R/D目标>=0.8`。捕食目标按合法猎物偏好分一次狩猎意图，预计完整猎物获食／`D目标>=0.9`，历史可再生猎物能量／`D目标>=0.8` 且 `>=0.8D目标`。进化提交时支付 `amount×extra` 植物，目标体型较小的多余身体质量入碎屑；目标五性状重置为模板，祖先及目标默认冷却 50 日。来源：`SimulationController.Evolution.TryPlanFormalEvolution/HasEvolutionFoodSupport/ConvertToPresetSpecies`、`LandLifeRules.ForecastEvolutionPlantSupport`。

可选 `useCompensatedEvolutionFoodGate=true` 时改走支付后下日预测：先保留 `min(0.5P,0.1Pmax)` 不可支付库存；祖先与食草后代成年覆盖当天／更新均至少 0.95，后代预计净增长当天 `>=−0.001amount`、更新 `>0`。捕食后代改在祖先猎物人数 `1,.85,.70,.55,.40,.30` 倍六档中，至少相邻两档满足猎物净更新 `>0`、候选整只捕获 `>0`、成年覆盖 `>=0.95`、后代净增长 `>=0.001amount`。此开关默认关，具体场景是否覆盖未知。来源：`HasCompensatedEvolutionFoodSupport/HasCompensatedPredatorFoodSupport`。

## 3. 玩家操作账与邻地／地形

| 操作 | 实际环境事务与时序 | 对变异的**直接**压力（仅提交成功且当时本格有群） | 对生态的间接变化 |
| --- | --- | --- | --- |
| 植物存量 ±1/±2 档 | 当前 `P←clamp(P×(1±0.25/0.5),0,Pmax)`，立即、无恢复；水域按连通整片总库存计算再均分 | `+`：体型 `+3`、繁殖 `+1.5`；`−`：体型 `−3`、运动 `+1.5` | 改当日可食预算和以后库存，不直接改 `R` |
| 植物日增量 ±1/±2 档 | `R目标=clamp(R当前±25,000/50,000,0,150,000)`；陆地 25 日线性进入、150 日维持、100 日返回；水域整片即时改 `R`、维持 50 日、50 日返回 | `+`：繁殖 `+3`、运动 `−1.5`；`−`：繁殖 `−3`、体型 `−1.5` | 改后续日增长、可持续供给及 K；不立即充值库存 |
| 温度 ±1/±2 档 | 目标 `clamp(提交时生境默认T±25/50,0,100)`；陆地单格 25／150／100 日 | `+`：体型 `−1.5`；`−`：体型 `+1.5` | 当日实际 `T` 改 `F`、寒冷死亡、气候候选；无“直接设适温” |
| 湿度 ±1/±2 档 | 目标 `clamp(提交时生境默认H±25/50,0,100)`；陆地单格 25／150／100 日 | `+`：繁殖 `+1.5`；`−`：体型 `+1.5` | 当日实际 `H` 改 `F` 和气候候选；无“直接设适湿” |
| 海拔 ±1 | 合法范围 0～2，即时、无自动回滚；水域对整片连通水体同步 | 无 `RecordPlayerTechnology` 性状压力 | 可能重分类生境、改变后续 `R` 默认回归目标、邻地坡度与断连资格；库存不因改高立即凭空变化 |

上表 `±1/±2` 是工具档位，不是性状点数。温湿和日增量轨迹按 `实际值=start+(target−start)×已过日数/时长` 推进；提前撤销从当前实际值进入返回段。温湿回归提交前基线，若生境已经变更则转向当前生境默认；日增量陆地同理，水域转向水域默认 `75,000`。同格温度、湿度、增量同时最多两个**活跃**槽，新事务会让最早的活跃事务进入返回。存量与海拔无需槽位。来源：`EnvironmentTechnologyCatalog`、`EnvironmentTechnologyController.Preview/TryDeploy`、`EnvironmentRules`、`EnvironmentWorld.TrySetClimate/TrySetRecoveryBatch/AdvanceOneDay/EnforceSlots`、`SimulationEnvironmentController.TryApply/TryApplyWater`。

邻地建立不是一项“邻地吸引”科技，也不给群体直接压力。地图拓扑要求四向共享边且互惠；新增邻地、地块温湿／库存／恢复量改变、海拔改变、居民变化，都能改下一次只读邻地反事实、运动候选资格及自然迁徙概率。陆地自然迁徙默认每 7 日检查，`N<95` 标准人数 0；`95≤N<1000` 为整数 `(N+5)/10`；`N>=1000` 为 100。平地须 `m>30`，一级坡 `m>60`，高差≥2 禁止；还受冷却（默认 7 日）、刚迁出来源、目的地席位（满 5 群但同种可并入）、同日反向同种及同日重复计划限制。基础概率：

```text
attraction=clamp01[目标空闲食物/(目标空闲食物+迁入人数·A)
                   ·max(0,1−0.2|T源−T目标|/100−0.2|H源−H目标|/100)
                   ·目标F]
chance=clamp01(attraction·m/100·目标容量修正·(目标已有4群?0.5:1)
               ·(当前V2且食草?clamp01(3D):1))
```

目标容量修正 `clamp01(1−目标已有同种人数/目标估计容量)`；当前 V2 开启时，食草还须通过与运动诱导相同的 `Δ>0.002`、目标长期增长 `>=−0.01` 预测；捕食者保留原物理迁徙支路。候选按概率降序逐个抽签，成功才移动实际只数。`enableV62Mobility` 若另行显式启用则改用其目标可支付人数、食物／高差修正和归一化收益支路；当前初值关闭。来源：`SimulationController.Migration.RunMigration/TryPlanMove/NeighborAttraction/TargetCapacityCorrection`、`LandMovementOpportunity.StandardMigrationAmount`、`NeighborMovementPolicyV2`。环境操作只通过已生效的环境值或上述 7 日影响改变生态／变异，不能把二者混为同一压力。

## 4. 历史修改与原因（仅有现成证据者）

| 机制 | 以前怎样 | 现在怎样 | 修改原因 | 证据 |
| --- | --- | --- | --- | --- |
| 植物与出生付款 | 旧账本以密度系数压出生，且未把新生身体物质列为完整付款 | 当前先全群成年分食、再用剩余能量付 `Y+M`，无当日密度出生乘数 | V6.1 账本记录为避免未付出生与共享资源不一致 | `Artifacts/V61Integration/Source/DARWIN_V61_玩法规则与生态计算账本.md`；`LandLifeRules.Simulate/AllocatePlants`；提交 `8b19aca3` |
| 草食层预算 | 旧实现／文档把小、大植物层作为 70/30 优先预算 | 默认统一库存加权分配；优先预算仅在开关开启时走 | 现成 V6.1 账本明确要求单一库存并保留可回滚开关 | 同上；`LandLifeRules.AllocatePlants` |
| 捕食补偿与保护 | 默认无猎物密度反馈／动态猎物再生配额 | 两者代码存在但默认仍关闭；开启时分别乘密度因子、限制实际猎杀 | 现成注释记为防稀少猎物持续被捕与保护可再生更新；尚不能推断普通场景已启用 | `LandRuleTuning` 注释；`LandLifeRules.PreyDensityFactor/RenewablePreyHarvestQuota`；`当前模拟系统_完整规则与JSONL测试手册.md` |
| 正式进化食物保护 | 旧门槛按当天与恢复食物比例 | 保留默认路径，新增支付后预测和祖先／后代净增长补偿门槛，开关默认关闭 | 防止进化付款后新群或祖先缺食；更早历史原因未知 | `SimulationController.Evolution.HasEvolutionFoodSupport/HasCompensatedEvolutionFoodSupport`；`LandRuleTuning` 注释 |
| 同种本地分化 | 曾有 5% 分支、定向套模板、50 日锁定和合并 | 当前 `ReadSplitGate` 固定退休；只有正式目标物种转化，约 10% 且真实付物质 | V6.1 规则账本明确取消普通分化捷径 | `Artifacts/V61Integration/Source/DARWIN_V61_玩法规则与生态计算账本.md`；`SimulationController.LandEvents.ReadSplitGate`；`SimulationController.Evolution.ConvertToPresetSpecies` |
| 变异候选与目标保护 | 旧账本描述最高收益胜出和进度累积；更早定向分支有锁期 | 当前气候、自然、玩家、邻地候选共用按权重的一次抽签；目标 ±5 保护及衰退 FIFO 释放 | 多压力共享每日唯一变异机会；目标保护避免已达标策略立即跨出，衰退时允许解除 | `当前陆地种群生死与变异计算账本.md`（旧口径）；`SimulationController.LandTraits.EvolveCommunityNewRules`；`SimulationController.Influence.ReleaseTargetProtectionFIFO`；`Artifacts/V61Integration/Source/DARWIN_V61_玩法规则与生态计算账本.md` |
| 邻地运动概率门槛 | 旧邻地吸引或早期机会加权容易把运动推到很高，且可能迁往无正增长优势的地块 | 当前食草 V2 用 `Δ>0.002`、资格概率上限 0.25、运动达物理门槛后停止邻地加分；迁徙也核对同一反事实，限制同日同种反向计划 | 现成接入报告记录“运动过冲”和旧迁徙可向非正收益地移动 | `Artifacts/20261018_movement_v2/RESULTS.md`；`NeighborMovementOpportunity.Evaluate`；`NeighborMovementPolicyV2`；`SimulationController.NeighborOpportunity`、`SimulationController.Migration.TryPlanMove` |
| 同种邻地迁入 | 曾见目标已有同种就跳过，另有历史版本没有目标容量修正 | 当前可到已有同种群并合并，概率乘容量修正；同日计划和反向对流另拦截 | 当时鸡已迁出一次后无法再次向同一目标并群；后续限制防同日对流 | `Artifacts/V05FiveSpecies_20261016/PARAMETER_CHANGELOG.md`；`SimulationController.Migration.TryPlanMove` |
| 温湿、增量恢复及地形 | 旧文档有 150 日回归或旧环境按钮口径 | 当前陆地进入 25／维持 150／回归 100；海拔即时，邻地吸引无按钮 | 历史原因未知 | `EnvironmentRules` 常量；`EnvironmentWorld.AdvanceOneDay`；`EnvironmentTechnologyCatalog.All` |

## 5. 需要标记的代码口径差异

| 项目 | 当前可确认事实 |
| --- | --- |
| 旧文档与现行实现 | 2026-10-15《当前陆地种群生死与变异计算账本》的密度出生系数、分化、最高分变异／累积进度、150 日温湿回归，均不适合作为当前日账公式。2026-10-17 测试手册的“邻地开关默认关闭”也与当前源码初值冲突。 |
| 预测与真实日账 | 变异 `ForecastLandGrowth` 以一日连续值比较，真实出生需完整 `Y+M` 付款且捕猎有整只与储备；因此预测收益不能当成次日净增长保证。 |
| 参数存在但不参与默认路径 | `densityBirthDampingStart`、`competitionPressure` 等仍可序列化，但不能据字段存在就认定它们在默认植物／出生路径生效；旧 `mutationSelectionProgress` 字段被每日清空。 |
| 运行时不确定 | 未读取运行中的场景状态、日志或存档；正式物种模板与食物边依资产及当次载入图，具体群体 `N/t/h/m/s/f`、实际开关、生态参数若被实时覆盖，须以该次运行记录为准。 |
