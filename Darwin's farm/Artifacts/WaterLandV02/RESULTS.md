# 水域系统接入 v0.2：执行与验收记录

日期：2026-10-18。隔离分支：`codex/water-land-v02`；固定基线：`e1e94b13900e14b880d5d81d560303aee2407314`；Unity：6000.2.9f1。原工作树开始时干净，未在原工作树编辑。用户指定 30 分钟硬停止；ZIP 中 4 小时建议未采用。

## 结论与等级

- **L0 PASS**：随包 `python -m unittest -v test_model.py`，34/34；另有 C# 离线边界检查 15/15。
- **L1 PASS（编辑器边界夹具）**：Unity 预览场景 25 项检查通过，seed=20261018。覆盖真实 `BlockInfo`、`MapTileInstance`、`SimulationController` 类型与真实鹿→鹿游进化资产边。最关键反例 `w3→A` 返回 `NO_LOCAL_EDGE`；水群迁至 w1 后位置门禁允许 `w1→A`，原目标进度已清空。这里的 `ROUTE_OK` 只表示位置、图边、本地槽位等只读预检通过，**不是正式进化已提交**。
- **L2 BLOCKED**：没有可复用的完整水生摄食、竞争和生态付款闭环。正式陆↔水交易没有接入真实每日结算；生产场景 `enableWaterEcology: 0` 保持关闭。不得把编辑器夹具作为水生实际游戏日通过。
- **L3 NOT_RUN**：未执行从陆地母群培养至跨域进化和长期存活的完整玩家日程。

## 源码接入

1. `Assets/Scripts/Simulation/WaterLandBoundary.cs`：陆水候选只检查母群当前 `BlockInfo` 与目标格的真实双向邻接、正交坐标及高差≤1；不遍历同水域其他水格。
2. `Assets/Scripts/Simulation/HabitatTopology.cs`：水路连通要求双向地图邻接、正交、水路双向开口；同高才合并水域，高差1只保留跨水域通道。原有每格库存记录及聚合方式保留。
3. `Assets/Scripts/Simulation/SimulationController.WaterLand.cs`：增加受水生态开关保护的跨域正式进化**只读路由预检**；校验实际源群、生态位、真实进化图、本地目标唯一性及槽位。尚未扣资源、分化人数或提交事件。
4. `Assets/Scripts/Simulation/SimulationController.Migration.cs`：整群水内换格也清除原目标连续合格记录；原有陆地整群移动行为不改。
5. `Assets/Scripts/Simulation/Editor/WaterLandUnityBoundaryChecks.cs` 与 `Tools/WaterLandBoundaryAudit/`：Unity 编辑器和离线边界夹具。夹具造图/造群仅用于门禁验收，不代表玩家流程。

旧随机跨域转换、河岸散步和普通 5% 分支均未重新接入每日调度；没有改五 Bar、连续天数、10% 比率、冷却或生态数值。

## Unity 测试矩阵

场景夹具：`w1(1,0),w2(2,0),w3(1,1),A(0,0),B(0,1)`，seed=20261018；路由观测 day=0，迁格观测 day=10。水格群体只挂在实际所在 `BlockInfo.community`。`PASS` 限定为说明列写明的编辑器层；其余按完整条款记 `BLOCKED`。

