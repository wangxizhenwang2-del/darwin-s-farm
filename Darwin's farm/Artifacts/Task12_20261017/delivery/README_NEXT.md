# 复现与接手

先读 `FINAL_REPORT.md`、`ACCEPTANCE_MATRIX.csv`、`P3_FINDINGS.md`，再读 P1/P2 与公式研究。`protocol/` 为用户修订规范。原始 Unity 输出在 `runs/`；生产与失败对照输入在 `replay/inputs/`。回放命令：`python replay/run_unity.py replay/inputs/<scenario>.json`，需先将 `TASK12_changes.patch` 应用到带有 `PREEXISTING_BASELINE.patch` 对应状态的Unity项目，并核对 `PATCH_MANIFEST.json`。`replay/run_unity.py` 的Unity及隔离项目路径为本机绝对路径，换机时调整。`replay/source-bioweb13.json` 为原四边目标图。优先跑同世界第二正式进化，不得将夹具的初始高运动鸡当作自然迁徙验收。
