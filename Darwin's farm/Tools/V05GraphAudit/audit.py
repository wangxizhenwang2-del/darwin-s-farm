"""Compare the user's source JSON with imported Unity SpeciesData assets.

This is a static graph/import audit, never an ecology simulator.
"""

import hashlib
import json
import pathlib
import re
import sys


ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = pathlib.Path(sys.argv[1])
OUTPUT = pathlib.Path(sys.argv[2])
NAMES = ("鸡", "灵感菇狸", "鹿", "彩鸡", "孔雀")
ROUTE = (("鸡", "灵感菇狸"), ("灵感菇狸", "鹿"),
         ("鸡", "彩鸡"), ("彩鸡", "孔雀"))
FIELDS = ("baseFitTemperature", "baseFitHumidity", "baseMovementAbility",
          "baseSize", "baseFertility", "trophicLevel")


def field(text, name):
    found = re.search(r"^  " + re.escape(name) + r": (\d+)$", text, re.M)
    return int(found.group(1)) if found else None


source_bytes = SOURCE.read_bytes()
source = json.loads(source_bytes.decode("utf-8-sig"))
species_by_name = {entry["name"]: entry for entry in source["species"]}
guid_to_id = {}
asset_by_id = {}
for asset in sorted((ROOT / "Assets/BiowebV46/Species").glob("*.asset")):
    meta = asset.with_suffix(".asset.meta").read_text(encoding="utf-8-sig")
    guid = re.search(r"^guid: ([0-9a-f]+)$", meta, re.M)
    text = asset.read_text(encoding="utf-8-sig")
    species_id = re.search(r"^  speciesId: ([0-9a-f]+)$", text, re.M)
    if guid is None or species_id is None:
        raise ValueError(f"Missing Unity GUID or Species ID: {asset}")
    sid = species_id.group(1)
    guid_to_id[guid.group(1)] = sid
    asset_by_id[sid] = {"path": str(asset.relative_to(ROOT)).replace("\\", "/"),
                        "fields": {key: field(text, key) for key in FIELDS},
                        "text": text}


def asset_links(text, label):
    match = re.search(r"^  " + re.escape(label) +
                      r":(?:\s*\[\])?\s*\n((?:  - .*\n)*)", text, re.M)
    if match is None:
        raise ValueError(f"Missing {label}")
    return re.findall(r"guid: ([0-9a-f]+)", match.group(1))


def pairs(label):
    edges = set()
    unresolved = []
    for sid, asset in asset_by_id.items():
        for guid in asset_links(asset["text"], label):
            target = guid_to_id.get(guid)
            if target is None:
                unresolved.append({"source": sid, "targetGuid": guid})
            else:
                edges.add((sid, target))
    return edges, unresolved


unity_evolution, unresolved_evolution = pairs("evolutionTargets")
unity_food_predator_to_prey, unresolved_food = pairs("preySpecies")
json_evolution = {(x["from"].replace("-", ""), x["to"].replace("-", ""))
                  for x in source["evolutionLinks"]}
# JSON foodLinks encode prey -> predator; SpeciesData.preySpecies stores predator -> prey.
json_food_predator_to_prey = {
    (x["to"].replace("-", ""), x["from"].replace("-", ""))
    for x in source["foodLinks"]
}
json_evolution_bidirectional = json_evolution | {
    (target, source) for source, target in json_evolution
}


def edge_names(edges):
    by_id = {entry["id"].replace("-", ""): entry["name"]
             for entry in source["species"]}
    return sorted([list(map(lambda sid: by_id.get(sid, sid), edge))
                   for edge in edges])


template_differences = []
for entry in source["species"]:
    sid = entry["id"].replace("-", "")
    asset = asset_by_id.get(sid)
    if asset is None:
        template_differences.append({"species": entry["name"], "missingAsset": True})
        continue
    for key in FIELDS:
        if key in entry and entry[key] != asset["fields"][key]:
            template_differences.append({"species": entry["name"], "field": key,
                                         "json": entry[key], "unity": asset["fields"][key],
                                         "asset": asset["path"]})


routes = []
for src, dst in ROUTE:
    a = species_by_name[src]["id"].replace("-", "")
    b = species_by_name[dst]["id"].replace("-", "")
    routes.append({"from": src, "to": dst,
                   "jsonStoredForward": (a, b) in json_evolution,
                   "jsonStoredReverse": (b, a) in json_evolution,
                   "unityStoredForward": (a, b) in unity_evolution,
                   "unityStoredReverse": (b, a) in unity_evolution,
                   "runtimeBidirectionalEligible": (a, b) in unity_evolution
                   or (b, a) in unity_evolution})


report = {
    "kind": "static_source_audit_not_unity_simulation",
    "source": str(SOURCE), "sourceSha256": hashlib.sha256(source_bytes).hexdigest(),
    "speciesCount": len(source["species"]),
    "jsonEvolutionCount": len(json_evolution), "unityEvolutionCount": len(unity_evolution),
    "jsonEvolutionBidirectionalCount": len(json_evolution_bidirectional),
    "jsonFoodCount": len(json_food_predator_to_prey),
    "unityFoodCount": len(unity_food_predator_to_prey),
    "evolutionOnlyInJson": edge_names(json_evolution - unity_evolution),
    "evolutionOnlyInUnity": edge_names(unity_evolution - json_evolution),
    "evolutionOnlyInBidirectionalJson": edge_names(
        json_evolution_bidirectional - unity_evolution),
    "evolutionOnlyInUnityAfterBidirectionalExpansion": edge_names(
        unity_evolution - json_evolution_bidirectional),
    "foodOnlyInJson": edge_names(json_food_predator_to_prey - unity_food_predator_to_prey),
    "foodOnlyInUnity": edge_names(unity_food_predator_to_prey - json_food_predator_to_prey),
    "unresolvedEvolutionGuids": unresolved_evolution,
    "unresolvedFoodGuids": unresolved_food,
    "templateDifferences": template_differences,
    "routes": routes,
}
OUTPUT.parent.mkdir(parents=True, exist_ok=True)
OUTPUT.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({key: report[key] for key in
                  ("sourceSha256", "jsonEvolutionCount", "unityEvolutionCount",
                   "jsonFoodCount", "unityFoodCount", "templateDifferences", "routes")},
                 ensure_ascii=True))
