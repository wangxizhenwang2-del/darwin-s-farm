from __future__ import annotations

import csv
import difflib
import hashlib
import json
import shutil
import zipfile
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parent
delivery = root / "delivery"
worktree = Path(r"G:\unity\darwin-s-farm\Task12_Worktree_20261017\Darwin's farm")
baseline = root / "baseline_code"

sources = {
    "LandLifeRules.cs": "Assets/Scripts/Simulation/LandLifeRules.cs",
    "PopulationTransitionResult.cs": "Assets/Scripts/Simulation/PopulationTransitionResult.cs",
    "SimulationController.Migration.cs": "Assets/Scripts/Simulation/SimulationController.Migration.cs",
    "V048ScenarioReplay.cs": "Assets/Scripts/Simulation/Editor/V048ScenarioReplay.cs",
}
for name in ("EnvironmentRules.cs", "EnvironmentState.cs", "EnvironmentTechnologyController.cs", "EnvironmentWorld.cs", "SimulationEnvironmentController.cs"):
    sources[f"Environment/{name}"] = f"Assets/Scripts/Simulation/Environment/{name}"

patch = []
manifest = []
for rel, target in sources.items():
    before = (baseline / rel).read_text(encoding="utf-8-sig").splitlines(keepends=True)
    after_path = worktree / target
    after = after_path.read_text(encoding="utf-8-sig").splitlines(keepends=True)
    if before == after:
        continue
    patch.extend(difflib.unified_diff(before, after, fromfile=f"a/{target}", tofile=f"b/{target}"))
    manifest.append({"path": target, "sha256_after": hashlib.sha256(after_path.read_bytes()).hexdigest(), "sha256_before": hashlib.sha256((baseline / rel).read_bytes()).hexdigest()})
