"""Sequential real-Unity long runs; stops at the first execution failure."""
import json
from pathlib import Path
import subprocess
import sys

BASE = Path(__file__).resolve().parent
cases = [f"stage2-civet{count}-day{days}" for days in (2000, 5000)
         for count in (10, 15, 20, 50, 100, 300, 500)]
for index, name in enumerate(cases, 1):
    evidence = BASE / "runs" / (name + ".execution.json")
    if evidence.exists():
        record = json.loads(evidence.read_text(encoding="utf-8"))
        if record.get("replayCompleted") and record.get("exitCode") == 0:
            print(f"retained {index}/{len(cases)} {name}", flush=True)
            continue
    print(f"starting {index}/{len(cases)} {name}", flush=True)
    result = subprocess.run([sys.executable, str(BASE / "run_case.py"),
        str(BASE / "inputs" / (name + ".json")),
        "--tag", f"Stage 2 长期群落 {index}/{len(cases)}"])
    if result.returncode:
        print(f"STOPPED {name}", flush=True)
        raise SystemExit(result.returncode)
print("STAGE2_BATCH_COMPLETE", flush=True)
