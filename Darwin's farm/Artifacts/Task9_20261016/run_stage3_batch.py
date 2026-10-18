"""Sequential real-Unity terrain and disaster batch."""
import json
from pathlib import Path
import subprocess
import sys

BASE = Path(__file__).resolve().parent
cases = [f"stage3-disaster{cut}" for cut in (10, 20, 50)] + [
    f"stage3-habitat{index:02d}" for index in range(27)]
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
        "--tag", f"Stage 3 环境/灾害 {index}/{len(cases)}"])
    if result.returncode:
        print(f"STOPPED {name}", flush=True)
        raise SystemExit(result.returncode)
print("STAGE3_BATCH_COMPLETE", flush=True)
