"""Mirror tool fixtures on the existing V5.1 desert sandbox scene."""
import json
from pathlib import Path

root = Path(__file__).resolve().parent / "inputs"
names = ["stage4-origin"]
for kind in ("PlantStockChange", "PlantRecoveryChange"):
    names += [f"stage4-{kind}-tier{tier}" for tier in (1, 2, -1, -2)]
names += ["stage4-TemperatureChange", "stage4-HumidityChange",
          "stage4-ElevationChange", "stage4-two-slots"]
for name in names:
    source = json.loads((root / (name + ".json")).read_text(encoding="utf-8"))
    new = "stage4-v05-" + name.removeprefix("stage4-")
    source["scenarioId"] = new
    source["useV05Sandbox"] = True
    (root / (new + ".json")).write_text(
        json.dumps(source, ensure_ascii=False, indent=2), encoding="utf-8")
