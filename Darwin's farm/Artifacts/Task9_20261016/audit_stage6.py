"""Summarize paid Unity player routes, including rejected actions."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parent
runs = root / "runs"
routes = {"color500": "彩鸡", "mushroom1000": "灵感菇狸",
          "deer1900": "鹿", "peacock900": "孔雀"}
summary = []
for suffix, target in routes.items():
    name = "stage6-player-" + suffix
    text = (runs / (name + ".jsonl")).read_text(encoding="utf-8-sig")
    records = [json.loads(line) for line in text.splitlines()
               if line.startswith("{")]
    actions = [line for line in text.splitlines() if line.startswith("action day=")]
    rejected = [line for line in actions if "accepted=False" in line]
    first_target = next((row["day"] for row in records if any(
        p["species"] == target for tile in row["tiles"]
        for p in tile["populations"])), None)
    final = {}
    for tile in records[-1]["tiles"]:
        for population in tile["populations"]:
            species = population["species"]
            final[species] = final.get(species, 0) + population["count"]
    execution = json.loads((runs / (name + ".execution.json")).read_text(
        encoding="utf-8"))
    assert execution["replayCompleted"] and records[-1]["day"] == int(
        re.search(r"\d+$", suffix).group()), name
    assert all(row["totalMaterial"] >= 0 for row in records)
    summary.append({"scenario": name, "target": target,
        "firstTargetDay": first_target, "final": final,
        "balanceAtEnd": records[-1]["balance"],
        "actions": len(actions), "rejectedActions": len(rejected),
        "firstRejection": rejected[0] if rejected else None,
        "exitCode": execution["exitCode"],
        "routePassed": first_target is not None and not rejected})
(root / "STAGE6_SUMMARY.json").write_text(
    json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
for item in summary:
    print(item["scenario"], "target day", item["firstTargetDay"],
          "actions", item["actions"], "rejected", item["rejectedActions"],
          "end balance", item["balanceAtEnd"])
print("PASS stage6 evidence audit; route success is recorded per scenario")
