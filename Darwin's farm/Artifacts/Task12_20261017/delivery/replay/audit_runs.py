"""Stream Task 12 Unity JSONL into auditable tables and baseline comparisons."""
import csv
import json
import re
from collections import defaultdict
from pathlib import Path

BASE = Path(__file__).resolve().parent.parent
RUNS = BASE / "runs"
SCENARIOS = ("int-color-s2", "int-mushroom-s2", "int-mushroom-stop-s2",
             "task12-mush-through-476", "task12-mush-through-490",
             "task12-mush-through-504", "task12-mush-through-518",
             "task12-mig-flat", "task12-mig-slope1", "task12-mig-slope2")


def read_run(stem):
    days = {}
    post = {}
    actions = []
    events = []
    for line in (RUNS / (stem + ".jsonl")).open(encoding="utf-8-sig"):
        if line.startswith("{") and '"modelHash"' in line:
            row = json.loads(line)
            days.setdefault(row["day"], row)
            post[row["day"]] = row
        elif line.startswith("action "):
            actions.append(line.strip())
        elif line.startswith("event "):
            events.append(line.strip())
    return days, post, actions, events


def aggregate(row):
    result = defaultdict(int)
    for tile in row["tiles"]:
        for pop in tile["populations"]:
            result[pop["species"]] += pop["count"]
    return dict(result)


def parse_kv(line):
    return dict(re.findall(r"(\w+)=([^ ]+)", line))


def main():
    cache = {stem: read_run(stem) for stem in SCENARIOS}
    daily = BASE / "DAILY_ECOLOGY.csv"
    evolution = BASE / "EVOLUTION_EVENTS.csv"
    migration = BASE / "MIGRATION_EVENTS.csv"
    with daily.open("w", newline="", encoding="utf-8-sig") as handle:
        writer = csv.writer(handle)
        writer.writerow(("scenario", "seed", "day", "tile_x", "tile_y", "species",
                         "population_id", "N", "K_est", "food_K", "allocated_food",
                         "adult_need_per_head", "energy_satisfaction", "births", "deaths",
                         "predation_deaths", "birth_stress_deaths", "plant_stock",
                         "plant_recovery", "plant_growth", "plant_consumed", "body",
                         "energy_reserve", "movement", "size", "fertility", "trophic",
                         "model_hash", "environment_hash", "total_material"))
        for stem in SCENARIOS:
            days, _, _, _ = cache[stem]
            for day, row in sorted(days.items()):
                for tile in row["tiles"]:
                    for pop in tile["populations"]:
                        writer.writerow((stem, 240, day, tile["x"], tile["y"],
                                         pop["species"], pop["id"], pop["count"],
                                         pop.get("environmentalK"), pop.get("foodK"),
                                         pop.get("allocatedFood"), pop.get("adultNeedPerHead"),
                                         pop.get("energySatisfaction"), pop.get("births"),
                                         pop.get("deaths"), pop.get("predationDeaths"),
                                         pop.get("birthStressDeaths"), tile["stock"],
                                         tile["recovery"], tile["plantGrowth"],
                                         tile["consumedPlants"], pop.get("body"),
                                         pop.get("reserve"), pop.get("movement"),
                                         pop.get("size"), pop.get("fertility"),
                                         pop.get("trophicLevel", "NA"), row["modelHash"],
                                         row["environmentHash"], row["totalMaterial"]))
    with evolution.open("w", newline="", encoding="utf-8-sig") as efile, \
         migration.open("w", newline="", encoding="utf-8-sig") as mfile:
        ew, mw = csv.writer(efile), csv.writer(mfile)
        header = ("scenario", "seed", "day", "kind", "source_tile", "target_tile",
                  "source_population_id", "result_population_id", "target_species",
                  "amount", "source_before", "source_after", "target_before",
                  "target_after", "outcome", "raw_event")
        ew.writerow(header)
        mw.writerow(header)
        for stem in SCENARIOS:
            for line in cache[stem][3]:
                kv = parse_kv(line)
                row = (stem, 240, kv.get("day"), kv.get("kind"), kv.get("source"),
                       kv.get("target"), kv.get("sourcePopulation"),
                       kv.get("resultPopulation"), kv.get("species"),
                       kv.get("amount"), kv.get("sourceBefore"),
                       kv.get("sourceAfter"), kv.get("targetBefore"),
                       kv.get("targetAfter"), kv.get("outcome"), line)
                (mw if kv.get("kind") == "Migration" else ew).writerow(row)
    summaries = {}
    for stem in SCENARIOS:
        days, post, actions, events = cache[stem]
        species = sorted({name for row in days.values() for name in aggregate(row)})
        summary = {"dayRows": len(days), "lastDay": max(days),
                   "actions": len(actions), "formalEvolutions": sum(
                       "kind=PresetEvolution" in event for event in events),
                   "migrations": sum("kind=Migration" in event for event in events),
                   "species": {}}
        for name in species:
            series = {day: aggregate(row).get(name, 0) for day, row in days.items()}
            living = [day for day, n in series.items() if n > 0]
            summary["species"][name] = {
                "firstLivingDay": min(living) if living else None,
                "lastLivingDay": max(living) if living else None,
                "peak": max(series.values()), "peakDay": max(series, key=series.get),
                "final": series[max(days)],
                "daySamples": {str(day): series.get(day) for day in
                               (132, 232, 429, 430, 434, 435, 490, 512, 529,
                                629, 929, 1050) if day in series}}
        summaries[stem] = summary
    left = cache["int-mushroom-s2"][1]
    right = cache["int-mushroom-stop-s2"][1]
    first = next((day for day in sorted(set(left) & set(right))
                  if left[day]["modelHash"] != right[day]["modelHash"]), None)
    summaries["mushroomComparison"] = {
        "firstPostActionModelDivergenceDay": first,
        "continueActionsAfterEvolution": [a for a in cache["int-mushroom-s2"][2]
                                          if int(parse_kv(a)["day"]) > 429],
        "stopActionsAfterEvolution": [a for a in cache["int-mushroom-stop-s2"][2]
                                      if int(parse_kv(a)["day"]) > 429],
        "stateAtFirstDivergence": {
            stem: {"stock": data[first]["tiles"][0]["stock"],
                   "recovery": data[first]["tiles"][0]["recovery"],
                   "counts": aggregate(data[first])}
            for stem, data in (("continue", left), ("stop", right))}
        if first is not None else None}
    (BASE / "P1_REPRODUCTION.json").write_text(
        json.dumps(summaries, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(summaries, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()
