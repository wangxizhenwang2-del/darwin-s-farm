"""Sequential real Unity player-tool replays."""
import json
from pathlib import Path
import subprocess
import sys

base = Path(__file__).resolve().parent
names = ["stage4-origin"]
for kind in ("PlantStockChange", "PlantRecoveryChange"):
    names += [f"stage4-{kind}-tier{tier}" for tier in (1, 2, -1, -2)]
names += ["stage4-TemperatureChange", "stage4-HumidityChange",
          "stage4-ElevationChange", "stage4-two-slots"]
for index, name in enumerate(names, 1):
    evidence = base / "runs" / (name + ".execution.json")
    if evidence.exists():
        record = json.loads(evidence.read_text(encoding="utf-8"))
        if record.get("replayCompleted") and record.get("exitCode") == 0:
            print(f"retained {index}/{len(names)} {name}", flush=True)
            continue
    print(f"starting {index}/{len(names)} {name}", flush=True)
    result = subprocess.run([sys.executable, str(base / "run_case.py"),
        str(base / "inputs" / (name + ".json")),
        "--tag", f"Stage 4 五工具 {index}/{len(names)}"])
    if result.returncode:
        print(f"STOPPED {name}", flush=True)
        raise SystemExit(result.returncode)
print("STAGE4_BATCH_COMPLETE", flush=True)
