"""Summarize real-Unity 2000/5000-day population and material records."""
import json
from pathlib import Path
from statistics import mean

BASE = Path(__file__).resolve().parent
RUNS = BASE / "runs"


def rows(name):
    return [json.loads(line) for line in (RUNS / (name + ".jsonl"))
            .read_text(encoding="utf-8-sig").splitlines() if line.startswith("{")]


def counts(row):
    tile = next(t for t in row["tiles"] if t["x"] == 1 and t["y"] == 0)
    return {p["species"]: p["count"] for p in tile["populations"]}


summary = []
for days in (2000, 5000):
    for initial in (10, 15, 20, 50, 100, 300, 500):
        name = f"stage2-civet{initial}-day{days}"
        data = rows(name)
        assert len(data) == days + 1, name
        for before, after in zip(data, data[1:]):
            tolerance = max(1.0, before["totalMaterial"] * 1e-5)
            assert after["totalMaterial"] <= (before["totalMaterial"] +
                    after["plantGrowthToday"] + tolerance), (name, after["day"])
            tile = next(t for t in after["tiles"] if t["x"] == 1 and t["y"] == 0)
            assert tile["stock"] >= 0 and tile["consumedPlants"] >= 0
            for p in tile["populations"]:
                assert p["births"] * p["newbornCost"] <= p["birthBudget"] + 0.3, (name, after["day"], p["species"])
                assert p["deaths"] >= p["predationDeaths"]
        first_window = [counts(row) for row in data[1000:2001]]
        late_window = [counts(row) for row in data[4000:5001]] if days == 5000 else []
        species = ("鸡", "鹿", "灵感菇狸")
        entry = {
            "scenario": name, "days": days, "initialCivet": initial,
            "day2000": counts(data[2000]), "last": counts(data[-1]),
            "mean1000to2000": {key: round(mean(item.get(key, 0) for item in first_window), 2) for key in species},
            "mean4000to5000": {key: round(mean(item.get(key, 0) for item in late_window), 2) for key in species} if late_window else None,
            "allThreeAtEnd": all(counts(data[-1]).get(key, 0) > 0 for key in species),
        }
        summary.append(entry)

standard = next(item for item in summary if item["days"] == 5000 and item["initialCivet"] == 50)
assert standard["allThreeAtEnd"], "standard 5000-day fixture lacks long-term coexistence"
assert standard["last"]["鸡"] >= 800 and standard["last"]["鹿"] >= 800
assert standard["last"]["灵感菇狸"] >= 100
(BASE / "STAGE2_SUMMARY.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
for item in summary:
    print(item["scenario"], item["last"], "three", item["allThreeAtEnd"])
print("PASS Stage 2 Unity material, paid births, and standard 5000-day coexistence")
