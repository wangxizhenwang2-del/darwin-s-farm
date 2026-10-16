# 完整状态检查点可行性评估

状态：`BLOCKED`（在当前实现下尚不能把保存的状态用于正式增量验收）。评估在基线保存后、尝试缓存实现前完成。

真正可恢复的检查点至少要覆盖：`SimulationController` 的 seed、day、已注册物种/进化过滤、完整 `LandRuleTuning` 与调谐；每块 `BlockInfo` 的环境、S、R、碎屑、邻接、当天食物收支；每个 `PopulationData` 的全部字段，包括 ID、能量与体物、出生死亡小数余量、捕食余量、进化目标与连续日、压力 potential、保护 FIFO、濒危天数、迁徙来源与冷却；`EnvironmentWorld` 内部每块地的三个 `Transition` 的 start/target/age/duration/phase、局部气候 effect、恢复命令年龄与序列、拓扑和命令计数；地图放置与海拔；`SimulationTime`；以及会拒绝后续玩家操作的经济控制器局终状态。仅存人口、五 Bar 或逐日 JSONL 不能恢复这些状态。

现有 `CanonicalModelHash()` 反射公开 M 字段，可核对模型，但不是反序列化器。`EnvironmentWorld` 保存大量内部私有轨迹，`SimulationEnvironmentController` 不公开完整状态恢复 API；`PlayerEconomyController.CaptureStorageState()` 只保存仓库，不是整个生态。Unity Play 模式保存场景不会保留这套纯 C# 环境世界。因此当前没有安全的小范围接入点。要实现可验收的落盘恢复需同时改环境世界、地图、时钟、种群引用重连及经济局终状态，超过本阶段“不要为缓存大规模改造模型”的边界。

当前探索只复用**已验证输入前缀与逐日哈希证据**，每个新分支仍从第 0 日在真实 Unity M 层完整演算。此方法避免重复推导和错误拼接世界，但不节省 Unity 模拟日数：正式缓存命中 `0`，节省 `0` 日。后续若引入完整快照，必须把代码/资产/参数/初始世界/seed/随机版本/历史操作前缀指纹绑定，并先做从零与恢复后每日模型和环境双哈希对照；未通过不得参与正式验收。分支世界仍必须彼此独立。
