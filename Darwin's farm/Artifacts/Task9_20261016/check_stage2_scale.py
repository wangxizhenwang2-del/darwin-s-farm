"""Compare the Unity 0.40 adult cost fixture against Task 5 cost fields."""
import json
from pathlib import Path

BASE = Path(__file__).resolve().parent
old = BASE.parent / "V05DynamicK_Continuation_20261016" / "runs" / "formal-T5-rich-civet50-run1.jsonl"
new = BASE / "runs" / "stage2-scale-only.jsonl"


def day_one(path):
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if line.startswith("{"):
            record = json.loads(line)
            if record["day"] == 1:
                tile = next(t for t in record["tiles"] if t["x"] == 1 and t["y"] == 0)
                return {p["species"]: p for p in tile["populations"]}
    raise AssertionError(f"missing day 1: {path}")


before, after = day_one(old), day_one(new)
for species in ("鸡", "鹿", "灵感菇狸"):
    assert abs(after[species]["adultNeedPerHead"] -
               0.4 * before[species]["adultNeedPerHead"]) < 0.0002, species
    assert abs(after[species]["newbornCost"] -
               before[species]["newbornCost"]) < 0.0002, species
print("PASS Unity adult maintenance 0.40; newborn cost unchanged")
