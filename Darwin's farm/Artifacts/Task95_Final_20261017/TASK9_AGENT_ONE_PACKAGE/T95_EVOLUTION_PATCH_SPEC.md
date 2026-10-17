# 《达尔文农场》T9.5 最终修复候选 · ChatGPT 离线研究版

> **边界**：本包在 ChatGPT 环境中用用户的 Task9、T95A/B/E 实测导出建立了离线参考模型及敏感性试验；**未修改或执行用户本机当前 Unity 代码**，不宣称正式 Unity PASS。所附 Unity V05 旧源码只是历史参考，T9 主工程真实源码须由 Agent 定位。

## 结论

- Task 9.5-E 的真实 Unity 四场景 5000d：单鸡4028，鸡鹿2819/581，鸡菇狸1040/429，三物种1040/1053/428，稳定候选 A+B+C+D 无需动。
- 彩鸡 Day128：真实分离361只，植物材料付款33336.0156；真实 Unity 到Day228仍有彩鸡424只（原361只）；旧 food gate 今日支持率0.4716，因此拒绝。**旧 gate 将预计全额潜在出生资源计为刚需，忽略实际付款出生**。
- 菇狸 Day425：实际鸡5738，当前性状运动30/体型15/繁殖60、五性状达标。植物S100000、R113000。预测捕食者的食物支持以猎物当前净补充为下界，可能忽略猎物受捕食释放密度竞争后增加的补充；**尚无捕食者 gate 数值探针，因此不能把哪条子判定失败写成事实**。
- 正式进化还有一次性物质支付风险，特别是体型由当前鸡转到体型20的菇狸。需要限制可动用植物库存，同时保持物质守恒。

## 提议 P1：仅限正式进化的资源安全付款

保留 `TryPlanFormalEvolution` 的最多源群约10%分离，仍从真实植物库存支付目标与来源活体的身体物质差额。仅将可付款余额从 `S` 改为 `spendable=max(0,S-reserveFloor)`，其中

`reserveFloor = min(0.5*S, plantCapacity*minimumPlantReserveFraction)`。

成本 `extraPerHead=max(0,BodyMass(target.size)−source.bodyBiomass/source.N)`；`founders=min(requested,floor(spendable/extraPerHead))`；若每只无需额外身体物质则不限制；保留原来至少1只及其它合法性判定。**不改变物种大小/真实来源体物质量，不能空凭名义 size 计算母群身体账本。**

该规则在彩鸡 Day128 `S=100000`，付款33336低于可支出50000，因此不缩小361只；在菇狸 Day425，假设母群实际单体身体量在350至600区间，则付额外200~450/只，保留50000库存使创始群上限约111~250只。**菇狸来源身体量尚未导出，不能把这个区间当做 Unity 的真实创始数。**

## 提议 P2：食草型正式进化的可持续食物判定

保留图谱/目标存在/性状±5/冷却/人口槽位/连续资格条件；仅替换旧 `today / Demand(full potential births) >=.9` 和 `renewal / Demand(full potential births) >=.8` 的判定含义。必须在**付款后的虚拟状态**评估：母群人数=N−founders，孩子群=founders且性状为目标模板，S' = S−plantPayment，物种其余群不变，预算由**同一 A+B+C+D / LandLifeRules 优先分配器**求出。

采用成人先行、幼体需整只付款及真实出生压力：

```
Adult = n * AdultNeed(size,movement)
q = clamp01(availableRealFood / Adult)
opportunity = n * birthRate * fertility/100 * fitness * q
paidBirths = min(floor(birthFraction+opportunity), floor(max(0,availableRealFood-Adult)/NewbornCost))
BaseDeaths = NaturalAdultDeaths + ClimateDeaths + ColdDeaths + StarvationDeaths
BirthStressDeaths = paidBirths * reproductionStressPerBirth * fertility/100 * YoungNeed/size
net = paidBirths - BaseDeaths - BirthStressDeaths - expectedLegalPredationDeaths
```

**本轮固定门槛候选（不让 Agent 自己调）**：先根据付款后 S' 预测首日 `foodNow`；再按固定 R、同样的其它群与源群人数，使用真实 70/30 分配器预测纯可再生 `foodRenewal`。对两种 food 分别用上式算 `netNow`,`netRenewal`。

```text
adultSatisfactionNow >= 0.95
adultSatisfactionRenewal >= 0.95
netNow >= −0.001 × founders        # 允许一次性物质付款后的轻微暂时跌落
netRenewal >= 0.005 × founders   # 再生期必须能补充死亡，有0.5%每日净增余量
```

当前 Python Day128 使用真实 Unity 的 food=8650.37、付款后植物预算缩放0.823，对应 `netNow≈+0.33`、`netRenewal≈+1.93`；分别满足−0.361和+1.805，因此这个**近似**可放行与 Unity 100日存活相符。把恢复及份额削减一半会拒绝。**这不是生产 allocator 在付款后状态上的精确输出，Agent 必须用真实共享分配器算出数值，不能照抄比例。**如果用真实分配器得到的值不符，则标记 FAIL、报告实际数值而不私调。并核对源母群成年支持，防止新子群挤死母群。