| ID | 状态 | 实际证据与剩余条件 |
| --- | --- | --- |
| T01 | PASS | w3→A `NO_LOCAL_EDGE`；只读检查未改人数和水库存；没有正式交易。 |
| T02 | BLOCKED | w3→w1 真实水内换格成功，w1→A 预检 `ROUTE_OK`；连续达标、10% 正式提交与付款尚无水生实机路径。 |
| T03 | PASS | w3→B 位置与图边预检 `ROUTE_OK`；其余正式条件尚未验收。 |
| T04 | PASS | 斜角、缺反向邻接、高差2均拒绝。 |
| T05 | BLOCKED | 夹具删去一侧水路后分区、群体位置和三格库存和保持；实际工程旋转后的重建未跑。 |
| T06 | BLOCKED | 高差1双开口被识别为跨水域通道且不共享 basin；实际迁徙候选未跑。 |
| T07 | BLOCKED | 高差2通道拒绝；单向开口不合并；完整迁徙拒绝未跑。 |
| T08 | BLOCKED | 每水域多水群每日统一摄食/竞争尚未接入。 |
| T09 | BLOCKED | 来源水域统一余额支付身体差的正式交易尚未接入。 |
| T10 | BLOCKED | 图边非法、本地重复目标能精准拒绝；性状/天数/食物/冷却/付款及失败原子性未跑。 |
| T11 | PASS | 真实 `MoveWithinWaterRegion` 后原 `evolutionConversions` 清空；每日重计尚未跑。 |
| T12 | BLOCKED | 无关水路拆断后 w3→B 仍为 `ROUTE_OK`；拆断后的实际资源日账未跑。 |
| T13 | PASS | 目标物种仅在同水域别格时，本地目标格仍获 `ROUTE_OK`。 |
| T14 | BLOCKED | A→w1 通过、A→w3 拒绝；真实鹿→鹿游资产边已核对；目标水域食物支持未跑。 |
| T15 | BLOCKED | 整群换格的 N、身体和储备守恒；跨水域拆出与同日连跳未跑。 |
| T16 | BLOCKED | 连续工程改变+实际水生日结算+同 seed 双跑哈希未跑。 |
| T17 | BLOCKED | 陆地 V05 沙盒 seed=240、无操作、day 0..100 共101个模型/环境日哈希与固定基线逐日相同；现有生态事件检查通过。该回放只出现初始鸡群，未覆盖 A-D 操作和全部五物种，因此完整 T17 未通过。 |

## 真实回放、首个失败日与复现

- `five-species-100.json`：`Assets/Scenes/V05FiveSpeciesSandbox.unity`，seed=240，`actions=[]`，day 0..100。隔离分支和固定原始提交各跑一次 `V048ScenarioReplay.Run`；两次均 `V048_REPLAY_OK`，所有 101 个日快照的 `modelHash`、`environmentHash` 相同；绑定源码哈希不同属于预期。逐日地块、群体、植物库存、出生/死亡、身体、储备和双哈希见两份 trace。
- `land-origin-100.json`：`Assets/Scenes/WhiteBox.unity`，seed=240，`actions=[]`，day 0..100，实际游戏日跑完；该场景没有活体群，不作为五物种证据。
- 既有 `V43LandRulesAudit.Run` 在改动分支及干净的原始提交上，**首个失败日均为 day 1**：`Day one must add one effective day on a reverse edge.` 复现：Unity 6000.2.9f1，`-batchmode -nographics -executeMethod V43LandRulesAudit.Run -quit`。这不是本分支新增的差异；仍应由原陆地规则负责人排查。
- 水生实际日循环没有启动，故没有可报告的“水生首个失败日”或水生逐日资源/事件/哈希日志。不能用夹具伪造该日志。
- 旧 `Tools/NicheAudit` 离线工程在现有源码下因缺失类型引用不能编译；未改该无关工程。

## 明确阻塞

`SimulationController.SimulateBlock` 在水格直接返回，`RunPresetEvolution` 仅遍历陆格；`LandLifeRules` 只处理陆生生态位。现役水域环境使用 `plantBiomass/habitatRecovery`，旧水生态/迁徙账使用另一套 `algaeBiomass/algaeRecovery`，且没有完整可复用的水生捕食食物网与统一身体付款接口。把这两套账直接拼接会制造免费资源或双扣；本轮没有发明新的摄食公式，也没有打开未验收功能开关。因此目标中的水生统一生态结算与陆↔水正式进化交易**尚未完成**。

## 证据索引

- `python-selftest.txt`、`offline-boundary.txt`：离线自检。
- `unity-boundary-evidence.txt`：逐项编辑器门禁、day 0/10 拓扑与资源/种群快照、SHA-256 和最终 PASS。
- `five-species-100.trace.txt`、`five-species-100.baseline.trace.txt`、`replay-comparison.txt`：逐日真实沙盒回放及哈希比对。
- `land-origin-100.trace.txt`：空操作真实场景回放。
- `land-events-evidence.txt`：现有生态事件检查。
- `land-audit-baseline-comparison.txt`：V4.3 原始与改动分支相同首日失败。

所有路径均相对于本报告所在的 `Artifacts/WaterLandV02/`。原始 Unity 完整日志保存在隔离工作树上一级 `G:\wl02\`（文件名分别为 `waterland-unity-boundary-state-fixed.log`、`five-species-100-unity.log`、`population-transition-unity.log`、`waterland-land-audit.log`），基线完整日志为 `G:\wlbase-five-species-100.log`、`G:\wlbase-land-audit.log`。日志仅用于审计，未将 Unity 生成的 ProjectSettings 变更加入提交。

\n