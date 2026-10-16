# v0.5 下一阶段公式与参数变更日志

基线：[`MODEL_BASELINE.md`](MODEL_BASELINE.md)，改前回退目录：`checkpoint-next-stage-before`。任何 Unity 正式回归必须以当前代码版本各自独立三次回放，旧版本哈希仅用于轨迹解释。

| 状态 | 位置 | 旧值/规则 | 新值/规则 | 测试依据与预期影响 | 回归结果 | 回退点 |
| --- | --- | --- | --- | --- | --- | --- |
| PASS | `SimulationController.Migration.cs` 自然迁徙候选 | 目标已有同种居民则跳过；自然路径未乘 `TargetCapacityCorrection` | 仅阻止同一天尚未落地的同种重复计划；对已有居民用 `TargetCapacityCorrection` 乘迁徙机会，落地后复用现有 `PopulationTransfer.Merge` | C2 改前真实 Unity 日 826 首次鸡迁徙成功，但至日 950 无再次鸡迁徙合并，尽管源鸡群和目标鸡群持续存在；修复后日 868 鸡源群 1608→1508、目标群 130→230，`outcome=Merged`；自然迁徙新规则得到实际触发。 | 三组固定输入各三次独立 Unity 完整回放 `PASS`，同组逐日模型/环境双哈希、操作和事件相同。旧版与新版三组每个记录的模型哈希仍相同；环境哈希因回放器显式加入海拔而改变，实际环境轨迹未改。详见 `nextstage-fixed-validation.json`。 | `checkpoint-next-stage-before/SimulationController.Migration.cs` |
| FAIL→REVERTED | `LandRuleTuning.cs` 小植物层体型尺度 | `smallPlantSizeScale=20`；效率 `1/(1+(size/20)^4)` | 试过 30 和 40，最终恢复 20；大植物层、层归属和其它规则未改 | 基线下孔雀候选五 Bar 短暂进入容差，但鸡有效容量降为 0；鹿第 1789 日分化 10 只后因无出生而注销。30 的孔雀探针第 580 日正式分化 4 只、随后注销，但已验证灵感菇狸路径由第 473 日变为第 455 日分化 25 只，并在第 573 日前消失；40 的孔雀探针无第二次正式进化，灵感菇狸也消失。提高效率改变鸡与捕食者竞争，破坏基线路线。 | 30 与 40 的 Unity 探索运行均 `FAIL`；代码按 SHA256 校验完全恢复到改前。未把这些探针用于正式验收，因此无需比较其哈希与基线。 | `checkpoint-smallplant20-before/LandRuleTuning.cs`，改前及恢复后 SHA256 `40939AF5FA769CC81818A34B172C81B26FAE3259D0F4BA01F1B3DF83933BFAC3` |

`preyEnergyPerSize=40` 是上一轮已确认基线，不是本阶段新增改动；Python 原型值未进入 Unity。
