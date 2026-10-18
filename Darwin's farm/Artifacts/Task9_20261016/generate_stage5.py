"""Read-only predator K on/off Unity counterfactual inputs."""
import json
from pathlib import Path

root = Path(__file__).resolve().parent / "inputs"
base = json.loads((root / "stage2-civet50-day5000.json").read_text(encoding="utf-8"))
for enabled in (False, True):
    name = "stage5-predatorK-" + ("on" if enabled else "off")
    data = dict(base, scenarioId=name, endDay=100,
                recordPredatorFoodK=enabled)
    (root / (name + ".json")).write_text(
        json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
