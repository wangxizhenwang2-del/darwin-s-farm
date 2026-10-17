"""Check final old-mode Unity replay against protected Task 9 start."""
import copy
import json
from pathlib import Path

runs = Path(__file__).resolve().parent / "runs"


def read(stem):
    return [json.loads(line) for line in (runs / (stem + ".jsonl"))
            .read_text(encoding="utf-8-sig").splitlines()
            if line.startswith("{")]


summary = []
for suffix in ("deer10-rich", "predator"):
    old = read("stage0-" + suffix)
    new = read("stage0-final-" + suffix)
    assert len(old) == len(new)
    model = sum(a["modelHash"] == b["modelHash"] for a, b in zip(old, new))
    environment = sum(a["environmentHash"] == b["environmentHash"]
                      for a, b in zip(old, new))
    ecological = 0
    for a, b in zip(old, new):
        x, y = copy.deepcopy(a), copy.deepcopy(b)
        x.pop("modelHash", None)
        y.pop("modelHash", None)
        for row in (x, y):
            for tile in row["tiles"]:
                for population in tile["populations"]:
                    population.pop("birthStressDeaths", None)
        ecological += x == y
    summary.append({"scenario": suffix, "records": len(old),
                    "modelHashMatches": model,
                    "environmentHashMatches": environment,
                    "ecologicalRecordsEqual": ecological})
print(summary)
assert all(x["environmentHashMatches"] == x["records"] and
           x["ecologicalRecordsEqual"] == x["records"]
           for x in summary)
(Path(__file__).resolve().parent / "STAGE0_FINAL_SUMMARY.json").write_text(
    json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
print("PASS final old-mode ecology and environment regression")
