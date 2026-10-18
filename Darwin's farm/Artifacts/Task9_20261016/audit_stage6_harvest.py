"""Check the real Unity paid harvest receipt and following daily income."""
import json
from pathlib import Path

base = Path(__file__).resolve().parent
stem = base / "runs" / "stage6-harvest"
execution = json.loads(stem.with_suffix(".execution.json").read_text(encoding="utf-8"))
assert execution["exitCode"] == 0 and execution["unityReplayOK"]
lines = stem.with_suffix(".jsonl").read_text(encoding="utf-8-sig").splitlines()
rows = [json.loads(line) for line in lines if line.startswith("{")]
before, after = [row for row in rows if row["day"] == 10]
following = next(row for row in rows if row["day"] == 11)

def chickens(row):
    tile = next(tile for tile in row["tiles"] if tile["x"] == 0 and tile["y"] == 0)
    return next(pop["count"] for pop in tile["populations"] if pop["species"] == "鸡")

assert any("kind=Harvest" in line and "accepted=True" in line for line in lines)
assert (chickens(before), chickens(after), chickens(following)) == (231, 115, 119)
assert (before["balance"], after["balance"], following["balance"]) == (2050, 2097, 2101)
print("PASS Unity harvest: 116 chickens, +47 bioenergy, following income +4")
