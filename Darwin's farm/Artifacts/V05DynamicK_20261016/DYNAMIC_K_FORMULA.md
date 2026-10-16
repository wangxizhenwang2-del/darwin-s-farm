# 动态 K 与共同资源分配

状态：Unity 实验实现，正式验收结果见 POPULATION_TEST_REPORT.md 与 REGRESSION_REPORT.md。此文的算法已进入本阶段 M 层候选；研究包中的 60%、0.035、0.25 及平衡人口均未使用。

## 真实账本与分配

植物仍只有地块的真实库存 S。日初生长是 `min(capacity-S, R)`，当日可取预算继续使用原有库存保留线和 10% 存量提取规则。

小/大植物层作为同一库存的虚拟获取通道。原有 0.7/0.3 仅进入获取效率，不再切成互不竞争的两份物质：

`A_i = fitness_i × ForagingAccess_i × (1+0.002×movement_i) × Σ_r[channelShare_r × NicheEfficiency(size_i,r) × affinity_i,r]`

偏好层 affinity=1，其它层沿用 unspecializedPlantEfficiency=0.7。小植物尺度仍 20，大植物尺度仍 25。所有植物消费者同时参加同一份真实预算的分配。

每阶段的未满足真实需求为 U，权重为 `W_i=A_i×U_i`。同时提交 `min(U_i, remaining×W_i/ΣW)`，达到需求上限后停止领取；剩余量重新分配。先分成年需求 `N×AdultNeed`，再分繁殖资源，两个阶段共用同一个 remaining。每个阶段最多消费者数量加一次迭代；不存在按物种依次扣取的先到先得。

繁殖需求仍为 `N×0.1×fertility/100×fitness×(YoungNeed+BodyMass)`。本阶段保留原有整数出生、完整身体付款和 birthRemainder 上限；小群粒度问题见 BIRTH_ACCOUNTING_DESIGN.md。

日结只从真实 S 扣一次总植物摄食。捕食继续使用原 HuntLevel：整只猎物身体先移除，猎手获得处理效率后的能量，其余入碎屑；猎物死亡计数一次。

## 人口结算

饥饿满足率改为实际成年维持支付/成年需求。不能因为尚未付清假设幼体资源，再把已经吃饱的成年个体判为饥饿。

删除按独立生态位 R 密度再次缩小出生的 GroupBirthFactor / DensityBirthMultiplier。人口仍由实际摄食、完整出生付款、气候与原有自然死亡及繁殖压力结算。没有使用 K_eff 或 K_food 额外扣人口。

原有繁殖压力仍按理论繁殖活动计算；本轮没有降低 1.2 或出生身体成本。没有新增保底资源。

## K_food

植食 K_food 为只读反事实成年维持边界。固定其它种群的数量和性状，令目标数量为 n，使用同一成年优先分配器：

`F_sustainable=min(max(0,R), capacity×(1−min(0.5,minimumPlantReserveFraction)))`

高 S 的一次性库存不计入长期供给。第二个上限保证日库存保留规则下该供给可持续：以 `S_start=min(R,capacity×reserveFraction)` 为余量，日增长和可取预算能维持该通量。

对 n 做整数二分，搜索成年分配不能完全满足的边界。上界为 `ceil(F_sustainable/AdultNeed)+1`。支付比较容许 `max(0.01,成年需求×1e−5)` 的单精度舍入差，避免把浮点末位误差误当缺粮。

JSONL 的 `foodK` 是新估计；`carryingCapacity` 保留旧“当日分配/每只成年成本”的观测口径，明确不能称为长期 K。自然选择当前仍使用旧 environmentalK，待新 K 的正确性与 A/B 通过后再决定接入。捕食者新 K_food 尚未正式验收，输出 -1 表示未提供，不能把旧历史猎物估计冒充新反事实结果。

## K_eff

仅在机制夹具的起点与终点诊断，不进入每天的人口循环。在临时 Unity BlockInfo 中复制本地食物计算所需的T/H/植物容量等字段，并独立序列化复制种群身体、能量、余数及历史，执行真实 LandLifeRules.Simulate 一个日步。它不复制地图操作轨迹、不执行正式分化/迁徙/注销，不能用于恢复真实世界。其它种群初始人数固定，目标按 n 缩放其身体与已有储备；植物放在上述可持续流量对应的库存位置，不使用原场景的高 S 补贴。

`G(n)=当日真实整数出生−自然死亡−捕食死亡`。搜索包含小创始数量和 64 个有界人口采样（搜索上限100000，另包含1/2/5/10/20/50/100的小群数量）；唯一观测到的正转非正区间再二分。结果只标 LOCAL_ONE_DAY_CANDIDATE；多个交点标 UNCERTAIN_MULTIPLE_CROSSINGS，未找到标 NO_STABLE_SOLUTION_IN_BOUND；最近十日平均净变化绝对值超过2%/日时标 UNCERTAIN_CHANGING_COMMUNITY；候选边界反事实日步中任一种群变化超过5%时标 UNCERTAIN_COUNTERFACTUAL_COMMUNITY_CHANGE。这两个阈值仅用于诊断可信度，不改人口。整数结算和动态猎物可能使单日候选不代表长期稳定点，不强填唯一正数。

所有计算操作临时复制，不登记到世界、不推进真实日期、不调用模型随机。调用前后核对 CanonicalModelHash。这里的单日诊断复制不等于完整世界检查点恢复，缓存恢复仍 BLOCKED，命中与节省日数均为 0。

## 版本与回退

改前文件见 checkpoint-before；每次使用的代码、资源、输入 SHA 绑定在真实 Unity JSONL 第一行。数值与公式变化见 PARAMETER_CHANGELOG.md。最终验收必须在同版本独立 Unity 进程三次完整运行；不同版本的轨迹哈希无需相等。

当前正式候选保留原真实捕食算法及原预测中的猎物数量项；新增饱和遭遇实验已回退。捕食者新K_food和可确认的长期K_eff尚未验收，不能把本文件称为已完成全食物链动态K。冻结跨日付款仍需用户确认，Task5整体不判PASS。
