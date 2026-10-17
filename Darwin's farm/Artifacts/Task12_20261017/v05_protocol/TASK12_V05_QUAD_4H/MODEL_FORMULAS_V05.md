# 生态计算公式基线 v0.5 — 给 Task12 Agent 的审计与建模参考

> **重要**：以下分为 A **可在随包源码摘录核查的既有计算**；B **正式策划冻结的数学约束**；C **尚需 Agent 研究验证的公式候选**。项目真实源文件/参数序列化是最终技术依据，但不能反过来覆盖用户明确冻结的规则。本文**不是宣称已实现并验收的一份“新最终公式”**；生态模型仍处于调试阶段。禁把伪公式当标准答案。

## 记号

- `t` 日；`b` 四边形地块；`i` 种群，`N_{i,b,t}` 数量；`S_b` 植物现存量；`R_b` 日实际植被再生；`Smax_b` 植物库存容量。
- `s_i` 体型、`m_i` 运动、`f_i` 繁殖、`T_i*` 适温、`H_i*` 适湿；`T_b,H_b` 实际环境；`F_i` 气候适应度 0–1。
- `A_i` 每只成年生物日维护消耗；`Y_i` 新生单位维护成本；`B_i` 新生需要转入身体的生物量；`Q_i` 当天可用食物；`K_i^renew` 长期可持续容量估计；`K_i^today` 本日食物/需求比所得容量尺度。
- `clamp(x,a,b)` 限幅；`max(ε,x)` 防止除零；所有库存、预算、能量、只数非负，浮点尾差有记录的容差。

## A. 当前随包 `LandLifeRules.cs` 与 `LandRuleTuning.cs` 可核查的实现表达式（不等于所有开关默认都开）

**A1 适应度（按当前文件）**

`F_i = clamp(1 - (abs(T_b-T_i*) + abs(H_b-H_i*))/90, 0, 1)`。

该表达式只计算温湿两项，不包含食物拥挤、猎物稀缺、迁徙成功率。不要把 `F_i` 当完整的生态净增长率。

**A2 成年日需求（按当前文件）**

`A_i = adultMaintenanceScale * (max(0,s_i) + smallBodyMaintenance/max(1,s_i)) * (1 + movementFoodCost*(clamp(m_i,0,100)/100)^2)`。

当前调参默认 `adultMaintenanceScale=1`、`smallBodyMaintenance=200`、`movementFoodCost=0.4`；调参文件/存档可覆写这些值。体型增大通常提高绝对维护成本，极小体型还存在额外反比维护项。

**A3 新生个体付款（按当前文件）**

`Y_i = youngLinearCost*s_i + youngQuadraticCost*s_i²`；`B_i=max(1,s_i)*preyEnergyPerSize`；`CostNewborn=Y_i+B_i`。

当前文件的默认值：`youngLinearCost=0.4`、`youngQuadraticCost=0.02`、`preyEnergyPerSize=40`。`B_i` 是转入新生身体库存的生物量，绝不能当作凭空新增。

**A4 当天出生机会、可支付出生、死亡**

`BirthOpportunity=N_i*birthRate*(f_i/100)*F_i*sat_i`，其中 `birthRate` 现有代码默认0.1，`sat_i=clamp((allocatedBiomass+savedReserve)/(N_i*A_i),0,1)`。

`possibleBirths=floor(birthRemainder + BirthOpportunity)`。

`born=min(possibleBirths, floor(max(0,foodAvailable-adultDemand)/CostNewborn))`。

成年需求优先支付，**未支付的出生不可变成免费后续负债**。当前文件结算后 `birthRemainder` 限在 `[0,0.999]`，并记录 `newbornCostToday`,`birthBudgetToday`,`birthStressDeathsToday`。若 `enablePaidBirthStress=true`，生育成本造成的死亡仅与**真实出生**有关；否则可能走旧式估算压力。需要回归小种群 `N<10` 的整数锁定和无出生死亡压力。

