"""Real Unity repeat and parameter neighbors; resumes passed cases."""
import json
from pathlib import Path
import subprocess
import sys

base = Path(__file__).resolve().parent
names = ["stage2-civet50-day5000-repeat",
         "stage2-civet50-day5000-scale035",
         "stage2-civet50-day5000-scale045",
         "stage2-civet50-day5000-stress070",
         "stage2-civet50-day5000-stress090"]
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
        "--tag", f"Stage 2 独立重复/邻点 {index}/{len(names)}"])
    if result.returncode:
        print(f"STOPPED {name}", flush=True)
        raise SystemExit(result.returncode)
print("STAGE2_SUPPLEMENT_COMPLETE", flush=True)
