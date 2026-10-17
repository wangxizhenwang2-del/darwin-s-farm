"""Summarize actual day records without treating a completed run as a win."""

import json
import sys
from collections import Counter
from pathlib import Path

path = Path(sys.argv[1])
days = {}
events = Counter()
for line in path.read_text(encoding="utf-8-sig").splitlines():
    if line.startswith('{"day":'):
        record = json.loads(line)
        counts = Counter()
        traits = []
        for tile in record["tiles"]:
            for population in tile["populations"]:
                if population["count"] > 0:
                    counts[population["species"]] += population["count"]
                    traits.append({"species": population["species"],
                                   "count": population["count"],
                                   "bars": [population[key] for key in
                                            ("fitT", "fitH", "movement", "size",
                                             "fertility")]})
        days[record["day"]] = (dict(counts), record["modelHash"],
                               record["totalMaterial"], traits)
    elif line.startswith("event day="):
        kind = line.split(" kind=", 1)[1].split(" ", 1)[0]
        events[kind] += 1
if not days or days[0][0].get("鸡", 0) != 160:
    raise SystemExit("FAIL: replay did not start with 160 real seeded chickens")
summary = {
    "input": str(path),
    "lastDay": max(days),
    "days": {str(day): {"species": days[day][0], "modelHash": days[day][1],
                        "totalMaterial": days[day][2], "traits": days[day][3]}
             for day in (0, 100, 110, 150, 300, 500, 1000, 1500, 1800, 2000)
             if day in days},
    "events": dict(events),
    "firstAppearance": {species: next(day for day in sorted(days)
                                      if days[day][0].get(species, 0) > 0)
                        for species in sorted({species for values, _, _, _ in days.values()
                                               for species in values})},
}
rendered = json.dumps(summary, ensure_ascii=False, indent=2) + "\n"
if len(sys.argv) > 2:
    Path(sys.argv[2]).write_text(rendered, encoding="utf-8")
else:
    print(rendered)
