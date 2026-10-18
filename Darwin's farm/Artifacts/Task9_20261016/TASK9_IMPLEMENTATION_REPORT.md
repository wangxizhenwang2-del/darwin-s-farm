# Task 9 实施报告（Unity 6000.2.9f1）

## 状态与来源

**已合入主工程，实验配置仍需显式开启。** 在完整隔离工程中改动并测试，八个源码文件合入后与隔离副本逐字节相同。主工程重新启动 Unity 批处理，`runs/main-five-tools.log` 报告 `PASS: Unity five-tools audit`，退出码 0。测试输入、输出、日志及执行元数据见 `inputs/`、`runs/` 和 `EVIDENCE_MANIFEST.json`。Python 脚本只核对 Unity 输出，没有代替生态模拟。

隔离工程：`G:/unity/darwin-s-farm/Task9_Unity_Isolated_20261016`；原始源码备份：`G:/unity/darwin-s-farm/Task9_Baseline_20261016`。原始 2004 个源码文件逐字节校验无差异。未改动 15 种生物基础资产、场景及 27 生境配置。

## 实际源码改动

| 文件 | 作用 |
|---|---|
| `Assets/Scripts/Simulation/LandRuleTuning.cs` | A、B、C 独立开关；D 成年维持倍率；半饱和参数。默认三个开关关闭、倍率 1.0，生育压力旧值 1.2。 |
| `Assets/Scripts/Simulation/LandLifeRules.cs` | 捕食密度反馈、成功付费出生压力、70/30 植物优先账本、统一成年维持倍率、只读捕食者 K_food。 |
| `Assets/Scripts/BioData/BlockInfo.cs` | 保存捕食所需稳定生产力参考值和资源账本观测。 |
| `Assets/Scripts/BioData/PopulationData.cs` | 增加每日付款及压力观测字段。 |
| `Assets/Scripts/map/MapSimulationBridge.cs` | 生境稳定生产力参考值接线。 |
| `Assets/Scripts/Simulation/SimulationController.cs` | 只读捕食者 K 入口及模型哈希保护；沿用正式收割入口。 |
| `Assets/Scripts/Simulation/Editor/V048ScenarioReplay.cs` | 隔离回放器的 V5.1 沙盒、五工具、收割和 K 观测；不参与正式生态公式。 |
| `Assets/Scripts/Simulation/Editor/EnvironmentFiveToolsAudit.cs` | 五工具预览前后模型哈希与生物能比较，执行 Unity 编辑器审计。 |

### A：猎物密度

对有稳定植物生产力参考的植食猎物，用 `F=N²/(N²+H²)` 调整合法猎物的捕食意图，`H=0.58×R_ref/AdultNeed_new`。`N=0` 时无捕食；猎获仍走整只猎物、真实库存和共享捕食账本。菇狸仅能捕鸡与彩鸡。更高营养级猎物暂维持原捕食反馈，未将植物公式强加给肉食猎物。永久地形变化及多地块的 `R_ref` 口径仍需专门设计；目前保存初始生境参考值，短期工具和灾害不改它。

### B：出生结算

出生机会继续保留小数余量；出生只按成年付款后足额支付 `YoungNeed+BodyMass` 的整数只发生。额外生育死亡压力以**实际已付费出生数**计算，零出生为零压力。实验系数 0.8 与旧 1.2 分开验证。灭绝时清空余量；身体物质与猎食储备沿既有账本结转。

### C：植物预算

同一日植物预算分小型优先 70%、大型优先 30%，先结清两赛道成年需求，再跨赛道用剩余真实资源补成年，最后同法分配繁殖资源。共同预算只从 S 扣一次；分配与种群遍历顺序无关。植食 `K_food` 调用同一套分配逻辑作反事实成年付款。

### D：成年维持

`AdultNeed` 单入口应用倍率 0.40；实验参数不改变幼体能耗、身体质量或猎物身体能量。储备上限仍按生效后的成年需求乘原 `reserveDays`，因而随倍率下降；这是同一规则的预期结果。旧模式仍为 1.0。

### 动态 K

捕食者诊断用历史猎物净补充、当日真实猎物数量、密度反馈、合法猎物、其它猎手竞争及自身成年需求，二分搜索可持续整数猎手数。只读开关的 101 条 Unity 模型与环境哈希完全一致。`carryingCapacity`、旧 `environmentalK` 仍保留；没有把新 K 改作正式分化门槛。长期零增长 `K_eff` **NOT TESTED／未实现**。

## 接入边界

此次只合入候选实现，没有修改 WhiteBox 场景起点或切换默认规则。WhiteBox 真实起点与 V5.1 文档冲突，见回归报告。旧模式生态记录逐条一致，但新增序列化调参字段改变了原始 `modelHash` 字节串；环境哈希仍一致。不能将这解释为旧生态轨迹变化。
