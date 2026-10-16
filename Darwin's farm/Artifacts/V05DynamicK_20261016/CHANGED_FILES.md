# 本轮作者变更与回退

主要改动位于M层与Editor回放工具。当前正式运行期间源码已冻结；源/资源闭包在checkpoint-final-candidate/sha256.json。不要用git reset覆盖工作区已有协作者改动。

| 文件 | 本轮内容 | 改前备份 |
| --- | --- | --- |
| Assets/Scripts/Simulation/LandLifeRules.cs | 单S账本、成年优先、取消重复密度出生倍率、成年饥饿分母、只读植食K和资源反事实、收支诊断 | checkpoint-before/LandLifeRules.cs |
| Assets/Scripts/Simulation/LandRuleTuning.cs | 新K作为选择输入的候选开关，默认false；原数值保留 | checkpoint-before/LandRuleTuning.cs |
| Assets/Scripts/BioData/PopulationData.cs | 非持久支付观测字段 | checkpoint-before/PopulationData.cs |
| Assets/Scripts/BioData/BlockInfo.cs | 非持久明确物质损耗计数 | checkpoint-before/BlockInfo.cs |
| Assets/Scripts/Simulation/SimulationController.cs | 只读诊断API、反事实前后模型哈希检查 | checkpoint-before/SimulationController.cs |
| Assets/Scripts/Simulation/SimulationController.Evolution.cs | 只记录原有转换储备截断损耗，分化条件/比例/支付未改 | checkpoint-before/SimulationController.Evolution.cs |
| Assets/Scripts/Simulation/SimulationController.LandTraits.cs | 遭遇实验已回退，当前预测表达式与改前相同；可能有换行格式变化 | checkpoint-before/SimulationController.LandTraits.cs |
| Assets/Scripts/Simulation/LandEcologyDiagnostics.cs及.meta | 新增独立只读局部G诊断，不进日循环 | 回退时移除，仅在hash仍匹配本阶段时操作 |
| Assets/Scripts/Simulation/Editor/V048ScenarioReplay.cs | 夹具/固定环境/受控R/移除扰动接口、完整支付观测、事件、完成标志和明确拒绝 | checkpoint-before/V048ScenarioReplay.cs |

SimulationController.cs改前副本通过删除本轮新增的诊断API恢复，其余既有代码保留；其余改前副本是在本轮相应变更前保存。全项目已有的UI、场景、Prefab或其它脏文件不是本轮恢复目标。

冻结付款原型和捕食遭遇试验均在独立分支保存；当前主工程没有储备跨日付款，也没有保留新遭遇系数。运行原型须新建独立工程副本后覆盖其存档Assets闭包，不能直接把其它分支人口和资源合进正式世界。

回退本轮时先关闭Unity，保存当前改动，核对所有目标文件仍与checkpoint-final-candidate的SHA相同。只恢复本表的改前文件并移除本轮新增诊断源码；任何文件有后续协作者变化就停止覆盖，人工合并。保留正式日志与本阶段报告作为回退证据。
