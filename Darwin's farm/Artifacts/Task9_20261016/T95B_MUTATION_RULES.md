# Task 9.5-B · 菇狸路线中的运动 ±1 变异取证

**范围：** 只解释当前源码的自然变异评分，并读取 Task 9.5-A 已有的**关闭费用** Unity 数据；没有重新运行 400–500 日回放，也没有修改运动规则。这里评分对象仍是来源种群**鸡**，因为菇狸在该路线中没有出现。

## 1. 实际入口与候选权重

`Assets/Scripts/Simulation/SimulationController.LandTraits.cs:235-355` 的 `EvolveCommunityNewRules` 每 `mutationInterval=1` 日检查一次。运动是 trait `2`；对 `m−1` 和 `m+1` 分别检查，限制在 `[0,100]`。`mutationEnabled=false` 或 `mutationStep≤0` 时整体不运行。本回放二者为 `true`、`1`，`selectionStrength=1`。

设当前 `N`、环境容量 `K=LandLifeRules.EnvironmentalCarryingCapacity`，`r=N/K`（若 K≤0，源码令 r=1）。自然候选先通过以下筛选：

```text
p_natural = 0.5 − 0.495 × clamp01(r / 0.95)
Δg± = [ForecastLandGrowth(m±1) − ForecastLandGrowth(m)] / max(1,N)
候选成立 ⇔ 下一档有效，未离开受保护目标区间，
             残余玩家影响 InfluenceNet(trait=2,residual=true)×方向 ≥ 0，
             Δg± > mutationGainThreshold(0.00002)，
             ModelRandom01("mutation.natural",day,tile,population,trait×2+方向位) < p_natural
w_natural± = clamp(Δg± × selectionStrength × 100, 0.05, 5)
```

所以正收益不保证选中：先要过独立随机门，再与其他候选按权重抽选。气候性状 T/H 另有 `w_climate=1+|环境值−当前性状|/20`，会参与同一总权重；运动没有气候候选。详见同文件 `:250-329`。同方向的自然与玩家候选权重由 `AddMutationCandidate` 相加（`:214-232`）；最终用独立 `ModelRandom01("mutation.choose")×Σw` 的累计权重抽取。

玩家定向影响是**另一路候选**，并非自然收益的加分：`net=InfluenceNet(trait=2,residual=false)`；若非零且不离开受保护区间，方向取 `sign(net)`，权重 `w_player=|net|`。`InfluenceNet` 对活跃影响求和 `weight+potential`，残余检查仅求和 `weight`；`|net|<0.00001` 归零（`SimulationController.Influence.cs:71-102`）。是否受目标区间保护由 `LeavesProtectedTarget` 决定（同文件 `:196-211`），本轮旧日志没有它的逐日状态，标为 **UNKNOWN**。

### 当前路线的玩家影响权重

`SimulationController.Influence.cs:8-69`：每次**正档**植物 S 或 R 操作都给运动加一条 `weight=−1.5`；负档给 `+1.5`。影响从操作次日开始，活跃 7 日，另有 5 日残余方向检查。每活跃日 `potential=clamp(potential+0.1×weight,−3,3)`。同日 S、R 两次正档会分别添加一条，因此初始运动活跃净权重至少 `−3`，再计各条 potential 与其它尚有效操作。关闭费用的菇狸路线 30 次操作全部成功；输入在 380、390、400、410、475 日均有正档 S/R 操作。因此这些窗口会产生运动 `−1` 玩家候选，并可能以残余负影响阻止自然 `+1` 候选。逐日精确 `net`、potential、抽签值未记录，均为 **UNKNOWN**。

## 2. `ForecastLandGrowth` 的原始评分

源码 `SimulationController.LandTraits.cs:62-139`：对当前五性状 `v=(T,H,m,s,f)`，

