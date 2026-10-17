"""Audit observed WhiteBox tool receipts and resource state from Unity JSONL."""
import json
import sys
from pathlib import Path

base = Path(__file__).resolve().parent
runs = base / "runs"
prefix = "stage4-v05-" if len(sys.argv) > 1 and sys.argv[1] == "v05" else "stage4-"


def records(name):
    text = (runs / (name + ".jsonl")).read_text(encoding="utf-8-sig")
    return [json.loads(line) for line in text.splitlines() if line.startswith("{")]


def origin(row):
    return next(t for t in row["tiles"] if t["x"] == 0 and t["y"] == 0)


baseline = records(prefix + "origin")[0]
initial = origin(baseline)
report = {"actualWhiteBoxOrigin": {
    "elevation": initial["elevation"],
    "temperature": initial["temperature"],
    "humidity": initial["humidity"],
    "recovery": initial["recovery"],
    "stock": initial["stock"],
    "populations": {p["species"]: p["count"] for p in initial["populations"]},
    "balance": baseline["balance"]}, "tools": []}

for kind in ("PlantStockChange", "PlantRecoveryChange"):
    for tier in (1, 2, -1, -2):
        name = f"{prefix}{kind}-tier{tier}"
        rows = records(name)
        before, after = rows[:2]
        old, new = origin(before), origin(after)
        price = (260 if tier == 1 else 560 if tier == 2 else
                 420 if tier == -1 else 960)
        assert after["balance"] == before["balance"] - price, name
        if kind == "PlantStockChange":
            expected = min(1_000_000, old["stock"] * (1 + tier * .25))
            assert abs(new["stock"] - expected) < .2, (name, new["stock"], expected)
            assert new["recovery"] == old["recovery"], name
        else:
            expected = max(0, min(150000, old["recovery"] + tier * 25000))
            assert new["recovery"] == expected, (name, new["recovery"], expected)
            assert new["stock"] == old["stock"], name
        report["tools"].append({"scenario": name, "price": price,
            "beforeS": old["stock"], "afterS": new["stock"],
            "beforeR": old["recovery"], "afterR": new["recovery"],
            "day1": origin(next(r for r in rows if r["day"] == 1))
                if kind == "PlantRecoveryChange" else None})

for kind, attribute in (("TemperatureChange", "temperature"),
                        ("HumidityChange", "humidity")):
    rows = records(prefix + kind)
    before, after = rows[:2]
    assert after["balance"] == before["balance"] - 220
    assert origin(after)["stock"] == origin(before)["stock"]
    assert origin(after)["recovery"] == origin(before)["recovery"]
    assert origin(after)[attribute + "Phase"] == "Entering"
    assert len({origin(row)[attribute] for row in rows if row["day"] <= 25}) > 1
    report["tools"].append({"scenario": prefix + kind,
        "day0": origin(after)[attribute],
        "day25": origin(next(r for r in rows if r["day"] == 25))[attribute],
        "day325": origin(next(r for r in rows if r["day"] == 325))[attribute]})

height = records(prefix + "ElevationChange")
assert origin(height[1])["elevation"] == origin(height[0])["elevation"] + 1
assert origin(height[-1])["elevation"] == origin(height[0])["elevation"]
report["tools"].append({"scenario": prefix + "ElevationChange",
    "day0Z": origin(height[1])["elevation"],
    "day1Z": origin(height[-1])["elevation"]})

slots = records(prefix + "two-slots")
assert origin(slots[-1])["temperaturePhase"] == "Returning"
assert origin(slots[-1])["humidityPhase"] in ("Entering", "Holding")
report["tools"].append({"scenario": prefix + "two-slots",
    "temperaturePhase": origin(slots[-1])["temperaturePhase"],
    "humidityPhase": origin(slots[-1])["humidityPhase"]})

if prefix == "stage4-v05-":
    assert report["actualWhiteBoxOrigin"]["stock"] == 50000
    assert report["actualWhiteBoxOrigin"]["recovery"] == 50000
    assert report["actualWhiteBoxOrigin"]["temperature"] == 55
    assert report["actualWhiteBoxOrigin"]["humidity"] == 35
    assert report["actualWhiteBoxOrigin"]["populations"].get("鸡") == 160
(base / ("STAGE4_V05_SUMMARY.json" if prefix == "stage4-v05-" else
         "STAGE4_SUMMARY.json")).write_text(
    json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print("PASS 13 real Unity tool runs; actual origin:",
      report["actualWhiteBoxOrigin"])
