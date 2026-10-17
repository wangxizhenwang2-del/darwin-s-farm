"""Independent 5000-day repeat and narrow parameter neighbors."""
import json
from pathlib import Path

root = Path(__file__).resolve().parent / "inputs"
standard = json.loads((root / "stage2-civet50-day5000.json").read_text(encoding="utf-8"))
variants = {
    "stage2-civet50-day5000-repeat": {},
    "stage2-civet50-day5000-scale035": {"adultMaintenanceScale": 0.35},
    "stage2-civet50-day5000-scale045": {"adultMaintenanceScale": 0.45},
    "stage2-civet50-day5000-stress070": {"reproductionStressPerBirth": 0.7},
    "stage2-civet50-day5000-stress090": {"reproductionStressPerBirth": 0.9},
}
for name, overrides in variants.items():
    data = dict(standard, scenarioId=name, **overrides)
    (root / (name + ".json")).write_text(
        json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
