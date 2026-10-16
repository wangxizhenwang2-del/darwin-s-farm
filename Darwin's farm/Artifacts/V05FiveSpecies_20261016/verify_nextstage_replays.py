"""Compare complete, independently produced Unity replay logs; never simulate ecology."""
import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent
CASES = [
    "task1-normal-actions",
    "task2-1000-stable-color-cancel110-plant2",
    "task3-mushroom-stable-1000d",
]


def read_log(path):
    data = path.read_bytes()
    lines = data.decode("utf-8-sig").splitlines()
    header = lines[0]
    daily = [json.loads(line) for line in lines if line.startswith("{")]
    actions = [line for line in lines if line.startswith("action ")]
    events = [line for line in lines if line.startswith("event ")]
    return {
        "sha256": hashlib.sha256(data).hexdigest(),
        "header": header,
        "days": len(daily),
        "firstDay": daily[0]["day"],
        "lastDay": daily[-1]["day"],
        "dailyModelHashes": [item["modelHash"] for item in daily],
        "dailyEnvironmentHashes": [item["environmentHash"] for item in daily],
        "actions": actions,
        "events": events,
        "rejectedActions": [line for line in actions if "accepted=False" in line],
    }


def main():
    report = {}
    for name in CASES:
        paths = [ROOT / f"{name}-nextstage-run{i}.jsonl" for i in range(1, 4)]
        if not all(path.is_file() for path in paths):
            report[name] = {"status": "NOT_RUN", "files": [str(p) for p in paths]}
            continue
        runs = [read_log(path) for path in paths]
        key = ("header", "days", "firstDay", "lastDay", "dailyModelHashes",
               "dailyEnvironmentHashes", "actions", "events")
        equal = all(all(run[field] == runs[0][field] for field in key)
                    for run in runs[1:])
        no_reject = not any(run["rejectedActions"] for run in runs)
        expected_days = json.loads((ROOT / f"{name}.json").read_text(encoding="utf-8"))["endDay"]
        complete = all(run["firstDay"] == 0 and run["lastDay"] == expected_days
                       for run in runs)
        report[name] = {
            "status": "PASS" if equal and no_reject and complete else "FAIL",
            "sameModelAndEnvironmentAndActionsAndEvents": equal,
            "noRejectedActions": no_reject,
            "complete": complete,
            "endDay": expected_days,
            "runSha256": [run["sha256"] for run in runs],
            "bindingHeaders": [run["header"] for run in runs],
            "files": [str(path) for path in paths],
        }
    destination = ROOT / "nextstage-fixed-validation.json"
    destination.write_text(json.dumps(report, ensure_ascii=False, indent=2),
                           encoding="utf-8")
    print(json.dumps({name: entry["status"] for name, entry in report.items()},
                     ensure_ascii=False))
    print(destination)


if __name__ == "__main__":
    main()
