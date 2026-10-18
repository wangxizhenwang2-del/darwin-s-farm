"""Run one Task 12 scenario in a fresh real Unity Editor process.

Example: python replay/run_unity.py replay/inputs/int-color-s2.json
The historical isolated project is the initial baseline; --project accepts a
different isolated Unity checkout after a production code change is staged.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time

DEFAULT_ROOT = Path(r"G:\unity\darwin-s-farm\Darwin's farm")
DEFAULT_PROJECT = Path(r"G:\unity\darwin-s-farm\Task9_Unity_Isolated_20261016")
UNITY = Path(r"G:\unity\6000.2.9f1\Editor\Unity.exe")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("scenario", type=Path)
    parser.add_argument("--project", type=Path, default=DEFAULT_PROJECT)
    parser.add_argument("--root", type=Path, default=DEFAULT_ROOT)
    parser.add_argument("--output-dir", type=Path,
                        default=Path(__file__).resolve().parent.parent / "runs")
    parser.add_argument("--timeout", type=int, default=1200)
    args = parser.parse_args()
    scenario = args.scenario.resolve()
    project = args.project.resolve()
    output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    stem = output / scenario.stem
    log = stem.with_suffix(".log")
    jsonl = stem.with_suffix(".jsonl")
    progress = stem.with_suffix(".progress.txt")
    execution = stem.with_suffix(".execution.json")
    for path in (log, jsonl, progress, execution):
        path.unlink(missing_ok=True)
    config = json.loads(scenario.read_text(encoding="utf-8"))
    env = os.environ.copy()
    env.update(DF_V048_SCENARIO=str(scenario), DF_V048_OUTPUT=str(jsonl),
               DF_TASK9_PROGRESS=str(progress))
    start = time.monotonic()
    try:
        process = subprocess.run(
            [str(UNITY), "-batchmode", "-nographics", "-projectPath", str(project),
             "-executeMethod", "V048ScenarioReplay.Run", "-logFile", str(log)],
            cwd=args.root, env=env, timeout=args.timeout,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        error = None
        code = process.returncode
    except subprocess.TimeoutExpired as exc:
        error = str(exc)
        code = None
    content = log.read_text(encoding="utf-8", errors="replace") if log.exists() else ""
    result = dict(scenarioId=config.get("scenarioId"), seed=config.get("seed"),
                  endDay=config.get("endDay"), scenario=str(scenario),
                  inputSha256=hashlib.sha256(scenario.read_bytes()).hexdigest(),
                  project=str(project), unity=str(UNITY), exitCode=code,
                  replayCompleted="V048_REPLAY_COMPLETE " in content,
                  unityReplayOK="V048_REPLAY_OK " in content,
                  seconds=round(time.monotonic()-start, 3), error=error,
                  log=str(log), jsonl=str(jsonl))
    for key, path in (("logSha256", log), ("jsonlSha256", jsonl)):
        if path.exists():
            result[key] = hashlib.sha256(path.read_bytes()).hexdigest()
    execution.write_text(json.dumps(result, ensure_ascii=False, indent=2),
                         encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False), flush=True)
    if code != 0 or not result["unityReplayOK"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
