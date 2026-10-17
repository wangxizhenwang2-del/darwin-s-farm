"""Run each independent integrated Unity replay once, preserving every result."""
import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
OLD = ROOT / "Artifacts" / "Task9_20261016"
INPUTS = HERE / "inputs"
ORDER = (
    "int-s0-predator",
    "int-a-off", "int-a-on", "int-b-off", "int-b-on",
    "int-c-off", "int-c-on", "int-d-off", "int-d-on",
    "int-color-s1", "int-color-s2",
    "int-mushroom-s1", "int-mushroom-s2", "int-mushroom-stop-s2",
    "int-gate-negative-color", "int-gate-negative-mushroom",
    "int-neg-zero-r", "int-neg-half-r", "int-neg-no-legal-prey",
    "int-neg-few-prey", "int-neg-many-hunters", "int-neg-two-hunters",
    "int-d-off-repeat", "int-d-on-repeat",
)
RESULT = HERE / "matrix_execution.json"
results = json.loads(RESULT.read_text(encoding="utf-8")) if RESULT.exists() else {}
for number, name in enumerate(ORDER, 1):
    execution = OLD / "runs" / (name + ".execution.json")
    if name in results and results[name].get("exitCode") == 0 and execution.exists():
        print(number, len(ORDER), name, "REUSED", flush=True)
        continue
    cmd = [sys.executable, str(OLD / "run_case.py"), str(INPUTS / (name + ".json")),
           "--tag", f"联合验收 {number}/{len(ORDER)} {name}"]
    done = subprocess.run(cmd, cwd=ROOT, text=True, capture_output=True,
                          encoding="utf-8", errors="replace")
    data = json.loads(execution.read_text(encoding="utf-8")) if execution.exists() else {}
    data["runnerExitCode"] = done.returncode
    data["runnerOutput"] = done.stdout.strip()[-600:]
    data["runnerError"] = done.stderr.strip()[-600:]
    results[name] = data
    RESULT.write_text(json.dumps(results, ensure_ascii=False, indent=2),
                      encoding="utf-8")
    print(number, len(ORDER), name, "PASS" if done.returncode == 0 else "FAIL",
          data.get("seconds"), flush=True)
