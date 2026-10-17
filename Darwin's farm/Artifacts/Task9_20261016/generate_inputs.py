"""Build Task 9 Unity fixtures from the archived Task 5 JSON inputs."""
import copy
import json
from pathlib import Path

BASE = Path(__file__).resolve().parent
OLD = BASE.parent / "V05DynamicK_Continuation_20261016" / "inputs"
OUT = BASE / "inputs"
OUT.mkdir(exist_ok=True)


def source(name):
    return json.loads((OLD / (name + ".json")).read_text(encoding="utf-8"))


def save(name, value):
    value["scenarioId"] = name
    (OUT / (name + ".json")).write_text(
        json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


three = source("formal-T5-rich-civet50")
three["endDay"] = 1
three["fixtureSkipDiagnostics"] = True
tile = three["tiles"][0]
chicken, deer, civet = tile["populations"]

a = copy.deepcopy(three)
a["tiles"][0]["populations"] = [copy.deepcopy(chicken), copy.deepcopy(civet)]
a["tiles"][0]["populations"][0]["count"] = 10
a["tiles"][0]["populations"][1]["count"] = 100
a["enablePreyDensityFeedback"] = True
save("stage1-A-scarce", a)

a_abundant = copy.deepcopy(three)
a_abundant["tiles"][0]["populations"] = [
    copy.deepcopy(chicken), copy.deepcopy(deer), copy.deepcopy(civet)]
a_abundant["tiles"][0]["populations"][2]["count"] = 100
a_abundant["enablePreyDensityFeedback"] = True
save("stage1-A-abundant", a_abundant)

b = source("formal-T3-deer10-rich")
b["endDay"] = 1
b["fixtureSkipDiagnostics"] = True
b["enablePaidBirthStress"] = True
save("stage1-B-unpaid", b)

b_paid = source("formal-T3-deer10-rich")
b_paid["endDay"] = 3
b_paid["fixtureSkipDiagnostics"] = True
b_paid["enablePaidBirthStress"] = True
save("stage1-B-paid-12", b_paid)
b_paid_low = copy.deepcopy(b_paid)
b_paid_low["reproductionStressPerBirth"] = 0.8
save("stage1-B-paid-08", b_paid_low)

c = copy.deepcopy(three)
c["tiles"][0]["populations"] = [copy.deepcopy(chicken), copy.deepcopy(deer)]
c["tiles"][0]["populations"][0]["count"] = 4000
c["tiles"][0]["populations"][1]["count"] = 2000
c["enablePriorityPlantBudgets"] = True
save("stage1-C-both-short", c)

c_reverse = copy.deepcopy(c)
c_reverse["tiles"][0]["populations"].reverse()
save("stage1-C-reverse", c_reverse)

c_transfer = copy.deepcopy(three)
c_transfer["tiles"][0]["populations"] = [copy.deepcopy(deer)]
c_transfer["tiles"][0]["populations"][0]["count"] = 2000
c_transfer["enablePriorityPlantBudgets"] = True
save("stage1-C-transfer", c_transfer)

joint = copy.deepcopy(three)
joint["endDay"] = 100
joint["enablePreyDensityFeedback"] = True
joint["enablePaidBirthStress"] = True
joint["enablePriorityPlantBudgets"] = True
save("stage1-ABC-joint", joint)

scale_only = copy.deepcopy(three)
scale_only["adultMaintenanceScale"] = 0.4
save("stage2-scale-only", scale_only)

for civet_count in (10, 15, 20, 50, 100, 300, 500):
    for end_day in (2000, 5000):
        candidate = copy.deepcopy(three)
        candidate["endDay"] = end_day
        candidate["tiles"][0]["populations"][2]["count"] = civet_count
        candidate["enablePreyDensityFeedback"] = True
        candidate["enablePaidBirthStress"] = True
        candidate["enablePriorityPlantBudgets"] = True
        candidate["reproductionStressPerBirth"] = 0.8
        candidate["adultMaintenanceScale"] = 0.4
        save(f"stage2-civet{civet_count}-day{end_day}", candidate)

for cut in (10, 20, 50):
    disaster = copy.deepcopy(three)
    disaster["endDay"] = 2000
    disaster["enablePreyDensityFeedback"] = True
    disaster["enablePaidBirthStress"] = True
    disaster["enablePriorityPlantBudgets"] = True
    disaster["reproductionStressPerBirth"] = 0.8
    disaster["adultMaintenanceScale"] = 0.4
    disaster["actions"] = [
        {"day": 599, "x": 1, "y": 0, "kind": "FixtureRecovery",
         "recovery": int(80000 * (100 - cut) / 100)},
        {"day": 999, "x": 1, "y": 0, "kind": "FixtureRecovery",
         "recovery": 80000},
    ]
    save(f"stage3-disaster{cut}", disaster)

# The 27 values are the EnvironmentRules.Profile table, in its index order.
# Each is applied to a real Unity tile with a matching elevation; no
# population survival promise is made for unsuitable climates.
habitats = [
    (0,35,35,50000),(0,35,55,70000),(0,35,75,90000),
    (0,55,35,50000),(0,55,55,100000),(0,55,75,120000),
    (0,75,35,50000),(0,75,55,90000),(0,75,75,130000),
    (1,30,30,60000),(1,30,50,70000),(1,30,70,80000),
    (1,50,30,60000),(1,50,50,80000),(1,50,70,100000),
    (1,70,30,40000),(1,70,50,80000),(1,70,70,120000),
    (2,25,25,50000),(2,25,45,40000),(2,25,65,40000),
    (2,45,25,60000),(2,45,45,90000),(2,45,65,70000),
    (2,65,25,40000),(2,65,45,40000),(2,65,65,90000),
]
for index, (z, t, h, r) in enumerate(habitats):
    habitat = copy.deepcopy(three)
    habitat["endDay"] = 10
    habitat["enablePreyDensityFeedback"] = True
    habitat["enablePaidBirthStress"] = True
    habitat["enablePriorityPlantBudgets"] = True
    habitat["reproductionStressPerBirth"] = 0.8
    habitat["adultMaintenanceScale"] = 0.4
    habitat["tiles"][0]["preset"] = {
        0: "Assets/Scripts/map/data/荒漠.asset",
        1: "Assets/Scripts/map/data/森林.asset",
        2: "Assets/Scripts/map/data/雪山.asset",
    }[z]
    habitat["tiles"][0]["temperature"] = t
    habitat["tiles"][0]["humidity"] = h
    habitat["tiles"][0]["recovery"] = r
    habitat["tiles"][0]["initialStock"] = r
    save(f"stage3-habitat{index:02d}", habitat)
