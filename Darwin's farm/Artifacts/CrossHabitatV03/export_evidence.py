"""Export compact, auditable evidence from actual V048 Unity replay traces."""
import csv
import json
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent


def days(path):
    result = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith('{"day":'):
            result.append(json.loads(line))
    return result


water = days(HERE / "water-100.trace.txt")
water_repeat = days(HERE / "water-100-repeat.trace.txt")
land = days(HERE / "land-five-species-100.trace.txt")
baseline = days(HERE.parent / "WaterLandV02" / "five-species-100.trace.txt")
competition_high = days(HERE / "water-competition-high.trace.txt")
competition_reduced = days(HERE / "water-competition-reduced.trace.txt")

with (HERE / "daily_water_ledger.csv").open("w", newline="", encoding="utf-8") as f:
    writer = csv.writer(f)
    writer.writerow(["day", "seed", "basinId", "sortedWaterCells", "stockBefore", "G", "P", "consumedAlgae", "stockAfter", "predationConsumedBody", "detritus", "sink", "residual"])
    previous = None
    for item in water:
        cells = [t for t in item["tiles"] if (t["x"], t["y"]) in ((1, 0), (1, 1), (2, 0))]
        stock = sum(t["stock"] for t in cells)
        growth = sum(t["plantGrowth"] for t in cells)
        consumed = sum(t["consumedPlants"] for t in cells)
        detritus = sum(t["detritus"] for t in cells)
        sink = item["accountedBiomassSinkToday"]
        if previous is not None:
            residual = stock - previous - growth + consumed
            writer.writerow([item["day"], 20261018, "w1+w2+w3", "1,0|1,1|2,0", previous, growth, stock + consumed, consumed, stock, "see pop_day predationDeaths/body", detritus, sink, residual])
        previous = stock

with (HERE / "pop_day.csv").open("w", newline="", encoding="utf-8") as f:
    writer = csv.writer(f)
    writer.writerow(["day", "popId", "speciesId", "tileId", "basinId", "N_pre", "huntDeaths", "adultNeed", "foodAllocated", "adultPaid", "born", "naturalDead", "N_end", "M_before", "M_after", "reserveBefore", "reserveAfter", "K_today", "K_food", "traits"])
    previous = {}
    for item in water:
        for tile in item["tiles"]:
            if (tile["x"], tile["y"]) not in ((1, 0), (1, 1), (2, 0)):
                continue
            for pop in tile["populations"]:
                prior = previous.get(pop["id"])
                writer.writerow([item["day"], pop["id"], pop["species"], f'{tile["x"]},{tile["y"]}', "w1+w2+w3", prior["count"] if prior else "", pop["predationDeaths"], pop["adultDemand"], pop["allocatedFood"], pop["adultPaid"], pop["births"], pop["deaths"], pop["count"], prior["body"] if prior else "", pop["body"], prior["reserve"] if prior else "", pop["reserve"], pop["carryingCapacity"], pop["foodK"], f'T={pop["fitT"]};H={pop["fitH"]};M={pop["movement"]};S={pop["size"]};F={pop["fertility"]}'])
                previous[pop["id"]] = pop

for name, series in (("water", water), ("water-repeat", water_repeat), ("land", land)):
    with (HERE / f"{name}-day_hashes.jsonl").open("w", encoding="utf-8") as f:
        for item in series:
            f.write(json.dumps({"day": item["day"], "modelHash": item["modelHash"], "environmentHash": item["environmentHash"]}, ensure_ascii=False) + "\n")

with (HERE / "cross_niche_events.csv").open("w", newline="", encoding="utf-8") as f:
    csv.writer(f).writerow(["day", "srcPop", "srcTile", "dstTile", "type", "targetSpecies", "legalEdge", "traitsOK", "daysOK", "preyForecast", "localSlot", "payment", "probability", "sampledRoll", "N_out", "N_in", "rejectReason"])
with (HERE / "topology_events.csv").open("w", newline="", encoding="utf-8") as f:
    writer = csv.writer(f)
    writer.writerow(["day", "eventType", "tile", "oldBasin", "newBasin", "beforeSumStock", "afterSumStock", "topologyHash"])
    log = (HERE / "unity-logs" / "water-zero-resource-final.log").read_text(encoding="utf-8", errors="replace")
    for day, event, stock in re.findall(r"CROSS_HABITAT_TOPOLOGY day=(\d+) operation=(split|reconnect) stock=([0-9.]+)", log):
        old, new = ("w1+w2+w3", "w1+w2|w3") if event == "split" else ("w1+w2|w3", "w1+w2+w3")
        writer.writerow([day, event, "w3", old, new, stock, stock, "not_exported_by_editor_fixture"])

water_residuals = []
for before, after in zip(water, water[1:]):
    old_stock = sum(t["stock"] for t in before["tiles"] if (t["x"], t["y"]) in ((1, 0), (1, 1), (2, 0)))
    cells = [t for t in after["tiles"] if (t["x"], t["y"]) in ((1, 0), (1, 1), (2, 0))]
    water_residuals.append(sum(t["stock"] + t["consumedPlants"] - t["plantGrowth"] for t in cells) - old_stock)

baseline_by_day = {item["day"]: item for item in baseline}
world_residuals = [(after["day"], after["totalMaterial"] - before["totalMaterial"] - after["plantGrowthToday"] + after["accountedBiomassSinkToday"]) for before, after in zip(water, water[1:])]
def food_of(series, species_index):
    item = next(x for x in series if x["day"] == 1)
    populations = [p for tile in item["tiles"] for p in tile["populations"]]
    return populations[species_index]["allocatedFood"]
repeat_by_day = {item["day"]: item for item in water_repeat}
repeat_difference = next((item["day"] for item in water if item["day"] in repeat_by_day and item["modelHash"] != repeat_by_day[item["day"]]["modelHash"]), None)
first_difference = next((item["day"] for item in land if item["day"] in baseline_by_day and item["modelHash"] != baseline_by_day[item["day"]]["modelHash"]), None)
report = {
    "water_days": len(water), "water_first_day": water[0]["day"] if water else None,
    "water_last_day": water[-1]["day"] if water else None,
    "water_max_stock_residual": max(map(abs, water_residuals), default=None),
    "water_first_stock_failure_day": next((water[i+1]["day"] for i, r in enumerate(water_residuals) if abs(r) > 0.1), None),
    "water_max_world_material_residual": max((abs(v) for _, v in world_residuals), default=None),
    "water_first_world_material_failure_day": next((day for day, v in world_residuals if abs(v) > 2), None),
    "competition_high_deer_food_day1": food_of(competition_high, 0),
    "competition_reduced_deer_food_day1": food_of(competition_reduced, 0),
    "water_repeat_days": len(water_repeat),
    "water_repeat_first_model_hash_difference_day": repeat_difference,
    "land_days": len(land), "baseline_days": len(baseline),
    "land_first_model_hash_difference_day": first_difference,
}
(HERE / "trace-audit.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(report, ensure_ascii=False))
