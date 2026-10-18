"""Audit fixed Unity replays and assemble the single Task 9/10 handoff ZIP."""
import csv
import difflib
import hashlib
import json
import re
from functools import lru_cache
from pathlib import Path
from statistics import mean
from zipfile import ZipFile, ZIP_DEFLATED

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
RUNS = ROOT / "Artifacts/Task9_20261016/runs"
INPUTS = HERE / "inputs"
OUT = HERE / "handoff"
OUT.mkdir(exist_ok=True)
NAMES = [p.stem for p in INPUTS.glob("int-*.json")]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


@lru_cache(maxsize=4)
def data(name):
    with (RUNS / (name + ".jsonl")).open(encoding="utf-8-sig") as handle:
        rows = [json.loads(line) for line in handle
                if line.startswith("{") and '"modelHash"' in line]
    return {row["day"]: row for row in rows}


def tile(row):
    return next(t for t in row["tiles"] if t["x"] == 0 and t["y"] == 0)


def populations(row):
    return tile(row)["populations"]


def species_count(row, name):
    return sum(p["count"] for p in populations(row) if p["species"] == name)


def species_names(row):
    return [p["species"] for p in populations(row)]


def ecology(row):
    # All original exported environment, resource, trait, life and food fields.
    ignored = {"t95", "t95eTrackedTarget", "t95eLastMutationEffect",
               "t95eProtectionSince", "t95eProtectionReleased", "t10"}
    clean = []
    for t in row["tiles"]:
        v = dict(t)
        v["populations"] = [{k: value for k, value in p.items()
                             if k not in ignored} for p in t["populations"]]
        clean.append(v)
    return clean


def comparison(old, new, end=None):
    a, b = data(old), data(new)
    days = sorted(set(a) & set(b))
    if end is not None:
        days = [day for day in days if day <= end]
    return {"comparedDays": len(days),
            "environmentHashesEqual": sum(a[d]["environmentHash"] ==
                                          b[d]["environmentHash"] for d in days),
            "modelHashesEqual": sum(a[d]["modelHash"] ==
                                    b[d]["modelHash"] for d in days),
            "originalEcologyFieldsEqual": sum(ecology(a[d]) == ecology(b[d])
                                              for d in days),
            "firstEcologyDifference": next((d for d in days if
                                            ecology(a[d]) != ecology(b[d])), None)}


def against_history(history, current):
    return comparison(history, current)


def conversion(name):
    by = data(name)
    source = populations(by[0])[0]["species"]
    first = next((day for day in sorted(by) if any(p["species"] != source
                  for p in populations(by[day]))), None)
    if first is None:
        return {"status": "FAIL", "reason": "no formal target", "firstDay": None}
    target = next(p["species"] for p in populations(by[first])
                  if p["species"] != source)
    counts = lambda day, name: species_count(by[day], name)
    horizon = first + 500
    points = {f"plus{n}": counts(first+n, target) if first+n in by else None
              for n in (1, 7, 30, 100, 200, 300, 500)}
    prey_values = [counts(day, source) for day in range(first, min(horizon,
                    max(by)) + 1)]
    predator_values = [counts(day, target) for day in range(first,
                       min(horizon, max(by)) + 1)]
    late = list(range(max(first, horizon-99), horizon+1)) if horizon in by else []
    late_counts = [counts(day, target) for day in late]
    slope = ((late_counts[-1] - late_counts[0]) / 99 if len(late_counts)==100
             else None)
    log = (RUNS / (name + ".log")).read_text(encoding="utf-8",
                                               errors="replace")
    event = next((line for line in log.splitlines()
                  if "kind=preset_evolution" in line), "")
    match = re.search(r"amount=(\d+) plantPayment=([\d.]+) excessBody=([\d.]+)", event)
    surviving = horizon in by and all(n > 0 for n in predator_values)
    coexist = surviving and all(n >= 10 for n in prey_values + predator_values)
    return {"status": "PASS" if match and
            f"day={first} kind=preset_evolution" in event else "FAIL",
            "source": source, "target": target, "firstDay": first,
            "founders": int(match.group(1)) if match else None,
            "plantPayment": float(match.group(2)) if match else None,
            "excessBody": float(match.group(3)) if match else None,
            "event": event,
            "sourcePriorDay": counts(first-1, source) if first-1 in by else None,
            "sourceAfter": counts(first, source), "targetAfter": counts(first,target),
            "counts": points,
            "sourceMinimum500": min(prey_values) if prey_values else None,
            "targetMaximum500": max(predator_values) if predator_values else None,
            "targetFirstExtinctDay": next((day for day in range(first,
                min(horizon,max(by))+1) if counts(day,target)==0),None),
            "survived500": "PASS" if surviving else "FAIL",
            "coexist500": "PASS" if coexist else "FAIL",
            "late100Mean": mean(late_counts) if late_counts else None,
            "late100SlopePerDay": slope,
            "late100Start": late_counts[0] if late_counts else None,
            "late100End": late_counts[-1] if late_counts else None,
            "late100Minimum": min(late_counts) if late_counts else None,
            "late100Maximum": max(late_counts) if late_counts else None}


