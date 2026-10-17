"""Assert observable outcomes of the three Unity mechanism fixtures."""
import json
import sys
from pathlib import Path

BASE = Path(__file__).resolve().parent


def day_one(name):
    path = BASE / "runs" / (name + ".jsonl")
    records = [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines()
               if line.startswith("{")]
    assert records[-1]["day"] == 1
    return next(tile for tile in records[-1]["tiles"] if tile["x"] == 1 and tile["y"] == 0)


check = sys.argv[1]
if check == "A":
    tile = day_one("stage1-A-scarce")
    prey = next((p for p in tile["populations"] if p["species"] == "鸡"), None)
    assert prey is not None and prey["count"] > 0, "scarce chicken must survive one day with density feedback"
    assert prey["predationDeaths"] <= 1, "density feedback must sharply reduce low-N predation"
elif check == "B":
    tile = day_one("stage1-B-unpaid")
    deer = next(p for p in tile["populations"] if p["species"] == "鹿")
    assert deer["births"] == 0 and deer["birthBudget"] < deer["newbornCost"]
    assert deer["deathRemainder"] < 0.06, "unpaid birth opportunity must cause zero birth stress"
elif check == "C":
    tile = day_one("stage1-C-both-short")
    populations = {p["species"]: p for p in tile["populations"]}
    assert abs(tile["consumedPlants"] - 170000) <= 0.1
    assert abs(populations["鸡"]["allocatedFood"] - 119000) <= 0.1, "small lane owns 70% when both adult demands exceed budget"
    assert abs(populations["鹿"]["allocatedFood"] - 51000) <= 0.1, "large lane owns 30% when both adult demands exceed budget"
else:
    raise SystemExit("usage: check_stage1.py A|B|C")
print("PASS", check)
