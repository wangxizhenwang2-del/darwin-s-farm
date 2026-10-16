# Task 1：进化网、食物网及受限确定性沙盒

本文件保留初始模型版本的历史回放。最终模型将未冻结的 `preyEnergyPerSize` 从 30 调到 40 后，Task 1 输入已再次进行三次独立 Unity 回放，结果见 [V05_TASK1_3_HANDOFF.md](V05_TASK1_3_HANDOFF.md) 与 `final-validation.json`。

状态：**完成 Task 1 范围内的核验和三次独立 Unity 回放**。未把 Task 2–6 的进化达成计入本项。

## 1. 唯一源数据与 Unity 对照

- 最新输入：`C:\Users\17799\Downloads\bioweb (13).json`，SHA256 `763fdd910f882df696688cf96a794155daa468cc34543a6077cd120df65c3dd5`；固定副本为 `source-bioweb13.json`。15 物种，20 条 JSON 进化连接，25 条有向食物连接。
- 最新规则把进化连接视为双向。JSON 中“鸡→灵感菇狸”存为“灵感菇狸→鸡”，所以只看 JSON 字段方向会误判；Unity 资产保存了双向的 40 条引用，模型 `ConnectedTargets` 也按两端相连判定。四条目标路线在真实 Unity 模型的目标选择 API 中均合法：鸡↔灵感菇狸、灵感菇狸↔鹿、鸡↔彩鸡、彩鸡↔孔雀。
- 资产食物引用和 JSON 25 条连线一致（JSON 为猎物→捕食者，资产为捕食者的 `preySpecies`）。五物种内部包括鸡→灵感菇狸、彩鸡→灵感菇狸、鸡→孔雀；鹿是食草动物。图上没有立即必然互灭的唯一猎物环，但长期共存仍须 Task 5/6 实测。
- 发现并经用户授权同步两处**导入资产与最新 JSON 的差异**：鸡默认繁殖 `70→71`、彩鸡默认体型 `15→14`。同步前文件和 SHA256 在 `checkpoint-task1-before`，同步后静态审计显示模板差异 0、双向展开后的进化边差异 0、食物边差异 0。没有改 JSON、连线或其他模板。`Tools/V05GraphAudit/audit.py` 只做源数据对照，不代替 Unity 模拟。

## 2. 受限沙盒

- 新建独立场景 `Assets/Scenes/V05FiveSpeciesSandbox.unity`，仅把 WhiteBox 场景的初始地块引用改为项目原有的荒漠预设；未改 WhiteBox 原场景。初始地块 (0,0) 的真实参数为温度 55、湿度 35、植物库存 S=50000、日恢复 R=50000。场景原有 `WhiteboxBiowebTestSetup` 在第 0 天生成默认鸡 160；回放器等待该初始化完成后才设种子和运行。
- `restrictedV05` 模式拒绝初始额外地块、初始库存覆盖、性状覆盖及额外初始物种；验证起点只有一块荒漠、一群默认鸡。模型只允许五个指定物种作为正式分化目标；现阶段允许的动作是项目真实温度、湿度、植物、海拔工具及温湿度撤销，禁止季风及其它未列出的技能。地块坐标限制在 3×3，地块总数至多 5；Task 1–3 不执行新增地块，地块放置及自然迁徙的实测留给 Task 4。
- 关闭的只是 `BioenergyAccount` 货币扣费；动物体物质、植物库存、出生、捕食和分化支付仍走真实 Unity M 层。回放器每个日步检查没有超出真实植物增长的无偿物质增加。
- 同一天的操作按输入 `actions[]` 原始顺序提交；玩家操作在所列日的日结后执行，影响从下一日开始。新增 `CancelTemperatureChange`、`CancelHumidityChange`，调用项目原有 `EnvironmentTechnologyController.TryCancel`。当前游戏语义是及时撤销进入 `Returning`，从当时的实际温湿度逐日恢复，并非瞬间回到基线。
- 每条 JSONL 记录模型哈希、环境哈希、五 Bar、种群数量与生死、体物质、储备、S/R、温湿度及环境阶段。另有操作接受/拒绝日志和 Unity 重要生态事件。回放绑定当前场景、主要代码、物种资产、源 JSON、场景输入和 Unity `6000.2.9f1`。

## 3. 真实 Unity 运行证据

正式输入：`task1-normal-actions.json`，`seed=240`，第 120 日结束。第 5 日温度 +25，第 7 日撤销温度，第 10 日湿度 +25，第 12 日撤销湿度，第 20 日植物 +25%／R +25000（具体由原项目工具执行）。五次操作全部 `accepted=True`，货币费用均为 0。

独立启动 Unity Editor 三次，产出 `task1-run1/2/3.jsonl` 与同名 `.log`。三份**完整 JSONL 字节**的 SHA256 完全相同：

`5DC5E8F133EE6C2F1A803EE9A5601DE51A5A9CEB48E2A62D651E6AC71332DCCE`

字节一致意味着其中每一个记录的逐日 `modelHash`、`environmentHash`、动作日志、状态和物质检查结果都一致。三次 Unity 日志均有 `V05_GRAPH_OK`、`V048_REPLAY_OK`。第 0 天为一块荒漠、鸡 160、五 Bar `(55,35,10,8,71)`、S=50000；第 120 天鸡 716，本次短场景没有出现新物种。第 7 日温度约 57 且阶段 `Returning`，第 12 日湿度约 37 且阶段 `Returning`；后续数值逐日回落。机器可读摘要见 `task1-validation.json`，静态图对照见 `task1-graph-audit-before.json` 和 `task1-graph-audit-after.json`。

## 4. 失败与修正记录

1. 初次空输入探测时，旧回放器在场景的鸡初始化协程完成前运行，留下空种群的 `task1-origin-probe.jsonl`。受限模式增加等待并严格核验鸡 160 后，正式输入从真实默认种群开始。
2. 第一次受限模式编译时，Editor 程序集不能直接访问内部 `FoodWeb` 类型；改为核验已导入的 `preySpecies` 引用，真实捕食仍由 M 层执行。编译随后通过。
3. 先行雨林诊断表明原 WhiteBox 起点是雨林，证据保留在 `task1-rainforest-diagnostic.*`；用户随后指定荒漠起点，正式三次回放均使用独立荒漠场景。

## 5. 改动、回退点和下一步

改动文件：两个上述 SpeciesData 资产；`SimulationController.Evolution.cs` 增加仅在受限运行时启用的五物种目标过滤；`Editor/V048ScenarioReplay.cs` 增加受限输入检查、初始化等待、取消操作、详细日记录与源绑定；新增独立测试场景及 `.meta`；新增静态图核对工具、输入和运行产物。普通游戏没有设置物种过滤时仍使用原进化网。

回退点：`checkpoint-task1-before` 保存修改前四个已有文件及 SHA256；`checkpoint-task1-complete` 保存 Task 1 完成后的文件、场景、输入和 SHA256。若要撤销本轮变更，应按清单恢复四个已有文件，并单独处理新增场景、工具和产物；不要重置此前其他合作者的工作树。

Task 2 将检查四条正式分化边在**真实日循环和正式门槛**下的结果。图合法只说明有候选连接，不证明自然演化已经跨过五 Bar、食物及物质门槛。
