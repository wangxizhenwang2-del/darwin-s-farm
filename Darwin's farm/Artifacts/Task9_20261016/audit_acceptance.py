"""Summarize the fixed Task 9 acceptance replays without rerunning Unity."""
import csv
import hashlib
import json
import re
from pathlib import Path

BASE = Path(__file__).resolve().parent
OUT = BASE.parent / "Task9_Acceptance_20261017"
OUT.mkdir(exist_ok=True)


def rows(name):
    path = BASE / "runs" / (name + ".jsonl")
    return [json.loads(line) for line in path.open(encoding="utf-8-sig")
            if line.startswith("{") and '"modelHash"' in line]


def daily(name):
    return {row["day"]: row for row in rows(name)}


def origin(row):
    return next(tile for tile in row["tiles"]
                if tile["x"] == 0 and tile["y"] == 0)


def ecology(row):
    return [(tile["x"], tile["y"], tile["stock"], tile["detritus"],
             tile["plantGrowth"], tile["consumedPlants"], tile["recovery"],
             tile["temperature"], tile["humidity"],
             [(p["species"], p["count"], p["body"], p["reserve"],
               p["births"], p["deaths"], p["predationDeaths"],
               p["allocatedFood"]) for p in tile["populations"]])
            for tile in row["tiles"]]


def compare(old_name, new_name):
    old, new = rows(old_name), rows(new_name)
    assert len(old) == len(new)
    return {"days": len(new),
            "modelHashMatches": sum(a["modelHash"] == b["modelHash"]
                                    for a, b in zip(old, new)),
            "environmentHashMatches": sum(a["environmentHash"] ==
                                          b["environmentHash"]
                                          for a, b in zip(old, new)),
            "ecologyFieldMatches": sum(ecology(a) == ecology(b)
                                       for a, b in zip(old, new))}


def route(name):
    by = daily(name)
    source = origin(by[0])["populations"][0]["species"]
    first = next(day for day in sorted(by) if any(
        p["species"] != source for p in origin(by[day])["populations"]))
    target = next(p["species"] for p in origin(by[first])["populations"]
                  if p["species"] != source)
    def member(day, species):
        return next((p for p in origin(by[day])["populations"]
                     if p["species"] == species), None)
    terminal = first + 500
    assert terminal in by
    after = [by[day] for day in range(first + 1, terminal + 1)]
    residuals = [row["totalMaterial"] - by[row["day"] - 1]["totalMaterial"]
                 - row["plantGrowthToday"] for row in after]
    anomalies = sum(value > max(1, by[row["day"] - 1]["totalMaterial"] * 1e-5)
                    for value, row in zip(residuals, after))
    unpaid = sum(p["births"] * p["newbornCost"] > p["birthBudget"] + 0.3
                 for row in after for tile in row["tiles"]
                 for p in tile["populations"])
    negative = sum(p["body"] < -0.01 or p["reserve"] < -0.01
                   for row in after for tile in row["tiles"]
                   for p in tile["populations"])
    log = (BASE / "runs" / (name + ".log")).read_text(
        encoding="utf-8", errors="replace")
    conversion = next(line for line in log.splitlines()
                      if "kind=preset_evolution" in line)
    count = lambda day: member(day, target)["count"] if member(day, target) else 0
    return {"input": str(BASE / "inputs" / (name + ".json")),
            "jsonl": str(BASE / "runs" / (name + ".jsonl")),
            "log": str(BASE / "runs" / (name + ".log")),
            "source": source, "target": target,
            "firstConversionDay": first,
            "foundersAtConversion": count(first),
            "sourceBefore": member(first, source)["count"] + count(first),
            "sourcePreviousDay": member(first - 1, source)["count"],
            "sourceAfter": member(first, source)["count"],
            "conversionLog": conversion,
            "conversionDayMaterialResidual": by[first]["totalMaterial"] -
                by[first - 1]["totalMaterial"] - by[first]["plantGrowthToday"],
            "dayPlus1": count(first + 1), "dayPlus7": count(first + 7),
            "dayPlus30": count(first + 30), "dayPlus100": count(first + 100),
            "dayPlus500": count(terminal), "finalDay": max(by),
            "finalTarget": count(max(by)),
            "lastLivingDay": max(day for day in by if count(day)),
            "post500MaterialCreationAnomalies": anomalies,
            "post500MaxMaterialResidual": max(residuals),
            "post500UnpaidBirthAnomalies": unpaid,
            "post500NegativeBodyOrReserve": negative,
            "post500TargetBirths": sum(member(row["day"], target)["births"]
                                       for row in after if member(row["day"], target)),
            "post500TargetDeaths": sum(member(row["day"], target)["deaths"]
                                       for row in after if member(row["day"], target)),
            "rejectedActions": int(re.search(
                r"V048_REPLAY_COMPLETE .*rejectedActions=(\d+)", log).group(1))}


