"""Final old-mode Unity reruns after all code changes."""
import json
from pathlib import Path

root = Path(__file__).resolve().parent / "inputs"
for suffix in ("deer10-rich", "predator"):
    data = json.loads((root / ("stage0-" + suffix + ".json")).read_text(
        encoding="utf-8"))
    name = "stage0-final-" + suffix
    data["scenarioId"] = name
    (root / (name + ".json")).write_text(
        json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
