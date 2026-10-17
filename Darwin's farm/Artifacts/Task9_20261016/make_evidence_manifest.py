"""Hash immutable Task 9 inputs and real Unity outputs for traceability."""
import hashlib
import json
from pathlib import Path

base = Path(__file__).resolve().parent
main = base.parents[1]
isolated = Path(r"G:\unity\darwin-s-farm\Task9_Unity_Isolated_20261016")
paths = sorted((base / "inputs").glob("*.json"))
paths += sorted(p for p in (base / "runs").iterdir()
                if p.suffix in {".jsonl", ".log", ".json"})
paths += sorted(base.glob("STAGE*_SUMMARY.json"))
sources = [
    "Assets/Scripts/Simulation/LandRuleTuning.cs",
    "Assets/Scripts/Simulation/LandLifeRules.cs",
    "Assets/Scripts/BioData/BlockInfo.cs",
    "Assets/Scripts/BioData/PopulationData.cs",
    "Assets/Scripts/map/MapSimulationBridge.cs",
    "Assets/Scripts/Simulation/SimulationController.cs",
    "Assets/Scripts/Simulation/Editor/V048ScenarioReplay.cs",
    "Assets/Scripts/Simulation/Editor/EnvironmentFiveToolsAudit.cs",
]

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

assert all(sha(main / source) == sha(isolated / source) for source in sources)
files = [{"path": p.relative_to(base).as_posix(), "bytes": p.stat().st_size,
          "sha256": sha(p)} for p in paths]
manifest = {
    "unityVersion": "6000.2.9f1",
    "inputCount": sum(p.parent.name == "inputs" for p in paths),
    "jsonlCount": sum(p.suffix == ".jsonl" for p in paths),
    "logCount": sum(p.suffix == ".log" for p in paths),
    "mainMatchesIsolatedForAllChangedSources": True,
    "sources": [{"path": source, "sha256": sha(main / source)} for source in sources],
    "files": files,
}
(base / "EVIDENCE_MANIFEST.json").write_text(
    json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
print("PASS evidence manifest", manifest["inputCount"],
      manifest["jsonlCount"], manifest["logCount"])
