"""Real Unity five-tool receipts on the V5.1 desert sandbox."""
import json
from pathlib import Path
import subprocess
import sys

base = Path(__file__).resolve().parent
names = ["stage4-v05-origin"]
for kind in ("PlantStockChange", "PlantRecoveryChange"):
    names += [f"stage4-v05-{kind}-tier{tier}" for tier in (1, 2, -1, -2)]
names += ["stage4-v05-TemperatureChange", "stage4-v05-HumidityChange",
          "stage4-v05-ElevationChange", "stage4-v05-two-slots"]
for index, name in enumerate(names, 1):
    evidence = base / "runs" / (name + ".execution.json")
    if evidence.exists():
        record = json.loads(evidence.read_text(encoding="utf-8"))
        valid_origin = True
        if name == "stage4-v05-origin":
            trace = (base / "runs" / (name + ".jsonl")).read_text(
                encoding="utf-8-sig")
            initial = next(json.loads(line) for line in trace.splitlines()
                           if line.startswith("{"))
            valid_origin = any(p["species"] == "鸡" and p["count"] == 160
                               for p in initial["tiles"][0]["populations"])
        if record.get("replayCompleted") and record.get("exitCode") == 0 and valid_origin:
            print(f"retained {index}/{len(names)} {name}", flush=True)
            continue
    print(f"starting {index}/{len(names)} {name}", flush=True)
    result = subprocess.run([sys.executable, str(base / "run_case.py"),
        str(base / "inputs" / (name + ".json")),
        "--tag", f"Stage 4 V5.1 荒漠工具 {index}/{len(names)}"])
    if result.returncode:
        print(f"STOPPED {name}", flush=True)
        raise SystemExit(result.returncode)
print("STAGE4_V05_COMPLETE", flush=True)
