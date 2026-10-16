# 本阶段源文件回退点

本目录保留阶段结束时的三份源文件副本；改前版本在相邻的 `checkpoint-next-stage-before/`。只保存本阶段实际接触的文件，不覆盖其他合作者文件。

| 源文件 | 阶段结束 SHA256 |
| --- | --- |
| `Assets/Scripts/Simulation/LandRuleTuning.cs` | `40939AF5FA769CC81818A34B172C81B26FAE3259D0F4BA01F1B3DF83933BFAC3` |
| `Assets/Scripts/Simulation/SimulationController.Migration.cs` | `AB388C2A164BB96A063D70E4E40BDC41B0D3127859962C941138572F15B8B912` |
| `Assets/Scripts/Simulation/Editor/V048ScenarioReplay.cs` | `909D6E27F2448DF2EDC77EDFA0E67877F90E1ACDD2E9B7395048D7CB05CF20D1` |

恢复方法：仅在确认目标文件没有他人后续编辑时，将需要的单个副本复制回上述原路径。数值尺度 20 的单独改前备份另在 `checkpoint-smallplant20-before/`，与阶段结束的 `LandRuleTuning.cs` 哈希完全一致。完整输入、输出与验证清单保留在上级目录。