`baseDeaths = starvationDeathRate*N*(1-sat) + ColdDeaths + climateMismatchDeathRate*N*(1-F) + NaturalAdultDeaths`，再加生育压力死亡。每项都需日志独立记录以诊断萎缩；不要凭数值感觉强改。

**A5 植被当日可被领取的共享预算**

当前文件相当于：

`growth=clamp(plantGrowthToday,0,S_afterGrowth)`；`standing=max(0,S_afterGrowth-growth)`；

`floor = R_b>0 ? min(Smax_b*minimumPlantReserveFraction, 0.5*S_afterGrowth) : 0`；

`draw = R_b<=0 ? standing : min(max(0,standing-floor), standing*dailyPlantStockDrawFraction)`；

`FoodBudget=max(0, min(S_afterGrowth-floor, growth+draw))`。

现有默认 `minimumPlantReserveFraction=0.1`, `dailyPlantStockDrawFraction=0.1`。请核对 `R_b` 和 `plantGrowthToday` 的同日顺序、上限及上一日剩余库存，否则会错将已收获量再分配。

**A6 竞争共享预算**

当前代码先算 `adultNeed_i=N_i*A_i`、`birthNeed_i=N_i*birthRate*(f_i/100)*F_i*(Y_i+B_i)`；各组按竞争权重 / 尚未满足需求迭代分配资源。权重包含 `F_i`、采食可达性、运动辅助、按体型区分的虚拟食物通道；先成年维护，再新生付款。大小生态位是**同一物理库存的分配通道，不是两份额外食物**。

**A7 动态 K 的两种量必须区分**

- 经验指标：当前源文件 `carryingCapacity = allocatedBiomass / energyNeed`，这是**当天按实际摄食结果衡量的容量尺度**，受当前可支出的库存影响，不能直接当长期生态承载能力。
- 长期可持续 K：`FoodCarryingCapacity` 对植物取 `supply=min(max(0, habitatRecovery), Smax*(1-min(0.5,reserveFraction)))`，固定真实竞争种群，在不同“目标种待测数量 n”下调用现有共享分配器；以二分法寻找满足 `allocatedAdultFood_i(n) >= n*A_i` 的最大 n。此估算既含竞争，又不把短期存量当无限日常供给。
- 捕食者 `PredatorFoodCarryingCapacity` 用历史猎物“出生-非捕食死亡”的净补充、真实可捕猎范围、捕食者相互分食来二分测试成年需求。现有历史窗口需核对，猎物为零时不得维持不合理正 K。

**A8 当前可选的 L0 猎物更新配额（实验性）**

源文件 `RenewablePreyHarvestQuota` 使用如下形式：

`g = RenewableUnhuntedPreyGrowth(prey, N)`；`N_eq = TryRenewablePreyEquilibrium(...)`；

`buffer=(N - refugeFraction*N_eq)/max(10,adjustmentDays)`（无有效平衡则0）；

`Q_hunt=floor(clamp(max(0,g+buffer),0,N))`。

可选参数默认 `refugeFraction=0.35`,`adjustmentDays=45`，开关 `enableDynamicPreyRenewalQuota=false`（源文件中的默认值）。**注意：这个 Q 是猎物每日可被消耗的数量配额，不是环境 K，也不是物种真实猎食次数。** 旧验收只证明已启用实验场景中 L0 猎物实际捕食未超出配额；尚未完全证明长期稳定、捕食 L1、所有物种与迁徙耦合。

**A9 可观测与模拟时间**

- `birthRate=0.1` 属于当前 `LandRuleTuning` 源码表；早期 `reproductionScale=0.05` 是另版模拟器参数，不能混算。
- `FoodCarryingCapacity` 的返回与 `EnvironmentalCarryingCapacity` 只有当相应开关/路径被实际调用才影响选择压力；务必收集运行时配置，不得把代码中存在的函数当成已经启用。
- 源文件 `trophicLevel==0` 是该版本“草食种群”的代码表示，与策划“植物 L0 / 食草 L1 / 捕食 L2”编号不同；映射要以 `FoodWeb.CanEat` 和现存对象为准，别整套错位。

## B. 冻结的数学/因果约束（所有实现都必须满足）

