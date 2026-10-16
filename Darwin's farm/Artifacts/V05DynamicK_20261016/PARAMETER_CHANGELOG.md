# Task 5 参数与公式变更账

Python仅解包、启动Unity、读取日志、汇总和画图。研究包60%分层、0.035出生系数、0.25捕食系数及目标人口均未采用。

| 项目 | 改前 | 当前主工程 | 依据与测试 | 回退点 |
| --- | --- | --- | --- | --- |
| 植物竞争 | 同一S预切互斥小/大层 | 一份S，两层混合成获取效率；W=A×U，需求封顶、重分配 | T2反事实/正反遍历、T1–T5收支 | checkpoint-before/LandLifeRules.cs |
| 结算顺序 | 成年+分数幼体合并领取 | 全体成年先分，剩余分繁殖 | T3成年支付/出生预算 | 同上 |
| 出生额外密度倍率 | GroupBirthFactor另按层R缩小出生 | 删除重复资源惩罚 | 单鸡收敛、双食草、旧路线 | 同上 |
| 饥饿分母 | 成年+假设幼体需求 | 成年真实维持需求 | 已吃饱成年不因未付幼体再判饥饿；T3 | 同上 |
| K_food | 旧K受当天需求封顶 | 植食只读，固定竞争者、可持续R预算二分边界 | T1供给上界、T2竞争单调 | 同上 |
| N/K输入 | 原environmentalK | 默认仍原值；新增useFoodCapacityForSelection=false开关 | 单独A/B；不默认接入未验证候选 | checkpoint-before/LandRuleTuning.cs |
| K_eff | 无 | 临时Unity地块、同一M层一个日步、有界诊断 | 多交点/群落快变均返回不确定；不控制出生死亡 | 新LandEcologyDiagnostics.cs及诊断API |
| 出生付款 | 40×size身体+0.4×size+0.02×size²幼体，全额付款 | **未改** | 小群粒度问题BLOCKED，待用户确认 | checkpoint-before |
| preyEnergyPerSize / smallPlantSizeScale | 40 / 20 | **40 / 20，未改** | 所有运行时JSONL | checkpoint-before/LandRuleTuning.cs |
| 模板、图、10%、正式门槛、工具、迁徙 | 已确认基线 | **未改** | 旧输入原字节复制并回归 | 改前/前阶段备份 |
| 观测 | 缺完整支付信息 | 非持久诊断字段、消费/储备截断损耗、操作物质差、灭绝事件 | 资源审计与JSONL；不创造资源 | 改前BlockInfo/PopulationData/Evolution/runner |

原0.7小植物份额改作通道效率混合权重，0.7无专精效率用作跨偏好层权重，具体A公式见DYNAMIC_K_FORMULA.md。这是本轮明确登记的结构变化，系数未改。competitionPressure=2与oppositeStrategyCompetition=0.5继续用于捕食；植物改用共同权重分配。densityBirthDampingStart=0.85仅留作历史序列化值，已无人口结算调用。

## 排除或回退的实验

1. 初版软通道仍各自保留预算。task1-shared-structure-explore为探索，未作正式公式；随后改为单账本，层只提供A权重。不得将该初版与最终版本混作同一参数。
2. 最初“贫乏草地”按资产文字读R1000，真实地形映射却是草原R90000。无v2/formal前缀的T3-*-poor全部排除。修正夹具显式固定T/H/R，保留当前S，不补库存。
3. K_food边界最初使用固定0.0001容差，被大数单精度误差误判。改用max(0.01,需求×1e−5)。没有用容差放宽真实出生付款；只用于只读K边界比较。
4. 捕食遭遇分支：实际捕食缺猎物数量饱和项，收益预测却有N/(N+50)。独立真实Unity分支加入该项，半饱和分别0、50、200、500、1000，只改这一项系数。0/50/200仍清空鸡，500延缓但未保留群落，1000保留部分猎物但捕食者仍注销。未证明三物种长期稳定，**主工程已回退实际捕食和原预测表达式，未保留新系数**。完整版本在experiment-encounter-v3；v3-T5-encounter输入须配该版本。最终主工程会拒绝该实验专用配置。
5. 跨日付款原型：仅在独立Unity工程复用energyReserve，成本不变。10鹿富资源100日到27；贫乏仍灭绝，竞争仍可失败，三物种长期稳定仍未证明。**未并入主工程**。完整代码/资源版本在birth-account-prototype；设计、守恒、A/B和确认要求见BIRTH_ACCOUNTING_DESIGN.md。

## 回退和正式证据

checkpoint-before保存本阶段改前文件；MODEL_BASELINE_BEFORE.md保存继承的数值来源。checkpoint-shared-v2为共享结构阶段；experiment-encounter-v3为已回退捕食实验；birth-account-prototype为未获批准付款方案。checkpoint-final-candidate保存当前完整M源码、相关模板、地图和沙盒场景，sha256.json列出指纹。原型及捕食分支均应恢复到独立工程复现，不覆盖协作者文件。

formal-*是最终选定测试，同输入从第0日独立Unity进程三次。路线生态FAIL与确定性PASS分别判断，原路线操作被拒绝必须记FAIL。三次结果见DETERMINISM_VALIDATION.json；完整文件SHA见SHA256.json。不同公式版本的哈希不作等值比较。

完整世界恢复BLOCKED，缓存命中0、节省模拟日数0；没有拼接分支状态。
