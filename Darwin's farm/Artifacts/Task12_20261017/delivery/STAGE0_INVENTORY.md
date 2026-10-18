# Task 12 v0.5 P0 基线审计（2026-10-17，中国标准时间）

## 协议、版本、隔离

- 已完整阅读修订包的 `RULES_V05_CANONICAL.md`、`MODEL_FORMULAS_V05.md`、`AGENT_PROMPT.md`、`BASELINE_EVIDENCE.md`。修订包 SHA256：`3E10EDD3A90E47DC2FDFB330EF7EBD0A40CC4053533535031A56399C2F2EC49A`。旧 Task12 对“六边形、五个子目标、七块、20次迁徙”的要求作废；新规则是四边形、五种共存、四次进化、少量可信迁徙。
- Unity 真工程 `G:\unity\darwin-s-farm\Darwin's farm`，项目版本 6000.2.9f1；本机 `G:\unity\6000.2.9f1\Editor\Unity.exe` 可用。基线隔离 Unity 工程 `G:\unity\darwin-s-farm\Task9_Unity_Isolated_20261016` 与主工程关键生态/回放源码 SHA256 一致。
- Git 基点 `e58cc14898cef2da680bbe892e9b17d082e70401`。原工作树在本任务开始前已有约33个跟踪文件修改及大量未跟踪证据，均未清除。另建分支 `codex/task12-ecology`、工作树 `G:\unity\darwin-s-farm\Task12_Worktree_20261017`。原 Assets 差异保存为 `Artifacts/Task12_20261017/baseline_preexisting.patch` 并同步到分支工作树；后续 Task12 改动须单独导出补丁。
- 主图 `Artifacts/V05FiveSpecies_20261016/source-bioweb13.json`，SHA256 `763FDD910F882DF696688CF96A794155DAA468CC34543A6077CD120DF65C3DD5`；15个物种、20条导入有向边。当前 C# 的 `ConnectedTargets` 将资产进化连接视为双向，再由性状、空间、食物、物质与连续天数门槛筛选；不是任意指定目标。

## 两条要求路线：图上四条连接已经核对，正式可达性仍待跑数

| 合法来源→目标 | 图连接 | 源码营养级（0为食草） | 生态位 | Unity 正式转化证据 |
|---|---|---:|---|---|
| 鸡→彩鸡 | 鸡→彩鸡 | 0→0 | 陆→陆 | 本轮复现 seed240 第132日，374只 |
| 彩鸡→孔雀 | 彩鸡→孔雀 | 0→1 | 陆→陆 | 尚无 |
| 鸡→灵感菇狸 | 导入为菇狸→鸡；运行时双向连接 | 0→1 | 陆→陆 | 本轮复现 seed240 第429日，267只 |
| 灵感菇狸→鹿 | 菇狸→鹿 | 1→0 | 陆→陆 | 尚无 |

图连接满足四条路线的必要条件，但后三代能否靠合法环境操作与自然变异通过正式进化门槛尚无 Unity 证据。初始鸡是第五种共存成员，不算进化事件。`splitFounderFraction=0.05` 属其他生态分化，与正式进化10%不可混同。

## 地图、入口、历史保护项

- 地图确为四边形：`MapGridManager.Directions` 和 `MapSimulationBridge.Directions` 均为上下左右四个单位坐标，`BlockInfo.Neighbors` 由共享边重建，不含斜对角。
- Unity 批处理入口 `V048ScenarioReplay.Run`，环境变量 `DF_V048_SCENARIO`、`DF_V048_OUTPUT`；脚本 `replay/run_unity.py` 已在本轮实际运行。`useV05Sandbox=true` 加载真实 `V05FiveSpeciesSandbox.unity`。`restrictedV05=true` 最多5块、允许五物种集合，适合本任务目标；非 restricted 可做独立迁徙机制测试。
- 证据包历史 A–D 5000日、D 同条件重复和 L0 捕食配额样本通过；本轮未复验前不能冒充新模型 PASS。历史彩鸡衰退与菇狸继续操作灭绝已在本轮用独立 Unity 进程重新复现，逐日数据和日志在 `runs/`。
- `SimulateDay` 外层次序：每块 `SimulateBlock` → 濒危移除 → 正式进化 → 迁徙 → 物种状态 → 全块环境指标刷新；迁徙事件时源与目标亦即时刷新。内层食物/出生/死亡顺序需继续审计。

## 未决和可复验命令

- 仍需验证彩鸡→孔雀、灵感菇狸→鹿的真实合法进化、同世界双路线及1000日五种共存。
- 持续环境操作的25/150/100时序与随时撤销还需核对源码及 Unity 红测；任何恢复期不能算严格无人干预稳态。
- 迁徙平地自然事件已在本轮 Unity 发生8次，含同种合并；高度1/2、拥挤、冷却和完整守恒仍待独立核验。该迁徙场景使用隔离机制夹具设置初始鸡运动80，仅证明迁徙函数，不可计作五物种自然进化验收。
- 重跑示例：在主工程目录执行 `python Artifacts/Task12_20261017/delivery/replay/run_unity.py Artifacts/Task12_20261017/delivery/replay/inputs/int-color-s2.json`。输入和执行结果均含 SHA256。
