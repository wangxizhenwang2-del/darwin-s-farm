"""Adapt historical player action routes to the current separate S/R tools.

These are new paid V5.1 routes from the real 160-chicken sandbox origin;
they are deliberately not claimed equivalent to the old combined-tool runs.
"""
import json
from pathlib import Path

root = Path(__file__).resolve().parent
old = root.parent / "V05DynamicK_Continuation_20261016" / "inputs"
for route in ("color500", "mushroom1000", "deer1900", "peacock900"):
    data = json.loads((old / (f"formal-regression-{route}.json")).read_text(
        encoding="utf-8"))
    name = "stage6-player-" + route
    data.update(scenarioId=name, restrictedV05=False, useV05Sandbox=True,
                disableCosts=False, enablePreyDensityFeedback=True,
                enablePaidBirthStress=True, enablePriorityPlantBudgets=True,
                adultMaintenanceScale=0.4,
                reproductionStressPerBirth=0.8)
    data.pop("originInitialStock", None)
    data.pop("originInitialChickenCount", None)
    translated = []
    for action in data["actions"]:
        kinds = ("PlantStockChange", "PlantRecoveryChange") if \
            action["kind"] == "PlantChange" else (action["kind"],)
        for kind in kinds:
            translated.append(dict(action, kind=kind,
                                   sequence=len(translated)))
    data["actions"] = translated
    (root / "inputs" / (name + ".json")).write_text(
        json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
