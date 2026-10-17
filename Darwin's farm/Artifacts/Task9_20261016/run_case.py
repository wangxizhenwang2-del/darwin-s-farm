"""Run one real Unity replay and publish its live log to the status page."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time

BASE = Path(__file__).resolve().parent
UNITY = Path(r"G:\unity\6000.2.9f1\Editor\Unity.exe")
PROJECT = Path(r"G:\unity\darwin-s-farm\Task9_Unity_Isolated_20261016")
STATUS = BASE / "status.json"


def update(**fields):
    state = json.loads(STATUS.read_text(encoding="utf-8"))
    state.update(fields)
    temp = STATUS.with_suffix(".tmp")
    temp.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(temp, STATUS)


parser = argparse.ArgumentParser()
parser.add_argument("scenario", type=Path)
parser.add_argument("--tag", default="Unity 回放")
args = parser.parse_args()
scenario = args.scenario.resolve()
config = json.loads(scenario.read_text(encoding="utf-8"))
stem = BASE / "runs" / scenario.stem
stem.parent.mkdir(exist_ok=True)
log = stem.with_suffix(".log")
jsonl = stem.with_suffix(".jsonl")
progress = stem.with_suffix(".progress.txt")
for stale in (log, jsonl, progress, stem.with_suffix(".execution.json")):
    stale.unlink(missing_ok=True)
env = os.environ.copy()
env["DF_V048_SCENARIO"] = str(scenario)
env["DF_V048_OUTPUT"] = str(jsonl)
env["DF_TASK9_PROGRESS"] = str(progress)
update(active=f"{args.tag} · {scenario.stem}", activeLog=str(log),
       activeJsonl=str(jsonl), activeProgress=str(progress),
       targetDay=config["endDay"], day=0,
       message="真实 Unity 进程运行中")
start = time.time()
try:
    process = subprocess.run(
        [str(UNITY), "-batchmode", "-nographics", "-projectPath", str(PROJECT),
         "-executeMethod", "V048ScenarioReplay.Run", "-logFile", str(log)],
        env=env, timeout=1200,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    content = log.read_text(encoding="utf-8", errors="replace") if log.exists() else ""
    result = {
        "scenario": str(scenario), "project": str(PROJECT),
        "inputSha256": hashlib.sha256(scenario.read_bytes()).hexdigest(),
        "exitCode": process.returncode,
        "replayCompleted": "V048_REPLAY_COMPLETE " in content,
        "unityReplayOK": "V048_REPLAY_OK " in content,
        "seconds": round(time.time() - start, 3),
    }
    stem.with_suffix(".execution.json").write_text(
        json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    update(active="等待结果审计", activeLog=None, activeJsonl=None,
           activeProgress=None,
           day=None, targetDay=None,
           message=f"{scenario.stem}：{'完成' if result['replayCompleted'] else '失败，见日志'}")
    print(json.dumps(result, ensure_ascii=False), flush=True)
    if not result["replayCompleted"] or process.returncode != 0:
        raise SystemExit(1)
except BaseException as error:
    if not isinstance(error, SystemExit):
        update(state="BLOCKED", active="Unity 回放异常", message=str(error))
    raise
