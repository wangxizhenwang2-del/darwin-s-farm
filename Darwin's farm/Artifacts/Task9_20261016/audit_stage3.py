"""Audit Unity terrain settings and shock runs without simulating ecology."""
import json
from pathlib import Path

BASE = Path(__file__).resolve().parent
RUNS = BASE / "runs"
INPUTS = BASE / "inputs"


def data(name):
    return [json.loads(line) for line in (RUNS / (name + ".jsonl"))
            .read_text(encoding="utf-8-sig").splitlines() if line.startswith("{")]


def target(row):
    return next(tile for tile in row["tiles"] if tile["x"] == 1 and tile["y"] == 0)


for index in range(27):
    name = f"stage3-habitat{index:02d}"
    scenario = json.loads((INPUTS / (name + ".json")).read_text(encoding="utf-8"))
    expected = scenario["tiles"][0]
    rows = data(name)
    assert len(rows) == 11, name
    first = target(rows[0])
    assert first["stock"] == expected["initialStock"] == expected["recovery"], name
    assert first["recovery"] == expected["recovery"], name
    assert first["temperature"] == expected["temperature"], name
    assert first["humidity"] == expected["humidity"], name
    assert first["elevation"] == index // 9, name
    for row in rows:
        tile = target(row)
        assert tile["stock"] >= 0 and tile["consumedPlants"] >= 0, (name, row["day"])

disasters = []
for cut in (10, 20, 50):
    name = f"stage3-disaster{cut}"
    rows = list({row["day"]: row for row in data(name)}.values())
    assert len(rows) == 2001
    assert target(rows[599])["recovery"] == int(80000 * (100-cut) / 100)
    assert target(rows[600])["recovery"] == int(80000 * (100-cut) / 100)
    assert target(rows[1000])["recovery"] == 80000
    for before, after in zip(rows, rows[1:]):
        tolerance = max(1.0, before["totalMaterial"] * 1e-5)
        assert after["totalMaterial"] <= before["totalMaterial"] + after["plantGrowthToday"] + tolerance, (name, after["day"])
    last = target(rows[-1])
    disasters.append({"cut": cut,
        "last": {p["species"]: p["count"] for p in last["populations"]},
        "stock": last["stock"]})

(BASE / "STAGE3_SUMMARY.json").write_text(
    json.dumps({"habitats": 27, "disasters": disasters}, ensure_ascii=False, indent=2),
    encoding="utf-8")
print("PASS 27 Unity habitat configurations and 3 resource disasters", disasters)
