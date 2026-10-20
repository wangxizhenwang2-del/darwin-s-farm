"""Local test driver; all Editor actions go through the installed Unity CLI."""
import json
import subprocess
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")

OUT = Path(__file__).resolve().parent
PROJECT = OUT.parent.parent / "Darwin's farm"
UNITY = str(Path.home() / "AppData/Local/Unity/bin/unity.exe")

def command(name, *, label=None, **params):
    argv = [UNITY, "command", name, "--caller", "plugin", "--skill", "unity-cli", "--format", "json", "--timeout", "120"]
    if params:
        argv += ["--"]
        for key, value in params.items():
            argv += ["--" + key, str(value).lower() if isinstance(value, bool) else str(value)]
    result = subprocess.run(argv, cwd=PROJECT, text=True, encoding="utf-8", capture_output=True, timeout=135)
    try:
        envelope = json.loads(result.stdout)
    except Exception:
        raise RuntimeError(result.stdout + result.stderr)
    if label:
        (OUT / (label + "-command.json")).write_text(json.dumps(envelope, ensure_ascii=False, indent=2), encoding="utf-8")
    inner = envelope.get("data", {}).get("result", {}) if envelope.get("data") else {}
    if not envelope.get("success") or (isinstance(inner, dict) and inner.get("success") is False):
        raise RuntimeError(json.dumps(envelope, ensure_ascii=False))
    return inner

def run(entry, args, label):
    result = command("run_script", label=label, file=str(OUT / "GameplayAudit.cs"), entry="GameplayAudit."+entry,
                     args=json.dumps(args), timeout_ms=110000)
    if isinstance(result, dict) and isinstance(result.get("result"), str):
        result = json.loads(result["result"])
    if entry == "Tutorial":
        failures=[p["page"] for p in result["checks"] if not(p["categoryHit"] and p["pageHit"] and p["content"] and p["reset"]) or p["clippedByContent"]]
        print(json.dumps({"label":label,"pages":result["pages"],"failures":failures,
                          **{k:result[k] for k in ("titleTutorialPauses","returnedTitle","autoTutorialShown","gameplay","pauseTutorialPauses","returnsPause","resumes")}},ensure_ascii=False),flush=True)
    elif entry == "Controls":
        print(json.dumps({"label":label,"checks":result["checks"]},ensure_ascii=False),flush=True)
    else:
        print(json.dumps({"label":label,**{k:result[k] for k in ("firstLoss","minimumPopulation","maxSpecies")},
                          "day":result["final"]["day"],"living":result["final"]["living"],"events":result["events"][:15]},ensure_ascii=False),flush=True)

if __name__ == "__main__":
    mode = sys.argv[1]
    if mode == "tutorial":
        run("Tutorial", [int(sys.argv[2])], "round"+sys.argv[2]+"-tutorial")
    elif mode == "controls":
        run("Controls", [int(sys.argv[2])], "round"+sys.argv[2]+"-controls")
    elif mode == "difficulty":
        round_number, seed = int(sys.argv[2]), int(sys.argv[3])
        for policy in sys.argv[4:]:
            run("Difficulty",[policy, seed, 3600, round_number],f"round{round_number}-{policy}-seed{seed}")
    elif mode == "command":
        print(json.dumps(command(sys.argv[2],**json.loads(sys.argv[3])),ensure_ascii=False))
