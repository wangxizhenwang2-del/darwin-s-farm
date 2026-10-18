"""Make real WhiteBox player-tool replay inputs; no ecology is simulated here."""
import json
from pathlib import Path

base = Path(__file__).resolve().parent / "inputs"
common = {"seed": 240, "disableCosts": False, "restrictedV05": False,
          "mechanismFixture": False, "tiles": [], "actions": [],
          "enablePreyDensityFeedback": True, "enablePaidBirthStress": True,
          "enablePriorityPlantBudgets": True,
          "reproductionStressPerBirth": 0.8,
          "adultMaintenanceScale": 0.4}


def save(name, days, actions):
    data = dict(common, scenarioId=name, endDay=max(1, days), actions=actions)
    (base / (name + ".json")).write_text(
        json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


save("stage4-origin", 0, [])
for kind, days in (("PlantStockChange", 0),
                   ("PlantRecoveryChange", 101)):
    for tier in (1, 2, -1, -2):
        save(f"stage4-{kind}-tier{tier}", days,
             [{"day": 0, "sequence": 0, "x": 0, "y": 0,
               "kind": kind, "tier": tier}])
for kind in ("TemperatureChange", "HumidityChange"):
    save(f"stage4-{kind}", 326,
         [{"day": 0, "sequence": 0, "x": 0, "y": 0,
           "kind": kind, "tier": 1}])
save("stage4-ElevationChange", 1,
     [{"day": 0, "sequence": 0, "x": 0, "y": 0,
       "kind": "ElevationChange", "tier": 1},
      {"day": 1, "sequence": 0, "x": 0, "y": 0,
       "kind": "ElevationChange", "tier": -1}])
save("stage4-two-slots", 1,
     [{"day": 0, "sequence": 0, "x": 0, "y": 0,
       "kind": "TemperatureChange", "tier": 1},
      {"day": 0, "sequence": 1, "x": 0, "y": 0,
       "kind": "HumidityChange", "tier": 1},
      {"day": 0, "sequence": 2, "x": 0, "y": 0,
       "kind": "PlantRecoveryChange", "tier": 1}])
