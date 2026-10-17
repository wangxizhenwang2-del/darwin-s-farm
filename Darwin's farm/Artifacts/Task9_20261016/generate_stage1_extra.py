"""Three-group same-lane order counterfactual for Unity Stage 1C."""
import json
from pathlib import Path

root = Path(__file__).resolve().parent / "inputs"
source = json.loads((root / "stage1-C-both-short.json").read_text(encoding="utf-8"))
color = {"species": "Assets/BiowebV46/Species/彩鸡-b59a4a82.asset",
         "count": 1000}
source["tiles"][0]["populations"].insert(1, color)
source["fixtureSkipDiagnostics"] = False
for suffix, reverse in (("forward", False), ("reverse", True)):
    data = json.loads(json.dumps(source, ensure_ascii=False))
    data["scenarioId"] = "stage1-C-three-" + suffix
    if reverse:
        data["tiles"][0]["populations"].reverse()
    (root / (data["scenarioId"] + ".json")).write_text(
        json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
