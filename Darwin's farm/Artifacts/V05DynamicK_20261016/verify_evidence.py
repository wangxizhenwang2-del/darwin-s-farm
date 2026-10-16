"""Verify persisted Unity evidence, version binding and read-only comparisons."""
import hashlib
import json
import math
from pathlib import Path

B = Path(__file__).resolve().parent
ROOT = B.parent.parent
names = json.loads((B / "FORMAL_CASES.json").read_text(encoding="utf8"))
validation = json.loads((B / "DETERMINISM_VALIDATION.json").read_text(encoding="utf8"))
summaries = json.loads((B / "RESULT_SUMMARY.json").read_text(encoding="utf8"))
manifest = json.loads((B / "checkpoint-final-candidate/sha256.json").read_text(encoding="utf8"))
mismatched = [name for name, sha in manifest.items()
              if not (ROOT / name).exists() or
              hashlib.sha256((ROOT / name).read_bytes()).hexdigest() != sha]
assert not mismatched, mismatched

def read(name, run=1):
    path = B / "runs" / (name + "-run" + str(run) + ".jsonl")
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    return lines, [json.loads(line) for line in lines if line.startswith("{")]

def finite(value):
    if isinstance(value, dict):
        return all(finite(v) for v in value.values())
    if isinstance(value, list):
        return all(finite(v) for v in value)
    return not isinstance(value, float) or math.isfinite(value)

days = 0
seconds = 0
expected_failures = []
for name in names:
    v = validation[name]
    assert v["fullJsonlIdentical"] and v["dailyJsonIdentical"]
    assert v["allUnityReplaysCompleted"]
    scenario = json.loads((B / "inputs" / (name + ".json")).read_text(encoding="utf8"))
    for run in (1, 2, 3):
        lines, rows = read(name, run)
        assert {r["day"] for r in rows} == set(range(scenario["endDay"] + 1))
        assert len(rows) == scenario["endDay"] + 1 + len(scenario.get("actions", []))
        assert all(finite(row) for row in rows)
        assert "random=DF-FNV64-1" in lines[0] and "unity=6000.2.9f1" in lines[0]
        rules = json.loads(lines[1].split("=", 1)[1])
        assert rules["preyEnergyPerSize"] == 40 and rules["smallPlantSizeScale"] == 20
        assert abs(rules["birthRate"] - 0.1) < 1e-6
        assert rules["evolutionTraitTolerance"] == 5
        s = summaries[name + "-run" + str(run)]
        assert not s["unpaidBirthViolations"]
        assert not s["physicalPlantResourceViolations"]
        assert s["maxRelativeMaterialResidual"] < 1e-6
        assert s["execution"]["replayCompleted"]
        if not s["execution"]["unityReplayOK"]:
            assert s["execution"]["actionRejections"]
            expected_failures.append([name, run])
        days += scenario["endDay"]
        seconds += s["execution"]["seconds"]
    validation[name]["status"] = "PASS"

_, on = read("formal-readonly-on")
_, off = read("formal-readonly-off")
assert on == off, "Read-only diagnostics changed daily state or environment"

def population_signature(row):
    return sorted((tile["x"], tile["y"], p["id"], p["species"],
                   *(p[k] for k in ("count", "fitT", "fitH", "movement", "size",
                                    "fertility", "births", "deaths", "body", "reserve")))
                  for tile in row["tiles"] for p in tile["populations"])

_, legacy = read("formal-selection-legacyK")
_, candidate = read("formal-selection-foodK")
first_difference = next((a["day"] for a, b in zip(legacy, candidate)
                         if population_signature(a) != population_signature(b)), None)
result = {
    "status": "PASS", "formalCases": len(names), "independentUnityProcesses": len(names)*3,
    "formalSimulatedDays": days, "unityProcessSeconds": round(seconds, 3),
    "wholeJsonlAndDailyLogsIdenticalWithinEachInput": True,
    "readonlyOnOffDailyJsonModelAndEnvironmentIdentical": True,
    "readonlyComparisonDays": len(on),
    "selectionABFirstPopulationOrTraitDifferenceDay": first_difference,
    "selectionABSamePopulationAndTraitTrajectory": first_difference is None,
    "expectedActionRejectionProcesses": expected_failures,
    "modelResourceManifestMatchesCurrentProject": True,
    "frozen40_20AndFormalTolerance5CheckedAcrossAllRuns": True,
    "worldCheckpointRestore": "BLOCKED", "cacheHits": 0, "savedSimulationDays": 0,
    "overallEcologicalTask": "BLOCKED",
    "note": "Deterministic failures are not ecological passes; payment prototype not merged."
}
(B / "FINAL_VERIFICATION.json").write_text(json.dumps(result, indent=2), encoding="utf8")
(B / "DETERMINISM_VALIDATION.json").write_text(
    json.dumps(validation, indent=2), encoding="utf8")
print(json.dumps(result, ensure_ascii=False, indent=2))
