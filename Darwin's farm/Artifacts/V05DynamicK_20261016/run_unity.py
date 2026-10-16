"""Launch the real Unity M-layer replay. This contains no ecological model."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time

BASE = Path(__file__).resolve().parent
UNITY = Path(r"G:\unity\6000.2.9f1\Editor\Unity.exe")
DEFAULT_PROJECT = BASE.parent.parent

parser = argparse.ArgumentParser()
parser.add_argument("cases", nargs="+")
parser.add_argument("--runs", type=int, default=1)
parser.add_argument("--project", type=Path, default=DEFAULT_PROJECT)
args = parser.parse_args()
out = BASE / "runs"
out.mkdir(exist_ok=True)
for case in args.cases:
    scenario = BASE / "inputs" / (case + ".json")
    for run in range(1, args.runs + 1):
        if (BASE / "STOP_AFTER_CURRENT").exists():
            raise SystemExit("Stopped before starting the next Unity process.")
        stem = out / (case + "-run" + str(run))
        env = os.environ.copy()
        env["DF_V048_SCENARIO"] = str(scenario)
        env["DF_V048_OUTPUT"] = str(stem.with_suffix(".jsonl"))
        start = time.time()
        process = subprocess.run(
            [str(UNITY), "-batchmode", "-nographics", "-projectPath",
             str(args.project), "-executeMethod", "V048ScenarioReplay.Run",
             "-logFile", str(stem.with_suffix(".log"))],
            env=env, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
            timeout=600)
        log = stem.with_suffix(".log").read_text(encoding="utf8", errors="replace")
        evidence = {
            "scenario": case, "run": run, "project": str(args.project),
            "exitCode": process.returncode,
            "unityReplayOK": "V048_REPLAY_OK " in log,
            "replayCompleted": "V048_REPLAY_COMPLETE " in log,
            "actionRejections": "player actions were rejected" in log,
            "seconds": round(time.time() - start, 3),
            "inputSha256": hashlib.sha256(scenario.read_bytes()).hexdigest(),
        }
        stem.with_suffix(".execution.json").write_text(
            json.dumps(evidence, indent=2), encoding="utf8")
        print(json.dumps(evidence), flush=True)
        if not (evidence["unityReplayOK"] or
                (evidence["replayCompleted"] and evidence["actionRejections"])):
            raise SystemExit("Unity replay failed; inspect " + str(stem))
