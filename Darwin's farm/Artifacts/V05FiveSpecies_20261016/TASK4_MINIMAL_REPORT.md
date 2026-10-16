# 《达尔文农场》v0.5 最小空间测试 Task 4

日期：2026-10-16。真实 Unity 6000.2.9f1 M 层，seed=240；统一为 3×3 坐标 `−1..1`、初始 1 块、最多另放 4 块陆地。输入是 `seed+actions[]`，同日数组顺序不变，操作在指定日结算后执行。两条正式空间输入各只放了 1 块新地，所有 17/20 项操作分别全部被真实玩家工具接受。没有开发新迁徙系统。

## 结果

| 测试 | 状态 | 事件与断言 |
| --- | --- | --- |
| C1 放置与连通 | **PASS** | 第 800 日在 `(1,0)` 相邻合法放苔原，高差 2；同日真实 T+2/H+2，第 825 日目标温湿度已为 75/75，至第 839 日无迁徙。第 840 日真实海拔 −1，高差变 1；第 847 日鸡从原地迁 100 只到新地。后续第 861/882/903 日又发生同种群合并。 |
| C2 自然迁徙、合并与容量 | **PASS** | 第 800 日在 `(1,0)` 放草原，第 826 日鸡 1552→1452、目标 0→100；第 868 日源 1608→1508、目标 130→230，`outcome=Merged`，居民 ID 保留。第 867 日目标居民 129、其当日 K≈269.73，既有容量修正 `clamp01(1−N/K)` 约 0.522；修复后的自然计划确实乘它，而非绕过。第 826→868 日间隔 42 日，满足 7 日冷却及 7 日一检，不出现新来者当天连跳。源/目标数量转移均严格 ±100。逐日总物质账本在每次 Unity 日结后核查 `after ≤ before + 当日植物增长 + 浮点容差`，完整运行没有触发无偿增物质异常。`PopulationTransfer.Split/Merge` 对 `bodyBiomass` 与 `energyReserve` 按比例拆分、相加，未修改；事件日志目前只给数量，没有身体物质的事件前后双快照，因此不把单个事件的浮点精确差值伪称为已单独测量。 |
| C3 扩地对培育的作用 | **PASS（比较完成）** | 同 seed、同前 16 项操作，第 800 日唯一差异是放不放草原。无新增地时第 950 日灵感菇狸 55 只、运动 47、体型 24、鹿目标超容差距离 81；新增地时原地 14 只、运动 53、体型 25、距离 74，新地另有 15 只、距离 82，总数 29。空间增加了运动选择信号，却分流猎物、降低菇狸总量，尚不足以帮助持续鹿路线。 |

距离使用 `Σ max(0, |当前五 Bar−目标五 Bar|−5)`，所以 0 才表示全五项进入正式容差。C3 的基线输入为 [`task3-mushroom-stable-1000d.json`](task3-mushroom-stable-1000d.json)，对应三次固定 Unity 输出见 [`nextstage-fixed-validation.json`](nextstage-fixed-validation.json)；扩地输入为 [`task4-c2-grass.json`](task4-c2-grass.json)。两者完整前缀在第 800 日前相同，逐日模型/环境哈希在操作分支前一致。彩鸡的额外一次探索 [`next-peacock-cool-sparse-space.json`](next-peacock-cool-sparse-space.json) 表明草原彩鸡虽到第 1000 日有 1301 只，但运动 57、体型 11、繁殖 71，离孔雀模板更远，且目标地无鸡猎物；这是单次探索，不是三次正式验收，也不据此提高运动奖励。

## 必要的 M 层差异修复

原有 `SimulationController.Migration.cs` 自然候选路径对已有同种居民直接跳过，导致已有的 `PopulationTransfer.Merge` 在自然迁徙中无法触发；同时候选机会没有使用已存在的 `TargetCapacityCorrection`。只修正这两处：仍阻止**同一天未落地的重复计划**，允许向已有居民迁入并合并；迁徙机会乘目标容量修正。迁徙数量、7 日冷却、高差和运动门槛、物质转移实现均沿用原 M。修复前草原到第 950 日没有鸡合并；修复后第 868 日真实出现。源文件和改前备份分别是 `Assets/Scripts/Simulation/SimulationController.Migration.cs`、`checkpoint-next-stage-before/SimulationController.Migration.cs`。回放器 `V048ScenarioReplay.cs` 只接入合法放地和海拔操作、空间事件记录、海拔环境哈希与 K 观测。

## 三次独立回放证据

下面每条从第 0 日运行三次独立 Unity 进程到第 950 日；组内完整 JSONL **逐字节相同**，逐日模型/环境双哈希和所有操作/事件因此相同。`nextstage-final-validation.json` 记录每个运行的 SHA256、代码/资源绑定指纹、完成和操作接受断言。JSONL 包含逐日记录、逐项操作行和事件行；同名 `.log` 是原始 Unity 编辑器日志。

| 测试 | 输入 | 逐日输出与原始日志 | 三次相同的 SHA256 |
| --- | --- | --- | --- |
| C1 | [`task4-c1-slope-climate.json`](task4-c1-slope-climate.json) | [`run1`](task4-c1-slope-climate-final-run1.jsonl) / [`log1`](task4-c1-slope-climate-final-run1.log)；[`run2`](task4-c1-slope-climate-final-run2.jsonl) / [`log2`](task4-c1-slope-climate-final-run2.log)；[`run3`](task4-c1-slope-climate-final-run3.jsonl) / [`log3`](task4-c1-slope-climate-final-run3.log) | `ec58aae56c284e60bb225619225e37d405fd998d4fd7bb9413a2181799a72c20` |
| C2/C3 扩地 | [`task4-c2-grass.json`](task4-c2-grass.json) | [`run1`](task4-c2-grass-final-run1.jsonl) / [`log1`](task4-c2-grass-final-run1.log)；[`run2`](task4-c2-grass-final-run2.jsonl) / [`log2`](task4-c2-grass-final-run2.log)；[`run3`](task4-c2-grass-final-run3.jsonl) / [`log3`](task4-c2-grass-final-run3.log) | `83a1b3b4deb441822cd77e61d95f82d2e1f47de6da4063fa2209ad11ca0d23c5` |

曾尝试运行工程里已有的 `PopulationTransitionRegressionChecks.RunBatch`；其中迁徙的创建、拆分、合并断言运行到下一段之前，但该编辑器检查在“正式进化应创建目标”的旧 fixture 处退出 1。该 fixture 没有布置如今所需的正式食物网/猎物，不能作为当前进化规则的有效失败证据；日志保留于 [`population-transition-regression-nextstage.log`](population-transition-regression-nextstage.log)。C1/C2 的正式结论来自以上真实日结和事件回放，未把这个退出 1 说成通过。

## 检查点与边界

完整状态恢复当前 **BLOCKED**，见 [`CHECKPOINT_FEASIBILITY.md`](CHECKPOINT_FEASIBILITY.md)；C1/C2/C3 共用已核实的输入前缀与证据，但各分支均从第 0 日完整计算。缓存命中 **0**，节省模拟日数 **0**，完整回放对照为各自三次逐字节一致；检查点恢复对照 **NOT_RUN**。只测试两块地，**没有**宣称五块上限内所有拓扑或五物种联合规划均已覆盖。