此门槛是有意选择的低成本单日+再生两种情景；它不保证灾害后永久存活，只负责过滤分化瞬间的明显不可持续情况。

**实际长跑并不承诺进化后永久不灭绝**。门槛仅避免初始显然不可持续，长期变化/灾害可正常灭绝。

## 提议 P3：捕食型正式进化的猎物补偿支持

旧检查 `AvailableSustainablePreyFood` 取过去 `preyNetRenewalHistory`，在原群高密度接近零增长时可能是低估。**仅正式转换资格**引入补偿性反事实：

1. 候选新肉食群必须在合法 `FoodWeb.CanEat` 上有可食猎物；获取食物必须完全遵循当前 T9 `F=N²/(N²+H²)`、`R_ref` 及真实整只猎获；不能凭空给猎物或捕食者记食物。
2. 对猎物密度以固定采样 `N_trial=N_afterTransfer×{1,0.85,0.70,0.55,0.40,0.30}` 预测植食猎物在共用植物预算下的**实际付费出生−非捕食死亡**，以及不同密度下候选猎手捕获能力；所有仍存活的合法猎手必须共享同一 `Q= max(0,paidBirths−nonPredatorDeaths)`，不可各取全额。其它竞争种群保持不变。
3. 对每个 trial，设可持续猎物能量=`min(max(0,Q−existingHunterClaims), legalCaptureCapacity) × PreyEnergy`，按已付款出生模型预测候选 predator net。**固定试验门槛**：来源猎物在创始付款后的当前植物预算下成年满足率≥0.95；六个密度档 `{1,.85,.70,.55,.40,.30}` 中至少**相邻两档**同时满足 `猎物Q>0`、候选猎手成年满足率≥0.95、猎手 `net>=0.001×founders`、捕获可行并共享猎物补给。若样本间存在危险的高密度捕杀率，必须用反事实短回放证实不会在中途将猎物杀光。不能只凭一个低密度乐观场景放行。
4. 如计算量或复用当前统一捕食账本的难度超限，可改用**独立隔离副本上的30～100日无玩家操作生态预演**作为最终可行性筛选；生产模型自身不可写入预测结果，也不能修改随机种子、原始 BlockInfo、事件与物种资料。性能不可接受则保持实验开关 OFF 而非交付未验的预测。
5. 不修改 `K_food` 只读值、不更改默认生态工具/捕食模型、不对高营养级猎物套用陆地植物 `R_ref`。无法构成合法反事实时应明确拒绝/标记不支持，不放水。

## 已执行离线自检及其边界

- 实际彩鸡实机影子转化 361→424/100日，出生/死亡并非模糊生存假设。
- C 的二维植食代理采用一样的 adultNeed×0.4、babyCost、70/30优先分配、整数幼体付款、死亡余量，**第一天能重现已观察彩鸡出生5、死亡4**；100日代理仍活377（Unity 424），差别说明它不是 Unity 同构。把 R 固定削减50%，100日仅约238，分支可识别资源危险。
- B1 支付33336后，下一日植物预算按纯账本约84932，较付款前103200下降17.7%；先算付款前资源再放行属于错误时序。
- 猎食代理加入密度反馈/捕食资源/已付款出生后，在500day里测试了 sourceBody350～600、保留半数初始植物库存、R维持80k～130k 的情形；观察到某些参数下数百日双群尚存，但出现**剧烈的捕食-猎物振荡**；这只能筛选候选，不是 Unity 成功证明。
- 纯函数及代理负控：0食物、0生育、低fitness、今日充裕续航0、0猎物、0生产、0捕食者、超过身体付款可用额、支付后预算下降、当前/再生双阈值与负R压力等共26项全通过。

## 一次性 Unity 验收门槛（Agent 直接实现与执行，不再做前置取证轮）

- 旧开关 OFF：Task9 Stage0 双哈希所允许生态字段逐条相同，旧存档未测试不得假称通过。
- 实验 ON：鸡→彩鸡在无经济限制真实输入中经**正常五性状/5天资格/正式转换**而出现，不通过 Editor 影子特权或直接生成。
- 实验 ON：鸡→灵感菇狸按 Task95E C1 的分段工具输入，第550日前应能正常触发正式转换；如果正常可达性卡在新规则，记录数值阻塞，不能私调。
- 两者在转化后继续500日：母群与新群记录出生付款、捕食/非捕食死亡、储备、材料残差；允许真实生态风险，但不可系统性“次日即灭绝”。
- 反例：库存不足、低R/低猎物密度、缺合法猎物、预期明显净衰退、重复本地目标、目标满槽，不准非法通过。保留真实支持金字塔的三物种固定性状夹具。
- 任何源 BlockInfo 或随机结果因只读预测而改变、身体物质未付款、同一猎物被复用领取、A+B+C+D失效，均回滚实验分支。若两个主要目标仍失败，提交全套输出与精准阻塞点，停止；不自行研发第二版本。

