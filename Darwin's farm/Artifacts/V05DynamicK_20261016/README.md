# Task 5 交付索引

整体 **BLOCKED**：当前主工程10鹿的跨日付款粒度问题仍在，修改冻结支付机制等待用户确认；长期稳定三物种未证明。已完成正式三次回放30/30组。不能把确定性PASS当成生态目标PASS。

- MODEL_BASELINE.md：当前真实Unity配置；MODEL_BASELINE_BEFORE.md：继承的改前来源。
- DYNAMIC_K_FORMULA.md：共享S、成年优先和只读K。
- RESOURCE_ACCOUNTING_AUDIT.md：1789日鹿案例与改后收支。
- POPULATION_TEST_REPORT.md：T1–T5、曲线、失败与未运行项。
- BIRTH_ACCOUNTING_DESIGN.md：冻结付款诊断、守恒、独立原型A/B及待确认。
- PARAMETER_CHANGELOG.md：保留/回退的公式和参数。
- REGRESSION_REPORT.md：四条旧路线和基础回放。
- DETERMINISM_VALIDATION.json / SHA256.json：三次逐日与完整文件验证。
- inputs / runs：完整输入、每日JSONL、Unity日志、逐项操作与事件、人口CSV和逐日哈希。
- checkpoint-before / checkpoint-final-candidate：改前与当前回退点。
- experiment-encounter-v3 / birth-account-prototype：已回退或未批准的受控分支，不能冒充正式游戏版本。

运行正式测试：用本目录run_unity.py，指定已关闭交互编辑器的主工程。每次新Unity进程从第0日打开V05沙盒。示例：python run_unity.py formal-T3-deer10-rich --runs 3 --project 主工程绝对路径。启动器只启动Unity，没有生态公式。analyze_results.py和plot_results.py只读取真实日志。

历史实验输入含overridePreyEncounter时，必须在独立工程恢复experiment-encounter-v3源码/资源闭包；付款原型则恢复birth-account-prototype/Assets闭包。各自sha256.json与日志bindingSha256记录版本。不得覆盖其他协作者文件，也不得将实验分支的物种/库存直接拼成联合世界。

所有玩家路线保持真实工具接口；机制夹具的直接初始物种/气候设置明确标记，不算游戏内可执行培育路径。恢复缓存仍BLOCKED，命中0，节省模拟日数0。

## 验收边界

| 项目 | 状态 |
| --- | --- |
| 5A旧鹿案例逐项诊断及1937条旧轨迹对照 | PASS |
| 共享植物S、成年优先、无超领/未付足出生 | PASS |
| 植食K_food、只读完整轨迹对照 | PASS |
| 捕食者新K_food | NOT_RUN |
| K_eff长期稳定数值 | NOT_RUN，仅提供明确不确定的局部诊断 |
| 10鹿正式持续繁殖 | FAIL |
| 冻结跨日付款正式并入 | BLOCKED，等待用户确认 |
| 单鸡、双食草、食物链与2000日实验执行 | PASS，生态结果各异 |
| 三物种长期稳定 | FAIL |
| 稳定三物种扰动 | NOT_RUN，前提未成立 |
| 旧路线回归和最终确定性 | PASS（执行/确定性）；原成功路线复现FAIL |

90次正式进程合计70563模拟日。Unity日志的启动时间、PID与性能统计不作生态等值比较；完整每日JSONL、操作与生态事件三次一致。性能在独立execution/FINAL_VERIFICATION中，不参与模型随机。