1. `SumFoodAllocated <= PlantFoodBudget`；植物大小生态位只分配一份真实存量；吃饱退出再分配的权重方案不能导致次序偏差或总预算溢出。
2. 成年维护先算，真实生物量余量才支付出生；`born>0` 必须有 `born*CostNewborn` 从合法来源支出与转账；`born=0` 时不得凭空产生等额生育压力死亡；整数余数不能放大生育量。
3. 捕食转移 `preyCount`、猎物身体物质、猎手获得能量及死亡回收均可对账；猎物稀缺时实际捕食能力/份额降低，捕食者持续 K 应下降，而不是继续固定猎物比例。
4. 竞争者/捕食者/迁入改变，动态 `K^renew` 必须重新估计；`K` **不硬截断数量**；允许 `N>K` 造成后续自然负反馈、迁出或变化，但不应瞬间删去溢出群体。
5. 新目标正式进化数量以当前母群为基础按 **10%** 计算，不套迁徙100只上限；实际物质不足缩减正式子群数量，不能用测试直接新建子群绕过支付。
6. 玩家温湿/增量操作在25/150/100日进入/维持/恢复，随时可撤销；撤销只切断未来作用，不反向重写过去；短期收益随撤销与后续回落逐渐消退，不瞬间 reset Bar。
7. 任何实装允许 2–4 四边形地块完成迁徙基本单测，不准隐性依赖六角布局或固定7块、固定20次事件。

## C. 允许自主探索但尚未被“全面确认”的公式设计空间

仅在代码证据证明 A 类模型有缺陷时，Agent 可以离线比较 **最多三个**候选，再最小化接入，并回归 A–D。参考候选（非已冻结）：

- **共享植物可持续供给**：以日再生资源 `R_eff` 为唯一长期饲养容量，使用当前消费分配器求 `K_i = max{n | AdultAllocation_i(n; competitorCounts, R_eff) >= n*A_i}`；有竞争者迁入后重新解出 K，不增加物理库存。与当前 A7 相近，重点查是否实际启用、是否漏掉预测捕食和迁移。
- **猎物更新型捕食上限**：先估算当前猎物日新增、自然死亡和条件平衡；捕食消耗受真实更新与谨慎利用超量库存约束，而非“捕食者需要多少就永远抓到多少”。当前 A8 为一个实验候选，不能默认打开就声称通过。
- **小群出生与死亡解耦**：先计算真实可支付整数出生，再依据真实出生计算生育压力；同时给连续率/余数一个不会产生免费出生或永不生育的处理策略。每一改动需测 N=1,2,9,10 和0食物边界。

## D. 最低公式测试清单

1. `F` 在边界0/100与不同海拔合法环境正常；分母不为0，适应度在[0,1]。
2. `N=0,1,9,10,100,1000` 下出生、死亡、余数合理且不凭空生成身体物质。
3. 空植物、只有存量无再生、只有再生无存量、接近满库存、突然加入竞争种时 `FoodBudget`、`K^renew`、当天分配账自洽。
4. 猎物由大量下降至10、1、0时，捕食者可捕食量及可持续 K 能随之下降；即使猎手需求很大，不能杀超过真实猎物。
5. 迁入同时影响源与目的地食物份额/K；同一天分解 migration / born / died / predation / evolved 后总量对账。
6. `Cancel` 在第10、50、200日各执行一次对照，环境按当时值恢复；没有撤销时遵循25/150/100天；性状变化不是即刻撤回。
7. 相同 seed + 操作/撤销/迁徙策略复跑同结论；不同 seed 只变随机源，不改变算法或策略条件。

## 源码核对范围与注意

相关重建文件可在原始验收代码差异中找到：`Assets/Scripts/Simulation/LandLifeRules.cs`、`LandRuleTuning.cs`、旧 `V048ScenarioReplay.cs`。本包附有供只读参考的 `reference_code/`，它不是新修改补丁，具体行号以 Agent 当前 Unity 工程为准。以前的日志 `PASS` 只能证明被执行的那一段机制通过，不能直接推导新的五物种+撤销+四边形综合验收通过。