color = route("task9-color-formal")
mushroom = route("task9-mushroom-formal")
negatives = {}
for name, day in (("task9-negative-color128", 128),
                  ("task9-negative-mushroom425", 425)):
    state = daily(name)[day]
    negatives[name] = next(p["t95"]["finalNegatives"]
                           for tile in state["tiles"]
                           for p in tile["populations"]
                           if p.get("t95") and p["t95"].get("finalNegatives"))
negative_pass = all(case["eligible"] == (case["caseName"] == "positive")
                    for tests in negatives.values() for case in tests)

regression = {
    "stage0DeerOff": compare("stage0-final-deer10-rich",
                             "task9-off-stage0-final-deer10-rich"),
    "stage0PredatorOff": compare("stage0-final-predator",
                                 "task9-off-stage0-final-predator"),
    "threeSpecies": compare("T95E-community-D", "task9-three-species-on"),
    "strictStage0DoubleHash": "FAIL: default-off tuning field added to CanonicalModelHash",
    "oldEcologyOutput": "PASS: all exported ecology fields matched every Stage0 day",
    "unity": "6000.2.9f1"}
for key, old, new in (("colorNegative", "task9-color-probe135",
                       "task9-negative-color128"),
                      ("mushroomNegative", "task9-mushroom-formal",
                       "task9-negative-mushroom425")):
    a, b = daily(old), daily(new)
    regression[key] = {
        "days": len(b),
        "modelHashMatches": sum(a[d]["modelHash"] == row["modelHash"]
                                for d, row in b.items()),
        "environmentHashMatches": sum(a[d]["environmentHash"] ==
                                      row["environmentHash"]
                                      for d, row in b.items())}
regression["threeSpecies"]["day5000"] = {
    p["species"]: p["count"] for p in origin(daily("task9-three-species-on")[5000])
    ["populations"]}
regression["material"] = "PASS" if all(
    route["post500MaterialCreationAnomalies"] ==
    route["post500UnpaidBirthAnomalies"] ==
    route["post500NegativeBodyOrReserve"] == 0
    for route in (color, mushroom)) else "FAIL"
regression["performanceSeconds"] = {
    name: json.loads((BASE / "runs" / (name + ".execution.json"))
                     .read_text(encoding="utf-8"))["seconds"]
    for name in ("T95E-community-D", "task9-three-species-on",
                 "task9-color-formal", "task9-mushroom-formal")}

predator_probe = next(p["t95"]["finalProbe"] for p in origin(
    daily("task9-mushroom-formal")[425])["populations"] if p.get("t95"))
names = ("task9-color-formal", "task9-mushroom-formal",
         "task9-negative-color128", "task9-negative-mushroom425",
         "task9-off-stage0-final-deer10-rich",
         "task9-off-stage0-final-predator", "task9-three-species-on")
