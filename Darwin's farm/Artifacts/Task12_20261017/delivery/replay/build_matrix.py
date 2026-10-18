"""Build the Task 12 v0.5 acceptance matrix from current execution records."""
import csv
import json
from pathlib import Path

BASE = Path(__file__).resolve().parent.parent
RUNS = BASE / "runs"
COMMIT = "e58cc14898cef2da680bbe892e9b17d082e70401+preexisting-assets"

# A completed Unity process alone does not set status PASS.
CASES = [
    ("LEGACY_A_5000", "NOT_RUN", "仅有上轮历史PASS，本轮未复验", "", ""),
    ("LEGACY_B_5000", "NOT_RUN", "仅有上轮历史PASS，本轮未复验", "", ""),
    ("LEGACY_C_5000", "NOT_RUN", "仅有上轮历史PASS，本轮未复验", "", ""),
    ("LEGACY_D_5000", "NOT_RUN", "仅有上轮历史PASS，本轮未复验", "", ""),
    ("FORMAL_COLOR_STOP", "FAIL", "第132日正式进化后长期衰退，第750日仅28只", 750, "int-color-s2"),
    ("FORMAL_COLOR_CONTINUE", "NOT_RUN", "未制定合法继续操作对照", "", ""),
    ("FORMAL_MUSH_CONTINUE", "FAIL", "第429日正式进化，第529日目标灭绝", 529, "int-mushroom-s2"),
    ("FORMAL_MUSH_STOP", "PASS", "第429日正式进化且停止操作后500日仍有1726只；仅E2短期门槛", "", "int-mushroom-stop-s2"),
    ("MUSH_CAUSAL_CUTOFF_476", "PASS", "截断后至1050日仍有1834只，仅因果试验", "", "task12-mush-through-476"),
    ("MUSH_CAUSAL_CUTOFF_490", "PASS", "截断后至1050日仍有2007只，仅因果试验", "", "task12-mush-through-490"),
    ("MUSH_CAUSAL_CUTOFF_504", "PASS", "截断后至1050日仍有1999只，仅因果试验", "", "task12-mush-through-504"),
    ("MUSH_CAUSAL_CUTOFF_518", "FAIL", "追加第518日合法减草后第529日灭绝", 529, "task12-mush-through-518"),
    ("MIG_FLAT", "PASS", "真实RunMigration；目标3次迁入，含同种合并", "", "task12-mig-flat"),
    ("MIG_SLOPE1", "PASS", "真实RunMigration；高差1目标3次迁入", "", "task12-mig-slope1"),
    ("MIG_BLOCK_SLOPE2", "PASS", "高差2目标0次迁入，其他合法邻居仍可迁", "", "task12-mig-slope2"),
    ("MIG_CAPACITY", "NOT_RUN", "未建目标满载夹具", "", ""),
    ("MIG_COOLDOWN", "PASS", "已记录同一来源群连续成功事件最短间隔14日；更完整边界待测", "", "task12-mig-flat"),
    ("MIG_MERGE", "PASS", "平地第21日同种迁入合并", "", "task12-mig-flat"),
    ("MIG_SPLIT_MIN_MAX", "NOT_RUN", "源码上下限已见，边界夹具未跑", "", ""),
    ("MIG_CONSERVATION", "FAIL", "24事件中1条事件快照因同日反向交换违背逐项方程；全局未见复制", 70, "task12-mig-slope2"),
    ("ACTION_ENTRY_HOLD_RECOVERY", "NOT_RUN", "源码常量与25/150/100冲突，待Unity红测", "", ""),
    ("CANCEL_ENTRY", "NOT_RUN", "待Unity红测", "", ""),
    ("CANCEL_HOLD", "NOT_RUN", "待Unity红测", "", ""),
    ("CANCEL_RECOVERY", "NOT_RUN", "待Unity红测", "", ""),
    ("CANCEL_AND_REAPPLY", "NOT_RUN", "待Unity红测", "", ""),
    ("CANCEL_STOCK", "NOT_RUN", "待Unity红测", "", ""),
    ("CANCEL_ATTRACTION", "NOT_RUN", "待Unity红测", "", ""),
    ("CANCEL_EVOLUTION", "NOT_RUN", "待Unity红测", "", ""),
    ("FIVE_COEXIST_4EVOL_SEED240", "NOT_RUN", "尚无同世界四次正式进化输入", "", ""),
    ("FIVE_COEXIST_4EVOL_SEED241", "NOT_RUN", "待固定共同策略", "", ""),
    ("FIVE_COEXIST_4EVOL_SEED242", "NOT_RUN", "待固定共同策略", "", ""),
    ("DETERMINISM_REPEAT_240", "NOT_RUN", "待正式输入冻结", "", ""),
    ("NEGATIVE_ZERO_FOOD", "NOT_RUN", "待运行", "", ""),
    ("NEGATIVE_MANY_PREDATORS", "NOT_RUN", "待运行", "", ""),
    ("NEGATIVE_NO_LEGAL_EVOLUTION", "NOT_RUN", "待运行", "", ""),
    ("NEGATIVE_FULL_TARGET", "NOT_RUN", "待运行", "", ""),
    ("NEGATIVE_MIGRATION_CYCLE", "FAIL", "第70日发生同种双向交换且事件快照不自洽", 70, "task12-mig-slope2"),
    ("MATERIAL_LEDGER", "NOT_RUN", "Unity回放自带正向造物检查通过，完整账本审计未完成", "", ""),
    ("PERFORMANCE", "NOT_RUN", "待无诊断模式可比运行", "", ""),
]


def main():
    with (BASE / "ACCEPTANCE_MATRIX.csv").open("w", newline="", encoding="utf-8-sig") as file:
        writer = csv.writer(file)
        writer.writerow(("scenario", "seed", "commit_or_config", "input_sha256", "status",
                         "unity_executed", "failure_day", "reason", "evidence"))
        for name, status, reason, day, stem in CASES:
            record = json.loads((RUNS / (stem + ".execution.json")).read_text(
                encoding="utf-8")) if stem else None
            writer.writerow((name, record["seed"] if record else "NA", COMMIT,
                             record["inputSha256"] if record else "NA", status,
                             bool(record and record["unityReplayOK"]), day, reason,
                             "runs/" + stem + ".jsonl" if stem else ""))


if __name__ == "__main__":
    main()