(delivery / "TASK12_changes.patch").write_text("".join(patch), encoding="utf-8")
(delivery / "PATCH_MANIFEST.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
shutil.copy2(root / "baseline_preexisting.patch", delivery / "PREEXISTING_BASELINE.patch")

protocol = delivery / "protocol"
protocol.mkdir(exist_ok=True)
for name in ("RULES_V05_CANONICAL.md", "MODEL_FORMULAS_V05.md", "AGENT_PROMPT.md", "BASELINE_EVIDENCE.md"):
    matches = list((root / "v05_protocol").rglob(name))
    if len(matches) != 1:
        raise RuntimeError(f"Protocol file {name}: {matches}")
    shutil.copy2(matches[0], protocol / name)
graph = root.parent / "V05FiveSpecies_20261016" / "source-bioweb13.json"
if not graph.exists():
    raise RuntimeError(f"Source graph: {graph}")
shutil.copy2(graph, delivery / "replay" / "source-bioweb13.json")

rows = []
def add(scenario, seed, status, reason, evidence, executed=True):
    rows.append({"scenario":scenario,"seed":seed,"status":status,"unity_executed":executed,"reason":reason,"evidence":evidence})
for label in "ABCD":
    add(f"LEGACY_{label}_5000", "fixture", "PASS", "生产代码5000日原群落存活；质量账日残差见P3_REGRESSION_A_D.csv", f"runs/task12-legacy-{label.lower()}-production.jsonl")
add("L0_PREY_QUOTA_D", "fixture", "PASS", "10002条猎物日记录，实捕死亡超过配额0条", "P3_L0_QUOTA.csv")
add("MATERIAL_LEDGER_TWO_SPECIES", "240/241/242", "PASS", "逐日物质差最大绝对值2.0，约末日总量4e-8；仅两物种", "P3_MATERIAL_LEDGER.csv")
for seed in (240,241,242):
    add("FORMAL_CHICKEN_TO_COLOR_AND_1025_DAY_COEXIST", seed, "PASS", "正式演化、真实植物付款、环境回稳后1025日两物种存续", f"runs/task12-rainforest-two-species-seed{seed}.jsonl")
add("DETERMINISM_TWO_SPECIES", 240, "PASS", "两次Unity执行1302个逐日模型/环境哈希完全相同", "runs/task12-rainforest-two-species-seed240-repeat.jsonl")
add("FORMAL_CHICKEN_TO_MUSH_OLD_REPLAY", 240, "PASS", "仅旧环境语义独立世界第429日；不可计入最终同世界", "runs/int-mushroom-s2.jsonl")
add("FORMAL_CHICKEN_TO_MUSH_CANONICAL_REPLAY", 240, "FAIL", "气候回归原始55/35后旧行动日程不再触发正式演化", "runs/int-mushroom-s2-production.jsonl")
add("MUSH_CONTINUE_OLD", 240, "FAIL", "旧语义继续减草，第529日灭绝；仅失稳复现", "runs/int-mushroom-s2.jsonl")
add("MUSH_STOP_OLD", 240, "PASS", "旧语义停止操作，第1050日仍存活；仅因果对照", "runs/int-mushroom-stop-s2.jsonl")
for label in ("flat","slope1","slope2"):
    add(f"MIGRATION_{label.upper()}", 240, "PASS", "独立机制夹具，真实迁徙事件及逐项数量守恒；不计入集成五物种", f"runs/task12-mig-{label}-production.jsonl")
add("MIGRATION_MERGE", 240, "PASS", "23条生产事件中21条同种目的地合并", "P3_MIGRATION_EVENTS.csv")
add("MIGRATION_HEIGHT2_BLOCK", 240, "PASS", "高差2指定目标0迁入；其它合法邻地仍可迁", "runs/task12-mig-slope2-production.jsonl")
add("MIGRATION_COOLDOWN", 240, "PASS", "运行轨迹中同源相邻成功间隔不低于14日；完整边界未覆盖", "P3_MIGRATION_EVENTS.csv")
add("MIGRATION_TARGET_CAPACITY_BOUNDARY", "fixture", "NOT_RUN", "未完成独立满载目的地边界夹具", "", False)
add("MIGRATION_GENE_EXCHANGE_BOUNDARY", "fixture", "NOT_RUN", "未完成基因交流断言", "", False)
add("MIGRATION_INTEGRATED_NATURAL", "240/241/242", "FAIL", "双物种综合场景无真实迁徙", "P3_FINDINGS.md")
add("CLIMATE_25_150_100", "fixture", "PASS", "第25日到目标，第175日开始回返，第275日回原始值", "runs/task12-climate-recovery-production.jsonl")
add("RECOVERY_25_150_100", "fixture", "PASS", "植物恢复R第25日75000、第275日50000", "runs/task12-recovery-return-production.jsonl")
for phase,filename in (("ENTRY","task12-climate-entry.jsonl"),("HOLD","task12-climate-hold-production.jsonl"),("RETURN","task12-climate-recovery-production.jsonl")):
    add(f"CLIMATE_CANCEL_{phase}", "fixture", "PASS", "从当前值渐进回原始基线；返回期重复撤销幂等", f"runs/{filename}")
add("CLIMATE_AND_RECOVERY_COMBINED", "fixture", "PASS", "同时施加后第275日均回原始基线", "runs/task12-climate-recovery-combined.jsonl")
add("CANCEL_AND_REAPPLY", "fixture", "NOT_RUN", "未单独验证撤销后重新施加", "", False)
add("CANCEL_STOCK", "fixture", "NOT_RUN", "未单独验证草库存按钮撤销", "", False)
add("CANCEL_ATTRACTION", "fixture", "BLOCKED", "源码玩家科技目录无邻地吸引按钮", "P3_FINDINGS.md", False)
add("EVOLUTION_REJECTION_NO_LEGAL_PREY", "fixture", "PASS", "Unity退出0且无正式进化事件；细分拒绝理由未独立断言", "runs/task12-int-neg-no-legal-prey-production.jsonl")
add("EVOLUTION_REJECTION_FEW_PREY", "fixture", "PASS", "Unity退出0且无正式进化事件；细分拒绝理由未独立断言", "runs/task12-int-neg-few-prey-production.jsonl")
add("TWO_EVOLUTIONS_SAME_WORLD", 240, "FAIL", "试过减草/交替对照，第二正式事件未产生且部分策略灭绝", "runs/task12-rainforest-two-evo-alternating.jsonl")
for n in (3,4,5):
    add(f"{n}_SPECIES_SAME_WORLD", "240/241/242", "NOT_RUN", "两物种后的第二正式进化受阻，未进行该阶段正式验收", "P3_FINDINGS.md", False)
for seed in (240,241,242):
    add("FIVE_SPECIES_FOUR_EVOLUTIONS_1000D", seed, "FAIL", "同世界仅完成鸡→彩鸡1次正式进化，五种目标与自然迁徙未满足", f"runs/task12-rainforest-two-species-seed{seed}.jsonl")
with (delivery / "ACCEPTANCE_MATRIX.csv").open("w", encoding="utf-8-sig", newline="") as f:
    writer = csv.DictWriter(f, fieldnames=["scenario","seed","status","unity_executed","reason","evidence"])
    writer.writeheader(); writer.writerows(rows)

(delivery / "FINAL_REPORT.md").write_text("""# Task 12 v0.5 最终结论：PARTIAL / 五物种 FAIL

本轮按修订版四边形地块、两条真实演化路线及25/150/100环境操作规范执行。Unity 6000.2.9f1 真机批量回放；未达到同世界鸡、彩鸡、孔雀、灵感菇狸、鹿四次正式进化与1000日五种共存，固定种子240/241/242完整验收均为 FAIL。没有把独立回放结果相加充作综合通过。

## 最大已验证成果

在同一雨林地块、初始模板鸡160只的阶梯场景，唯一玩家行动是第0日合法湿度-1。没有放入彩鸡或修改鸡性状。三个种子都通过真实 `PresetEvolution` 产生彩鸡并支付植物物质：240 第44日 `(0,0)` 29只、付款6668.57；241 第26日 `(0,0)` 19只、付款4217.74；242 第42日 `(0,0)` 27只、付款6211.10。第275日湿度恢复原始值后至第1300日无操作，三种种子鸡与彩鸡均存活1025日。末日240为鸡2318/彩鸡366，241为2456/266，242为2198/508。三种种子**均只完成4次目标正式进化中的1次**。详情见 `P3_FINDINGS.md` 与逐日、事件、物质 CSV。

生产代码 A–D 各5000日原群落存活；D 的10002条猎物配额记录无实捕超额。独立迁徙夹具共23条真实迁徙事件，逐项人数守恒，包含21次同种合并；运动80为夹具初始条件，不能计入综合自然迁徙。气候和植被恢复的25/150/100及进入/维持/恢复撤销通过局部 Unity 检查。种子240双物种重复执行1302条逐日哈希一致。

## 已知阻塞和失败对照

旧菇狸持续减草路线在旧环境语义下第429日进化、第529日灭绝；停止减草到1050日仍活。但修订规范修复气候回返后，原路线不再触发菇狸。雨林两物种后直接复用减草策略，两个物种在第550日前灭绝；重复单向减草第384日前灭绝；交替草库存干预保住两物种却未得到菇狸。源码中的邻地吸引信号只供诊断，未进入自然变异选择，科技目录也缺少玩家邻地吸引按钮。这是当前策略/接口阻塞；不能证明所有策略都不可能。未观察同世界鸡→灵感菇狸、彩鸡→孔雀、灵感菇狸→鹿，未运行三至五物种正式稳态试验。目的地满载、基因交流和取消后重新施加等边界也未完成。

## 源码和回退

源码只改隔离分支 `codex/task12-ecology` 与独立 Unity 测试工程，主工作树的 Assets 未动。`TASK12_changes.patch` 相对任务开始时已有未提交 Assets 改动；`PREEXISTING_BASELINE.patch` 是更早已有差异，不能算本轮代码。`baseline_code/` 存修改前文件，`PATCH_MANIFEST.json` 存修改前后 SHA256。回退本轮修复可从 `baseline_code/` 逐个恢复 manifest 所列文件。不要在干净 Git HEAD 上单独应用 Task12 补丁。

复现配置、场景与原始数据在 `replay/inputs/`、`runs/`，运行入口 `replay/run_unity.py`；Unity项目路径须按实际机器调整。`ACCEPTANCE_MATRIX.csv` 对每项明确 PASS/FAIL/BLOCKED/NOT_RUN。`P1_FINDINGS.md`、`MODEL_DECISIONS.md`、`P2_FINDINGS.md`、`P3_FINDINGS.md` 记录诊断、公式、候选、失败对照和证据界限。下一轮优先研究真实邻地迁入收益如何参与运动变异，再逐级复验第二、第三、第四次进化，保留本轮配额和质量账回归。
""", encoding="utf-8")
(delivery / "CODE_CHANGELOG.md").write_text("""# 源码变更记录

任务开始前主工作树已有未提交 Assets 差异，已单独封存为 `PREEXISTING_BASELINE.patch`。本轮只在隔离分支 `codex/task12-ecology` 和独立 Unity 测试工程进行两轮生产修改；没有改主工作树 Assets。

第一轮：植食环境 K 改复用既有共享植物分配器的 `FoodCarryingCapacity`；迁徙拒绝同日同种反向边计划并冻结拆分源计数事件快照。第二轮：气候与植被恢复统一25/150/100，保存真实原始基线，撤销从当前值渐进返回并使恢复期重复撤销幂等；Editor 回放器增加植被恢复撤销动作。完整9个文件差异见 `TASK12_changes.patch`，SHA见 `PATCH_MANIFEST.json`。

旧 K 近似 `max(0, R*生态位份额*效率 - 竞争者全部成年需求)/目标成年需求`；新 K 是固定竞争者数量条件下，以同一共享分配器求 `目标成年获食(n;可持续R) >= n*目标成年需求` 的最大整数。K只用于选择压力，不是数量硬上限。第232日旧公式彩鸡K=0，但真实分配约7208大于成年需求约4852，FoodK=3483。旧彩鸡619个存活日中609日出现 K=0/FoodK>0；更细推导见 `MODEL_DECISIONS.md`。另两候选加权扣竞争需求、经验摄食平滑均因物理预算/长稳态解释不足而舍弃。

回退方式：从 `baseline_code/` 恢复 manifest 中的对应路径，或丢弃隔离工作树；若从干净Git HEAD复现，需先明确处理 `PREEXISTING_BASELINE.patch`，然后再应用本轮补丁。不得把二者混作同一修复。
""", encoding="utf-8")
(delivery / "STAGE_LOG.md").write_text("""# 阶段研究日志

- P0 取证：完整阅读v0.5协议、图与历史基线；确认真Unity工程、四边形邻接、两条路线四条目标边和原工作树差异。见 `STAGE0_INVENTORY.md`。
- P1 复现：旧彩鸡第938日灭绝；旧菇狸持续减草第529日灭绝、停止后存活；迁徙第70日事件快照冲突；环境时程与撤销红测。见 `P1_FINDINGS.md` 和原始日志。
- P2 建模：用原始Unity日数据分析旧K假零，比较三候选；取共享分配FoodK；生产代码两轮最小修改并局部Unity复验。见 `MODEL_DECISIONS.md`、`P2_FINDINGS.md`。
- P3 阶梯验收：同一雨林世界鸡→彩鸡真实正式演化，三个种子恢复后1025日双物种存续；第二正式演化尝试失败；独立迁徙23事件；A–D各5000日和D捕食配额回归。见 `P3_FINDINGS.md`、P3 CSV、`ACCEPTANCE_MATRIX.csv`。
- P4 独立综合验收：第二演化未达到，三至五种进化及五种1000日无人干预未执行，综合目标FAIL。没有以独立场景拼接伪造通过。
- P5 截止封包：停止新实验，记录PARTIAL结论、补丁、输入、逐日原始trace、失败对照和下一轮阻塞。

离线部分仅对Unity原始数据代入公式及提取账本；未将离线推导称作Unity未来轨迹。
""", encoding="utf-8")
(delivery / "CHECKPOINT.md").write_text("""# 最终检查点

结论 PARTIAL。最新 `TASK12_RESULT.zip` 为可接手封包。最好同世界实绩：鸡和彩鸡通过真实进化在三个种子各存续至第1300日，恢复后1025日；完整五种目标未完成。生产修复两轮，补丁和初始文件、真实输入、Unity原始JSONL/日志、每日生态/账本、矩阵齐全。无待执行后台Unity进程。后续优先解决合法运动变异压力与鸡→菇狸的第二正式进化，再逐级扩展；不要把旧世界菇狸或独立迁徙算入综合通过。
""", encoding="utf-8")
(delivery / "README_NEXT.md").write_text("""# 复现与接手

先读 `FINAL_REPORT.md`、`ACCEPTANCE_MATRIX.csv`、`P3_FINDINGS.md`，再读 P1/P2 与公式研究。`protocol/` 为用户修订规范。原始 Unity 输出在 `runs/`；生产与失败对照输入在 `replay/inputs/`。回放命令：`python replay/run_unity.py replay/inputs/<scenario>.json`，需先将 `TASK12_changes.patch` 应用到带有 `PREEXISTING_BASELINE.patch` 对应状态的Unity项目，并核对 `PATCH_MANIFEST.json`。`replay/run_unity.py` 的Unity及隔离项目路径为本机绝对路径，换机时调整。`replay/source-bioweb13.json` 为原四边目标图。优先跑同世界第二正式进化，不得将夹具的初始高运动鸡当作自然迁徙验收。
""", encoding="utf-8")
with (delivery / "P3_FINDINGS.md").open("a", encoding="utf-8") as f:
    f.write("\n## 基础生态、捕食配额与剩余边界\n\nA–D生产分支各Unity运行5000日，末日原有物种全部存活；逐日质量账最大绝对残差依次0.0314、0.0355、1.7085、4.875，详见 `P3_REGRESSION_A_D.csv`。D的10002条逐日猎物配额记录中0条实际捕食死亡超配额，见 `P3_L0_QUOTA.csv`。两个正式进化拒绝夹具退出码0且无事件；更细拒绝原因尚未断言。迁徙满载边界、基因交流及取消后重施未跑，矩阵保持NOT_RUN。\n")

zip_path = root / "TASK12_RESULT.zip"
with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
    for path in sorted(delivery.rglob("*")):
        if path.is_file():
            archive.write(path, path.relative_to(delivery))
print(json.dumps({"zip":str(zip_path),"bytes":zip_path.stat().st_size,"files":len([p for p in delivery.rglob('*') if p.is_file()]),"patch_files":len(manifest),"matrix_rows":len(rows),"utc":datetime.now(timezone.utc).isoformat()}, ensure_ascii=False))
