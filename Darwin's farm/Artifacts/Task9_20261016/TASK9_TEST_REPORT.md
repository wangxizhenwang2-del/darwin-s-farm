# Task 9 Unity 正式测试报告

**结论：Stage 0–5 PASS；Stage 6 正式路线 FAIL。** 全部 PASS 指真实 Unity 6000.2.9f1 进程与其 JSONL／日志；Python 审计仅检查原始输出。主工程八个变更源码与已测隔离副本 SHA-256 相同，且主工程 Unity 五工具审计退出码 0。

| 阶段 | 状态 | 真实 Unity 证据及结论 |
|---|---|---|
| 0 旧模式 | PASS | `stage0-final-deer10-rich` 101 条及 `stage0-final-predator` 1001 条生态字段逐条相同，环境哈希 101/101、1001/1001；`STAGE0_FINAL_SUMMARY.json`。新增规则字段使原始模型哈希不同，已另行记录。 |
| 1 A/B/C | PASS | 独立反事实及 101 条联合记录；稀缺鸡捕食 0，充裕鸡被捕 32，鹿被捕 0；未付费出生压力 0，付费 1 只分别得 0.588／0.392；短缺预算鸡／鹿 K_food=1690／368；同赛道三群、逆序、跨赛道和逐日植物守恒通过。`audit_stage1.py`、`audit_stage1_extra.py`。 |
| 2 数值扩容 | PASS（有效初始域） | 7 个菇狸初始量各跑 2000 与 5000 日，共 14 组；15–500 组三物种长期共存，10 组菇狸灭绝。标准 50 组末日鸡1040、鹿1053、菇狸428；独立重跑 5001 条双哈希相同；四个参数邻点仍共存。`STAGE2_SUMMARY.json`、`STAGE2_SUPPLEMENT_SUMMARY.json`。 |
| 3 环境灾害 | PASS | 27 组 T/H/z/R、S₀=R；10／20／50% 减产各 2000 日，600–999 日减产、1000 日恢复。逐日非负及材料上界通过；50% 减产杀灭菇狸。`STAGE3_SUMMARY.json`。 |
| 4 五工具 | PASS，场景差异待设计确认 | 实际 WhiteBox 与 V5.1 荒漠沙盒各 13 组；S 与 R 分离、价格、时间线、T/H、海拔、两槽及只读预览通过。主工程 `runs/main-five-tools.log` PASS。`STAGE4_SUMMARY.json`、`STAGE4_V05_SUMMARY.json`。 |
| 5 动态 K | PASS（只读诊断） | K 关闭／开启的 101 条模型及环境双哈希相同；菇狸 K 正数 94 日，第100日 1089。`STAGE5_SUMMARY.json`。 |
| 6 正式路线 | **FAIL** | 彩鸡500、菇狸1000、鹿1900、孔雀900 四条付费路线全部跑到终日，但均未出现目标物种；菇狸与鹿路线各有 20 次生物能不足拒绝。`STAGE6_SUMMARY.json`。收割独立夹具 PASS。 |

## 失败与修复记录

Stage 3 首版回放夹具把 `S₀=R` 原值设置当作必须成功的变更操作，Unity 因无变化返回 false。修正回放器跳过这个无操作后通过；旧失败日志保存在 `failures/stage3-habitat00-initial.*`。Stage 4 沙盒首次在初始鸡 160 尚未加载时采样；改为等候真实场景就绪再读取，旧结果存 `failures/stage4-v05-origin-too-early.*`。两项均为回放器问题，未改生态公式或场景。

## 验证范围与限制

- 逐日材料审计检查植物唯一扣账、非负、出生付款、捕食死亡不重复，以及输入对应执行元数据；详情见各 `audit_stage*.py` 与 JSONL。
- Stage 2 最长 5000 日；标准 50 组重复与四邻点排除单次偶然。Stage 3 的 27 生境各跑 10 日设置与短期账本检查，并非全部地形的长期稳定性证明。
- Stage 6 路线由旧行动时序移植，组合植物工具拆成 S 与 R 两次付费，因此与旧输入不等价；不能由路线失败单独推断是 A/B/C/D 引起的回归。
- `ECOLOGY_T8_SAFE_DEVELOPMENT.zip` 未找到；Python 候选数量只作参考，绝不记为 Unity PASS。
- 水域、天空、跨生态位及长期 `K_eff` **NOT TESTED**。

每次 Unity 运行的输入 SHA-256、退出码、工程路径见相应 `runs/*.execution.json`；逐日生态哈希在 `runs/*.jsonl`，完整 Unity 编译日志在 `runs/*.log`。文件 SHA-256 与数量见 `EVIDENCE_MANIFEST.json`。
