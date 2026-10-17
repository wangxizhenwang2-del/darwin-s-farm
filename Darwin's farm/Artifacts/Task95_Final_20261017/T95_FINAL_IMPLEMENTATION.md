# Task 9.5 最终候选接入记录

**验收状态：FAIL；实验开关默认 OFF。** 严格保留规范固定门槛，未按结果调参。

- `LandRuleTuning.useCompensatedEvolutionFoodGate` 为独立开关，默认 `false`。Editor 回放输入 `t95FinalGate=true` 才在隔离工程启用；正式场景和资产未改。
- `SimulationController.Evolution.cs` 的创始群计划在开关 ON 时，最多分出母群约 10%，付款只使用 `S − min(S/2, capacity×minimumPlantReserveFraction)`；仍按真实母群每只身体物质计算差额。
- 食草型候选在付款后，调用 `LandLifeRules.ForecastEvolutionPlantsAfterPayment`，用真实下一日生长、同一共享植物分配器及纯 R 再生分配，计算母群与候选群的成年供给、整只已付费出生、自然死亡和出生压力。固定验收条件为成年两情景均 ≥0.95、当日净增 ≥−0.001N、再生净增 ≥0.005N。
- `V048ScenarioReplay` 仅增加开关和只读门槛探针。短诊断 0–135 日与完整回放双哈希 136/136 相同。

彩鸡正式路线在第 128 日被 `netRenewal ≥ 0.005×founders` 拒绝：Unity 实值 1.36217594 < 1.805。第 133 日虽短暂通过，但连续资格最高仅 1 日；至第 750 日未转化。按用户和包内“失败即停止”要求，未调门槛，也未继续 P3 捕食反事实、菇狸路线、负控、Stage 0 OFF 回归或三物种夹具。ON 分支对未实现的捕食反事实保守拒绝；本候选不能作为通过验收的修复启用。

详见 `T95_FINAL_RESULT.json`、`T95_FINAL_REGRESSION.json` 和第 128–135 日缩减 CSV。原始输入、Unity JSONL 与 `.log` 保存在 `../Task9_20261016/inputs/t95final-*` 和 `../Task9_20261016/runs/t95final-*`。
