"""Read-only comparison of complete logs produced by independent Unity processes."""

import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent
CASES = (
    "task4-c1-slope-climate",
    "task4-c2-grass",
    "next-peacock-release-support",
    "next-deer-early-size-support",
)


def read(path):
    data = path.read_bytes()
    lines = data.decode("utf-8-sig").splitlines()
    days = [json.loads(line) for line in lines if line.startswith("{")]
    return {
        "sha256": hashlib.sha256(data).hexdigest(),
        "binding": lines[0].split("bindingSha256=")[-1],
        "firstDay": days[0]["day"],
        "lastDay": days[-1]["day"],
        "modelHashes": [day["modelHash"] for day in days],
        "environmentHashes": [day["environmentHash"] for day in days],
        "actions": [line for line in lines if line.startswith("action ")],
        "events": [line for line in lines if line.startswith("event ")],
    }


def main():
    result = {}
    for name in CASES:
        paths = [ROOT / f"{name}-final-run{i}.jsonl" for i in (1, 2, 3)]
        if not all(path.exists() for path in paths):
            result[name] = {"determinism": "NOT_RUN"}
            continue
        runs = [read(path) for path in paths]
        end_day = json.loads((ROOT / f"{name}.json").read_text(encoding="utf-8"))["endDay"]
        same = all({k: v for k, v in run.items() if k != "sha256"} ==
                   {k: v for k, v in runs[0].items() if k != "sha256"}
                   for run in runs[1:])
        bytes_same = len({run["sha256"] for run in runs}) == 1
        complete = all(run["firstDay"] == 0 and run["lastDay"] == end_day
                       for run in runs)
        accepted = all("accepted=False" not in action
                       for run in runs for action in run["actions"])
        result[name] = {
            "determinism": "PASS" if same and bytes_same and complete and accepted else "FAIL",
            "byteIdentical": bytes_same,
            "dailyModelAndEnvironmentIdentical": same,
            "allActionsAccepted": accepted,
            "completeFromDayZero": complete,
            "lastDay": end_day,
            "bindingSha256": runs[0]["binding"],
            "runSha256": [run["sha256"] for run in runs],
            "inputs": str(ROOT / f"{name}.json"),
            "outputs": [str(path) for path in paths],
        }
    target = ROOT / "nextstage-final-validation.json"
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({name: row["determinism"] for name, row in result.items()},
                     ensure_ascii=False))


if __name__ == "__main__":
    main()