```text
fitness = clamp01(1 − (|block.T−T|+|block.H−H|)/90)
G(v) = births(v) − starvation(v) − cold − climateDeaths(v)
       − naturalAdultDeaths(s) − forecastBirthStress(v) − preyRisk(v)
births(v) = N × birthRate(0.1) × f/100 × fitness × q(v)
starvation(v) = N × starvationDeathRate(0.6) × [1−q(v)]
climateDeaths(v) = N × climateMismatchDeathRate(0.01) × [1−fitness]
cold = coldDeathRate(0.005) × N × max(0,(50−block.T)/50)
naturalAdultDeaths(s) = N × [0.008×20/(20+max(1,s))
                            +0.5/(1+max(1,s))²]
forecastBirthStress(v) = births(v) × 0.8 × f/100
                         × YoungNeed(s)/max(1,s)
```

此处源码预测使用 `ReproductionStressDeaths`（`LandLifeRules.cs:63-79`），不读取实际结算后的付费出生数；这是**评分公式现状**。对该路线的鸡（植食），`q(v)=clamp01(ForecastPlantIntake(v)/[N×AdultNeed(s,m)])`，见 `LandTraits.cs:73-76` 与 `LandLifeRules.cs:475-485`。运动进入两个相反方向的项：

```text
AdultNeed(s,m) = 0.40 × [s+200/max(1,s)] × [1+0.4×(clamp(m,0,100)/100)²]
ForagingAccess(s,m) = clamp01(1−0.08×[20/(20+max(1,s))]
                                /[1+clamp(m,0,100)/10]²)
plantAccessCommon = fitness × ForagingAccess(s,m) × (1+0.002×m)
```

`LandLifeRules.cs:12-20,40-49,260-338` 的同一分配器用 `plantAccessCommon` 乘小／大生态位效率及 70/30 预算，成年先于出生分配。`m+1` 增加成年能耗，也提高觅食可达性与 `1+0.002m` 权重；`m−1` 则反向。两种影响都通过 `q(v)` 进入 `G(v)`，源码没有固定的“运动向上奖励”。若有合法猎手，`preyRisk` 中追逐项 `clamp(1+(hunter.m−m)/40,0.1,1.5)` 使 `m+1` 降低被捕风险、`m−1` 增加风险（`LandTraits.cs:108-125`）。捕食者来源种群还走 `PredatorPotential` 的猎物权重平方和路径（`:127-139`）；这条菇狸路线当时只有鸡，不适用。

## 3. 400–500 日已有 Unity 观测

来自 `T95_EVOLUTION_DIAGNOSTICS.csv` 的关闭费用重跑，每 10 日样本在此摘录；两项植物工具的操作时序与原输入相同。这里的 `adultSatisfaction10=1` 是实际近 10 日成年满足率，**不是** ±1 反事实的 `q(v)`。

| 日 | 鸡 N | 环境 K | N/K | 运动 m | 体型 | 近 10 日满足率 | `p_natural` |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 400 | 5909 | 2713.26 | 2.178 | 6 | 21 | 1.0 | 0.005 |
| 420 | 6177 | 1618.00 | 3.818 | 0 | 25 | 1.0 | 0.005 |
| 440 | 6298 | 1618.00 | 3.892 | 0 | 25 | 1.0 | 0.005 |
| 460 | 6395 | 1618.00 | 3.952 | 0 | 25 | 1.0 | 0.005 |
| 480 | 6464 | 1618.00 | 3.995 | 0 | 25 | 1.0 | 0.005 |
| 500 | 6530 | 1618.00 | 4.036 | 0 | 25 | 1.0 | 0.005 |

这些样本只能证明运动从 6 降到 0、自然候选抽签概率在该区间被 N/K 压至 0.005，以及正档植物工具存在向下玩家影响。每次 `m±1` 的实际 `Δg`、两方向是否进入候选表、目标保护状态、逐日 influence 净值、随机抽签值和候选总权重在现有日志中均为 **UNKNOWN**；不能仅由运动轨迹倒推单次评分。
