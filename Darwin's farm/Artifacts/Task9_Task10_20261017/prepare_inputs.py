"""Create fixed Task 9/10 Unity replay inputs from supplied fixtures."""
import copy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
PACKAGE = HERE / "source_package" / "task9"
OLD = ROOT / "Artifacts" / "Task9_20261016" / "inputs"
OUT = HERE / "inputs"
OUT.mkdir(exist_ok=True)


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save(name, source, evolution=False, quota=False, diagnostics=False):
    scenario = copy.deepcopy(source)
    scenario["scenarioId"] = name
    scenario["t95FinalGate"] = evolution
    scenario["t10Quota"] = quota
    scenario["t10Diagnostics"] = diagnostics
    (OUT / (name + ".json")).write_text(
        json.dumps(scenario, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8")
    return scenario


save("int-s0-deer", read(OLD / "stage0-final-deer10-rich.json"))
save("int-s0-predator", read(OLD / "stage0-final-predator.json"))
for letter in "ABCD":
    source = read(OLD / ("T95E-community-" + letter + ".json"))
    save("int-" + letter.lower() + "-off", source)
    save("int-" + letter.lower() + "-on", source,
         quota=True, diagnostics=letter in "CD")
for route in ("color", "mushroom"):
    source = read(PACKAGE / ("task9-" + route + "-formal.json"))
    save("int-" + route + "-s1", source, evolution=True)
    save("int-" + route + "-s2", source, evolution=True,
         quota=True, diagnostics=True)
stop = read(PACKAGE / "task9-mushroom-stop-after-429.json")
save("int-mushroom-stop-s2", stop, evolution=True, quota=True,
     diagnostics=True)

# Exact replay duplicates quantify deterministic output and timing variance.
save("int-d-off-repeat", read(OLD / "T95E-community-D.json"))
save("int-d-on-repeat", read(OLD / "T95E-community-D.json"),
     quota=True, diagnostics=True)

for label, original, day in (
    ("color", "task9-negative-color128", 128),
    ("mushroom", "task9-negative-mushroom425", 425),
):
    source = read(OLD / (original + ".json"))
    source["endDay"] = day
    source["actions"] = [action for action in source["actions"]
                         if action["day"] <= day]
    save("int-gate-negative-" + label, source,
         evolution=True, quota=True, diagnostics=True)

c = read(OLD / "T95E-community-C.json")
for name, recovery, chicken, hunter, days in (
    ("zero-r", 0, 1000, 50, 150),
    ("half-r", 40000, 1000, 50, 500),
    ("few-prey", 80000, 5, 50, 150),
    ("many-hunters", 80000, 1000, 500, 250),
):
    value = copy.deepcopy(c)
    value["tiles"][0]["recovery"] = recovery
    value["tiles"][0]["populations"][0]["count"] = chicken
    value["tiles"][0]["populations"][1]["count"] = hunter
    value["endDay"] = days
    save("int-neg-" + name, value, quota=True, diagnostics=True)

no_prey = copy.deepcopy(c)
no_prey["tiles"][0]["populations"][0]["species"] = (
    "Assets/BiowebV46/Species/鹿-fd1c574b.asset")
no_prey["endDay"] = 150
save("int-neg-no-legal-prey", no_prey, quota=True, diagnostics=True)

multiple = copy.deepcopy(c)
multiple["tiles"][0]["populations"][1]["count"] = 25
second = copy.deepcopy(multiple["tiles"][0]["populations"][1])
second["count"] = 25
second["species"] = "Assets/BiowebV46/Species/飞狸-b2e8cdba.asset"
second["movement"] = 70
multiple["tiles"][0]["populations"].append(second)
multiple["endDay"] = 150
save("int-neg-two-hunters", multiple, quota=True, diagnostics=True)

print("prepared", len(list(OUT.glob("*.json"))), "inputs", OUT)
