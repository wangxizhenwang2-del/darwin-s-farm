"""Compare independent Unity replay hashes and narrow parameter neighbors."""
import json
from pathlib import Path
from statistics import mean

base = Path(__file__).resolve().parent


def rows(name):
    return [json.loads(line) for line in
            (base / "runs" / (name + ".jsonl")).read_text(
                encoding="utf-8-sig").splitlines() if line.startswith("{")]


def counts(row):
    tile = next(t for t in row["tiles"] if t["x"] == 1 and t["y"] == 0)
    return {p["species"]: p["count"] for p in tile["populations"]}


standard = rows("stage2-civet50-day5000")
repeat = rows("stage2-civet50-day5000-repeat")
assert len(standard) == len(repeat) == 5001
assert [(r["modelHash"], r["environmentHash"]) for r in standard] == [
    (r["modelHash"], r["environmentHash"]) for r in repeat]
result = {"repeatDailyDoubleHashMatches": 5001, "neighbors": []}
for suffix in ("scale035", "scale045", "stress070", "stress090"):
    name = "stage2-civet50-day5000-" + suffix
    data = rows(name)
    assert len(data) == 5001
    late = [counts(row) for row in data[4000:5001]]
    species = ("鸡", "鹿", "灵感菇狸")
    result["neighbors"].append({"scenario": name,
        "last": counts(data[-1]),
        "mean4000to5000": {s: round(mean(x.get(s, 0) for x in late), 2)
                            for s in species}})
(base / "STAGE2_SUPPLEMENT_SUMMARY.json").write_text(
    json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print("PASS independent 5001-record Unity double-hash repeat and 4 neighbors")
