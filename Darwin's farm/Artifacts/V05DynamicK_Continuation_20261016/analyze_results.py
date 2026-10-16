"""Summarize Unity evidence and plot recorded values; no ecology is simulated."""
from collections import defaultdict
import csv
import hashlib
import json
from pathlib import Path
import re

BASE = Path(__file__).resolve().parent
RUNS = BASE / "runs"
summaries = {}
sha_manifest = {}
for path in sorted(RUNS.glob("*-run*.jsonl")):
    execution_path = path.with_suffix(".execution.json")
    if not execution_path.exists():
        continue
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    rows = [json.loads(line) for line in lines if line.startswith("{")]
    if not rows:
        continue
    first, last = {}, {}
    for row in rows:
        first.setdefault(row["day"], row)
        last[row["day"]] = row
    births, deaths, predation = defaultdict(int), defaultdict(int), defaultdict(int)
    first_birth, extinction = {}, {}
    previous = set()
    for day, row in sorted(first.items()):
        alive = set()
        for tile in row["tiles"]:
            for pop in tile["populations"]:
                species = pop["species"]
                alive.add(species)
                if day:
                    births[species] += pop["births"]
                    deaths[species] += pop["deaths"]
                    predation[species] += pop["predationDeaths"]
                    if pop["births"]:
                        first_birth.setdefault(species, day)
        for species in previous - alive:
            extinction.setdefault(species, day)
        previous = alive
    residuals = []
    relative = []
    resource_violations = []
    for day, row in first.items():
        if day - 1 not in last or "accountedBiomassSinkToday" not in row:
            continue
        before = last[day - 1]
        error = (row["totalMaterial"] - before["totalMaterial"]
                 - row["plantGrowthToday"] + row["accountedBiomassSinkToday"])
        residuals.append(error)
        relative.append(abs(error) / max(1, before["totalMaterial"]
                                        + row["plantGrowthToday"]))
        previous_tiles = {(t["x"], t["y"]): t for t in before["tiles"]}
        for tile in row["tiles"]:
            key = (tile["x"], tile["y"])
            if key not in previous_tiles or "plantGrowth" not in tile:
                continue
            available = previous_tiles[key]["stock"] + tile["plantGrowth"]
            if (tile["consumedPlants"] > available + 0.1 or
                    tile["stock"] > available - tile["consumedPlants"] + 0.1):
                resource_violations.append([day, *key, available,
                                            tile["consumedPlants"], tile["stock"]])
    final = defaultdict(int)
    for tile in last[max(last)]["tiles"]:
        for pop in tile["populations"]:
            final[pop["species"]] += pop["count"]
    payment_violations = []
    for row in first.values():
        if not row["day"]:
            continue
        for tile in row["tiles"]:
            for pop in tile["populations"]:
                if "birthBudget" in pop and (pop["births"] * pop["newbornCost"]
                                            > pop["birthBudget"] + 0.1):
                    payment_violations.append([row["day"], pop["species"]])
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    sha_manifest[path.name] = digest
    binding = re.search(r"bindingSha256=(\w+)", lines[0])
    summary = {
        "days": max(last), "records": len(rows), "sha256": digest,
        "bindingSha256": binding.group(1) if binding else None,
        "final": dict(final), "births": dict(births), "deaths": dict(deaths),
        "predationDeaths": dict(predation), "firstBirth": first_birth,
        "firstDisappearance": extinction,
        "maxAbsMaterialResidual": max(map(abs, residuals), default=0),
        "maxRelativeMaterialResidual": max(relative, default=0),
        "unpaidBirthViolations": payment_violations,
        "physicalPlantResourceViolations": resource_violations,
        "events": [line for line in lines
                   if line.startswith(("event ", "ecology "))],
        "execution": json.loads(execution_path.read_text(encoding="utf8")),
        "counterfactuals": [line for line in lines
                           if line.startswith("counterfactual ")],
        "allocationAudits": [line for line in lines
                            if line.startswith("plantAllocationAudit ")],
    }
    summaries[path.stem] = summary
    if path.stem.endswith("-run1"):
        stem = path.with_suffix("")
        stem.with_suffix(".actions.log").write_text(
            "\n".join(line for line in lines if line.startswith("action ")) + "\n",
            encoding="utf8")
        stem.with_suffix(".events.log").write_text(
            "\n".join(summary["events"]) + "\n", encoding="utf8")
        with stem.with_suffix(".daily-hashes.tsv").open("w", encoding="utf8",
                                                        newline="") as stream:
            writer = csv.writer(stream, delimiter="\t")
            writer.writerow(["record", "day", "modelHash", "environmentHash"])
            for index, row in enumerate(rows):
                writer.writerow([index, row["day"], row["modelHash"],
                                 row["environmentHash"]])
        with stem.with_suffix(".population.csv").open("w", encoding="utf-8-sig",
                                                      newline="") as stream:
            writer = csv.writer(stream)
            writer.writerow(["day", "x", "y", "species", "count", "S", "R",
                             "foodK", "births", "deaths", "predationDeaths",
                             "allocatedFood", "adultDemand", "adultPaid",
                             "birthOpportunity", "birthBudget", "newbornCost",
                             "body", "reserve"])
            for day, row in sorted(last.items()):
                for tile in row["tiles"]:
                    for pop in tile["populations"]:
                        writer.writerow([day, tile["x"], tile["y"], pop["species"],
                                         pop["count"], tile["stock"], tile["recovery"]]
                                        + [pop.get(key) for key in
                                           ("foodK", "births", "deaths",
                                            "predationDeaths", "allocatedFood",
                                            "adultDemand", "adultPaid",
                                            "birthOpportunity", "birthBudget",
                                            "newbornCost", "body", "reserve")])

(BASE / "RESULT_SUMMARY.json").write_text(
    json.dumps(summaries, ensure_ascii=False, indent=2), encoding="utf8")
(BASE / "SHA256.json").write_text(json.dumps(sha_manifest, indent=2), encoding="utf8")

formal = {}
for key in summaries:
    if not key.endswith("-run1"):
        continue
    name = key[:-5]
    group = [summaries.get(name + "-run" + str(i)) for i in (1, 2, 3)]
    if not all(group):
        continue
    records = []
    for run in (1, 2, 3):
        records.append([line for line in
                        (RUNS / (name + "-run" + str(run) + ".jsonl"))
                        .read_text(encoding="utf-8-sig").splitlines()
                        if line.startswith("{")])
    formal[name] = {
        "fullJsonlIdentical": len({g["sha256"] for g in group}) == 1,
        "dailyJsonIdentical": records[0] == records[1] == records[2],
        "sha256": [g["sha256"] for g in group],
        "allUnityReplaysCompleted": all((lambda evidence:
            evidence.get("replayCompleted", evidence["unityReplayOK"]))(json.loads(
            (RUNS / (name + "-run" + str(run) + ".execution.json"))
            .read_text(encoding="utf8"))) for run in (1, 2, 3)),
        "runs": 3,
    }
(BASE / "DETERMINISM_VALIDATION.json").write_text(
    json.dumps(formal, indent=2), encoding="utf8")
print("summarized", len(summaries), "Unity runs; triple groups", len(formal))
