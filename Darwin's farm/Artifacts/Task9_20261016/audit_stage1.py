"""Cross-fixture checks over raw real-Unity Task 9 JSONL evidence."""
import json
from pathlib import Path

RUNS = Path(__file__).resolve().parent / "runs"


def records(stem):
    return [json.loads(line) for line in (RUNS / (stem + ".jsonl"))
            .read_text(encoding="utf-8-sig").splitlines() if line.startswith("{")]


def tile(record):
    return next(t for t in record["tiles"] if t["x"] == 1 and t["y"] == 0)


def populations(record):
    return {p["species"]: p for p in tile(record)["populations"]}


scarce = populations(records("stage1-A-scarce")[-1])
abundant = populations(records("stage1-A-abundant")[-1])
assert scarce["鸡"]["predationDeaths"] == 0
assert abundant["鸡"]["predationDeaths"] > scarce["鸡"]["predationDeaths"]
assert abundant["鹿"]["predationDeaths"] == 0

paid_12 = populations(records("stage1-B-paid-12")[-1])["鹿"]
paid_08 = populations(records("stage1-B-paid-08")[-1])["鹿"]
assert paid_12["births"] == paid_08["births"] == 1
assert abs(paid_12["birthStressDeaths"] - 0.588) < 0.0001
assert abs(paid_08["birthStressDeaths"] - 0.392) < 0.0001
assert populations(records("stage1-B-unpaid")[-1])["鹿"]["birthStressDeaths"] == 0

forward = populations(records("stage1-C-both-short")[-1])
reverse = populations(records("stage1-C-reverse")[-1])
for species in ("鸡", "鹿"):
    assert forward[species]["allocatedFood"] == reverse[species]["allocatedFood"]
assert forward["鸡"]["foodK"] == 1690
assert forward["鹿"]["foodK"] == 368
transfer = populations(records("stage1-C-transfer")[-1])["鹿"]
assert transfer["adultPaid"] >= transfer["adultDemand"] - 0.1

joint = records("stage1-ABC-joint")
assert len(joint) == 101
for before, after in zip(joint, joint[1:]):
    a, b = tile(before), tile(after)
    assert b["consumedPlants"] <= 170000.1
    assert abs(b["stock"] - (a["stock"] + b["plantGrowth"] -
                            b["consumedPlants"])) <= 0.2
    assert after["totalMaterial"] <= (before["totalMaterial"] +
        after["plantGrowthToday"] + max(1, before["totalMaterial"] * 1e-5))
    for p in b["populations"]:
        assert p["deaths"] >= p["predationDeaths"]
        assert p["births"] * p["newbornCost"] <= p["birthBudget"] + 0.2
assert len(populations(joint[-1])) == 3
print("PASS Stage 1 A/B/C independent and joint Unity audit")