evidence = {}
for name in names:
    files = {kind: BASE / folder / (name + ext) for kind, folder, ext in
             (("input", "inputs", ".json"), ("jsonl", "runs", ".jsonl"),
              ("log", "runs", ".log"))}
    evidence[name] = {kind: {"path": str(path),
                             "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
                      for kind, path in files.items()}

result = {
    "status": "FAIL", "unity": "6000.2.9f1",
    "project": r"G:\unity\darwin-s-farm\Task9_Unity_Isolated_20261016",
    "switch": "useCompensatedEvolutionFoodGate", "defaultEnabled": False,
    "economyCostsDisabled": True,
    "color": {"status": "PASS", "detail": "formal conversion and 500-day survival", **color},
    "mushroom": {"status": "FAIL", "detail": "formal conversion succeeded; target extinct by day 512",
                 **mushroom, "day425PredatorTrials": predator_probe["predatorTrials"],
                 "highDensitySafety": "FAIL: full-density prey renewal zero; lower-density trials passed, but real conversion collapsed"},
    "negativeControls": {"status": "PASS" if negative_pass else "FAIL",
                         "cases": negatives},
    "regression": {"status": "FAIL", "strictStage0DoubleHash": "FAIL",
                   "stage0Ecology": "PASS", "threeSpecies": "PASS",
                   "readOnly": "PASS", "material": regression["material"],
                   "performance": "PASS",
                   "detailFile": str(OUT / "TASK9_ACCEPTANCE_REGRESSION.json")},
    "predatorGateSpec": {"status": "FAIL",
                         "detail": "no isolated short high-density counterfactual before enablement"},
    "rawEvidence": evidence}

(OUT / "TASK9_ACCEPTANCE_RESULT.json").write_text(
    json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
(OUT / "TASK9_ACCEPTANCE_REGRESSION.json").write_text(
    json.dumps(regression, ensure_ascii=False, indent=2), encoding="utf-8")
with (OUT / "TASK9_FAILURE_WINDOW.csv").open(
        "w", encoding="utf-8-sig", newline="") as handle:
    writer = csv.writer(handle)
    writer.writerow(("day", "chicken", "civet", "chickenBirths", "chickenDeaths",
                     "chickenPredationDeaths", "civetBirths", "civetDeaths",
                     "civetPredationDeaths", "civetReserve", "plantStock",
                     "preyQFullDensity", "passingPredatorTrials"))
    by = daily("task9-mushroom-formal")
    for day in range(420, 513):
        tile = origin(by[day])
        source = next((p for p in tile["populations"]
                       if p["species"] == mushroom["source"]), None)
        child = next((p for p in tile["populations"]
                      if p["species"] == mushroom["target"]), None)
        probe = source.get("t95", {}).get("finalProbe", {}) if source else {}
        trials = probe.get("predatorTrials") or []
        writer.writerow((day, source["count"] if source else 0,
                         child["count"] if child else 0,
                         source["births"] if source else 0,
                         source["deaths"] if source else 0,
                         source["predationDeaths"] if source else 0,
                         child["births"] if child else 0,
                         child["deaths"] if child else 0,
                         child["predationDeaths"] if child else 0,
                         child["reserve"] if child else 0, tile["stock"],
                         trials[0]["preyRenewal"] if trials else "",
                         sum(t["pass"] for t in trials) if trials else ""))
(OUT / "TASK9_FIRST_EVOLUTION.log").write_text(
    color["conversionLog"] + "\n" + mushroom["conversionLog"] + "\n",
    encoding="utf-8")
print("REPORT", OUT)
print("COLOR", color["firstConversionDay"], color["foundersAtConversion"],
      color["dayPlus500"])
print("MUSHROOM", mushroom["firstConversionDay"],
      mushroom["foundersAtConversion"], mushroom["lastLivingDay"],
      mushroom["dayPlus500"])
print("NEGATIVE", negative_pass, "MATERIAL", regression["material"])