@lru_cache(maxsize=None)
def quota_audit(name):
    config = json.loads((INPUTS/(name+".json")).read_text(encoding="utf-8-sig"))
    if not config.get("t10Quota", False):
        return {"observedPreyDays":0,"quotaViolations":0,
                "noRootDays":0,"zeroQuotaDays":0,"maxKills":None,
                "maxQuota":None,"rows":[]}
    by = data(name)
    observed = []
    for day, row in by.items():
        if day == 0 or day-1 not in by:
            continue
        before = populations(by[day-1])
        has_hunter = any(p["species"] not in ("鸡","鹿","彩鸡") and
                         p["count"]>0 for p in before)
        if not has_hunter:
            continue
        for t in row["tiles"]:
            for p in t["populations"]:
                if p["species"] not in ("鸡","鹿","彩鸡"):
                    continue
                q = p.get("t10")
                if q:
                    observed.append((day, p["species"], p["predationDeaths"],
                                     q["quota"], q["unhuntedGrowth"],
                                     q["equilibrium"], q["equilibriumFound"]))
    return {"observedPreyDays": len(observed),
            "quotaViolations": sum(kills > quota for _,_,kills,quota,*_ in observed),
            "noRootDays": sum(not found for *_,found in observed),
            "zeroQuotaDays": sum(quota == 0 for _,_,_,quota,*_ in observed),
            "maxKills": max((x[2] for x in observed), default=None),
            "maxQuota": max((x[3] for x in observed), default=None),
            "rows": observed}


@lru_cache(maxsize=None)
def material_audit(name):
    by = data(name)
    log = (RUNS / (name + ".jsonl")).read_text(encoding="utf-8-sig")
    action = {}
    for line in log.splitlines():
        if not line.startswith("action day="):
            continue
        match = re.search(r"action day=(\d+).*materialDelta=([-+\d.eE]+)",line)
        if match:
            day = int(match.group(1))
            action[day] = action.get(day, 0) + float(match.group(2))
    residuals = []
    for day in range(1,max(by)+1):
        current, previous = by[day], by[day-1]
        delta = (current["totalMaterial"] - previous["totalMaterial"] -
                 current["plantGrowthToday"] - action.get(day,0))
        residuals.append((day,delta))
    unpaid = sum(p["births"] * p["newbornCost"] > p["birthBudget"] + .3
                 for row in by.values() for t in row["tiles"]
                 for p in t["populations"])
    negative = sum(p["body"] < -.01 or p["reserve"] < -.01
                   for row in by.values() for t in row["tiles"]
                   for p in t["populations"])
    tol = lambda day: max(1,by[day-1]["totalMaterial"] * 1e-5)
    return {"materialCreationDays": [day for day,value in residuals
                                      if value > tol(day)],
            "largestResidual": max((v for _,v in residuals),default=0),
            "unpaidBirthRows": unpaid, "negativeBodyOrReserveRows": negative}


