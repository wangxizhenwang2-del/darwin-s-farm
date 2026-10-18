"""Verify real Unity predator K is read-only and uses renewable prey."""
import copy
import json
from pathlib import Path

root = Path(__file__).resolve().parent


def records(name):
    return [json.loads(line) for line in (root / "runs" / (name + ".jsonl"))
            .read_text(encoding="utf-8-sig").splitlines()
            if line.startswith("{")]


off = records("stage5-predatorK-off")
on = records("stage5-predatorK-on")
assert len(off) == len(on) == 101
assert [(x["modelHash"], x["environmentHash"]) for x in off] == [
    (x["modelHash"], x["environmentHash"]) for x in on]
predator_k = []
for old, new in zip(off, on):
    unmodified = copy.deepcopy(new)
    for tile in unmodified["tiles"]:
        for population in tile["populations"]:
            if population["species"] == "灵感菇狸":
                predator_k.append({"day": new["day"],
                                   "foodK": population["foodK"]})
                population["foodK"] = -1.0
    assert old == unmodified, new["day"]
assert any(x["foodK"] > 0 for x in predator_k), "no positive predator K"
result = {"dailyDoubleHashMatches": 101,
          "predatorDay100": predator_k[-1],
          "positiveDays": sum(x["foodK"] > 0 for x in predator_k)}
(root / "STAGE5_SUMMARY.json").write_text(
    json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print("PASS read-only predator K Unity comparison", result)