def community(name):
    by = data(name)
    all_names = sorted({p["species"] for p in populations(by[0])})
    def window(lo,hi):
        return {name: mean(species_count(by[day],name)
                           for day in range(lo,hi+1)) for name in all_names}
    values = {name: [species_count(by[day],name) for day in sorted(by)]
              for name in all_names}
    q = quota_audit(name)
    return {"day5000": {name: species_count(by[5000],name) for name in all_names},
            "mean1000to2000":window(1000,2000),
            "mean4000to5000":window(4000,5000),
            "minimum": {name:min(seq) for name,seq in values.items()},
            "maximum": {name:max(seq) for name,seq in values.items()},
            "births": {name:sum(p["births"] for row in by.values()
                       for p in populations(row) if p["species"]==name)
                       for name in all_names},
            "deaths": {name:sum(p["deaths"] for row in by.values()
                       for p in populations(row) if p["species"]==name)
                       for name in all_names},
            "predationDeaths": {name:sum(p["predationDeaths"]
                       for row in by.values() for p in populations(row)
                       if p["species"]==name) for name in all_names},
            "quota": {k:v for k,v in q.items() if k!="rows"},
            "material": material_audit(name)}


def execute():
    attempts = json.loads((HERE/"matrix_execution.json").read_text(encoding="utf-8"))
    attempts["int-s0-deer"] = json.loads((RUNS/"int-s0-deer.execution.json")
                                          .read_text(encoding="utf-8"))
    statuses = {name: "PASS" if item.get("exitCode")==0 and
                item.get("replayCompleted") else "FAIL"
                for name,item in attempts.items()}
    baseline = {"deer":against_history("stage0-final-deer10-rich","int-s0-deer"),
                "predator":against_history("stage0-final-predator","int-s0-predator"),
                "threeSpecies":against_history("T95E-community-D","int-d-off")}
    community_result = {letter:{side:community(f"int-{letter}-{side}")
                        for side in ("off","on")} for letter in "abcd"}
    neutral = {letter: comparison(f"int-{letter}-off",f"int-{letter}-on")
               for letter in "ab"}
    evolutions = {route:{side:conversion(f"int-{route}-{side}")
                  for side in ("s1","s2")} for route in ("color","mushroom")}
    evolutions["mushroom"]["stopS2"] = conversion("int-mushroom-stop-s2")
    pre = {route:comparison(f"int-{route}-s1",f"int-{route}-s2",
                          evolutions[route]["s1"]["firstDay"]-1)
           for route in ("color","mushroom")}
    stop_pre = comparison("int-mushroom-s2","int-mushroom-stop-s2",
                          evolutions["mushroom"]["s2"]["firstDay"])
    gate_negative = {}
    for route,day in (("color",128),("mushroom",425)):
        row = data(f"int-gate-negative-{route}")[day]
        tests = next(p["t95"]["finalNegatives"] for p in populations(row)
                     if p.get("t95") and p["t95"].get("finalNegatives"))
        gate_negative[route] = {"status":"PASS" if all(
            t["eligible"]==(t["caseName"]=="positive") for t in tests)
                                else "FAIL", "cases":tests}
    negative = {name:{"lastCounts":{p["species"]:p["count"]
                         for p in populations(data(name)[max(data(name))])},
                      "quota":{k:v for k,v in quota_audit(name).items()
                               if k!="rows"},"material":material_audit(name)}
                for name in NAMES if name.startswith("int-neg-")}
    read_only = {"color":comparison("int-color-s2",
                                "int-gate-negative-color",128),
                 "mushroom":comparison("int-mushroom-s2",
                                "int-gate-negative-mushroom",425)}
    determinism = {side:comparison(f"int-d-{side}",
                                  f"int-d-{side}-repeat") for side in ("off","on")}
    timings = {name:json.loads((RUNS/(name+".execution.json"))
                      .read_text(encoding="utf-8"))["seconds"]
               for name in ("int-d-off","int-d-on","int-d-off-repeat",
                            "int-d-on-repeat")}
    speed_ratio = mean(timings[n] for n in ("int-d-on","int-d-on-repeat"))/mean(
        timings[n] for n in ("int-d-off","int-d-off-repeat"))
    regression = {"baseline":baseline,"herbivoreNeutral":neutral,
                  "readOnly":read_only,"determinism":determinism,
                  "timingsSeconds":timings,"performanceRatio":speed_ratio,
                  "performanceStatus":"PASS" if speed_ratio<=1.25 else "FAIL"}
    regression["baselineStatus"] = "PASS" if all(
        v["comparedDays"]==v["originalEcologyFieldsEqual"] and
        v["comparedDays"]==v["environmentHashesEqual"]
        for v in baseline.values()) else "FAIL"
    regression["determinismStatus"] = "PASS" if all(
        v["comparedDays"]==v["originalEcologyFieldsEqual"] and
        v["comparedDays"]==v["environmentHashesEqual"] and
        v["comparedDays"]==v["modelHashesEqual"]
        for v in determinism.values()) else "FAIL"
    task9 = {"color":evolutions["color"],"mushroom":evolutions["mushroom"],
             "preConversion":pre,"stopPreConversion":stop_pre,
             "gateNegative":gate_negative}
    task9["resourceAudit"] = {name:{"material":material_audit(name),
                                     "quota":{k:v for k,v in quota_audit(name).items()
                                              if k!="rows"}}
                              for name in ("int-color-s1","int-color-s2",
                                  "int-mushroom-s1","int-mushroom-s2",
                                  "int-mushroom-stop-s2")}
    task9["formalConversionStatus"] = "PASS" if all(
        evolutions[r]["s2"]["status"]=="PASS"
        for r in ("color","mushroom")) else "FAIL"
    task9["materialStatus"] = "PASS" if all(
        not x["material"]["materialCreationDays"] and
        x["material"]["unpaidBirthRows"]==0 and
        x["material"]["negativeBodyOrReserveRows"]==0
        for x in task9["resourceAudit"].values()) else "FAIL"
    task9["longTermStable"] = {"colorS2":"FAIL" if
        evolutions["color"]["s2"]["late100End"] <
        evolutions["color"]["s2"]["late100Start"] else "PASS",
        "mushroomS2":"FAIL" if
        evolutions["mushroom"]["s2"]["survived500"]=="FAIL" else "PASS",
        "mushroomStopS2":"FAIL" if
        evolutions["mushroom"]["stopS2"]["targetMaximum500"]>1000 else "PASS"}
    task9["status"] = "PASS" if (task9["materialStatus"]=="PASS" and
        all(evolutions[r]["s2"]["status"]=="PASS"
        and evolutions[r]["s2"]["survived500"]=="PASS"
        and evolutions[r]["s2"]["coexist500"]=="PASS"
        for r in ("color","mushroom")) and all(v["status"]=="PASS"
        for v in gate_negative.values()) and all(v=="PASS"
        for v in task9["longTermStable"].values())) else "FAIL"
    task10 = {"communities":community_result,"negative":negative,
              "l2Prey":"OUT_OF_SCOPE", "performance":regression["performanceStatus"]}
    all_quotas = [x["on"]["quota"]["quotaViolations"]
                  for x in community_result.values()]
    all_quotas += [v["quota"]["quotaViolations"] for v in negative.values()]
    all_quotas += [quota_audit("int-mushroom-s2")["quotaViolations"],
                   quota_audit("int-mushroom-stop-s2")["quotaViolations"]]
    task10["quotaBound"] = "PASS" if sum(all_quotas)==0 else "FAIL"
    task10["communityStatus"] = {letter:"PASS" if (
        all(value > 0 for value in community_result[letter]["on"]["day5000"].values())
        and all(value <= 1000 for name,value in
                community_result[letter]["on"]["maximum"].items()
                if name not in ("鸡","鹿"))
        and all(abs(value-community_result[letter]["on"]["mean4000to5000"][name])
                <= 0.1*max(1,value) for name,value in
                community_result[letter]["on"]["day5000"].items())) else "FAIL"
        for letter in "cd"}
    task10["materialStatus"] = "PASS" if all(
        not side["material"]["materialCreationDays"] and
        side["material"]["unpaidBirthRows"]==0 and
        side["material"]["negativeBodyOrReserveRows"]==0
        for x in community_result.values() for side in x.values()) and all(
        not x["material"]["materialCreationDays"] and
        x["material"]["unpaidBirthRows"]==0 and
        x["material"]["negativeBodyOrReserveRows"]==0
        for x in negative.values()) else "FAIL"
    task10["negativeStatus"] = "PASS" if all(
        x["quota"]["quotaViolations"]==0 for x in negative.values()) else "FAIL"
    task10["legacyRegression"] = regression["baselineStatus"]
    task10["determinism"] = regression["determinismStatus"]
    task10["herbivoreNeutral"] = "PASS" if all(v["originalEcologyFieldsEqual"]==
        v["comparedDays"] for v in neutral.values()) else "FAIL"
    task10["formalRouteContinue500"] = evolutions["mushroom"]["s2"]["coexist500"]
    task10["formalRouteStop500"] = evolutions["mushroom"]["stopS2"]["coexist500"]
    task10["stopRoutePopulationRisk"] = "FAIL" if (
        evolutions["mushroom"]["stopS2"]["targetMaximum500"] > 1000) else "PASS"
    task10["status"] = "PASS" if (
        task10["quotaBound"]=="PASS" and
        task10["herbivoreNeutral"]=="PASS" and
        all(v=="PASS" for v in task10["communityStatus"].values()) and
        task10["materialStatus"]=="PASS" and
        task10["negativeStatus"]=="PASS" and
        task10["legacyRegression"]=="PASS" and
        task10["determinism"]=="PASS" and
        task10["performance"]=="PASS" and
        task10["formalRouteContinue500"]=="PASS" and
        task10["formalRouteStop500"]=="PASS" and
        task10["stopRoutePopulationRisk"]=="PASS") else "FAIL"
    result = {"unityVersion":"6000.2.9f1","costsDisabled":True,
              "defaultSwitches":{"E":False,"Q":False},
              "task9":task9,"task10":task10,"runStatus":statuses}
    (OUT/"INTEGRATED_RESULT.json").write_text(
        json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
    (OUT/"REGRESSION.json").write_text(
        json.dumps(regression,ensure_ascii=False,indent=2),encoding="utf-8")
    with (OUT/"SCENARIO_MATRIX.csv").open("w",encoding="utf-8-sig",newline="") as f:
        w=csv.writer(f)
        w.writerow(("scenario","E","Q","status","endDay","initial",
                    "final","min","max","materialCreationDays",
                    "unpaidBirthRows","quotaViolations","noRootDays","seconds"))
        for name in sorted(NAMES):
            by=data(name); init={p["species"]:p["count"] for p in populations(by[0])}
            final={p["species"]:p["count"] for p in populations(by[max(by)])}
            names=set(init)|set(final)
            minimum={s:min(species_count(row,s) for row in by.values())
                     for s in names}
            maximum={s:max(species_count(row,s) for row in by.values())
                     for s in names}
            m=material_audit(name); q=quota_audit(name)
            config=json.loads((INPUTS/(name+".json")).read_text(encoding="utf-8"))
            w.writerow((name,config.get("t95FinalGate",False),config.get("t10Quota",False),
                        statuses.get(name,"NOT_TESTED"),max(by),
                        json.dumps(init,ensure_ascii=False),json.dumps(final,ensure_ascii=False),
                        json.dumps(minimum,ensure_ascii=False),json.dumps(maximum,ensure_ascii=False),
                        len(m["materialCreationDays"]),m["unpaidBirthRows"],
                        q["quotaViolations"],q["noRootDays"],
                        json.loads((RUNS/(name+".execution.json")).read_text(
                            encoding="utf-8"))["seconds"]))
    return result, regression


if __name__ == "__main__":
    result, regression = execute()
    print("Task9",result["task9"]["status"],
          "Task10",result["task10"]["status"],
          "performance ratio",round(regression["performanceRatio"],3))
