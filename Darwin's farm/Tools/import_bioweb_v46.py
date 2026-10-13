"""Materialize the approved bioweb export as Unity species assets and sprites.

Run with the original bioweb JSON path. Evolution links are undirected;
food links point from prey to predator. The generated asset GUIDs are stable.
"""
import base64
import io
import json
import pathlib
import re
import sys
import uuid

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "BiowebV46"
SPECIES = OUT / "Species"
SPRITES = OUT / "Sprites"
SOURCE = OUT / "bioweb.json"
SCRIPT_GUID = "4dba79df3cdc97a43a021e65f117e9a0"
SPRITE_META = (ROOT / "Assets" / "2DAsset" / "孔雀.PNG.meta").read_text(encoding="utf-8")
FIT = {
    "鸡": (55, 35), "孔雀": (58, 58), "灵感菇狸": (75, 80),
    "雪狼": (28, 28), "鹿": (30, 70), "雀狮": (52, 66),
    "彩鸡": (70, 49), "鹿驯": (25, 20), "鹿飞": (40, 45),
    "飞狸": (67, 20),
}
NICHE = {"Land": 0, "Water": 1, "Air": 2}


def guid(kind, species_id):
    return uuid.uuid5(uuid.NAMESPACE_URL, f"darwin-farm/v4.6/{kind}/{species_id}").hex


def yaml_string(value):
    return json.dumps(value, ensure_ascii=True)


def folder(path):
    path.mkdir(parents=True, exist_ok=True)
    meta = pathlib.Path(str(path) + ".meta")
    if not meta.exists():
        meta.write_text("fileFormatVersion: 2\nguid: " + guid("folder", path.name) +
                        "\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n"
                        "  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8")


def main(source_path):
    data = json.loads(pathlib.Path(source_path).read_text(encoding="utf-8"))
    assert data["schemaVersion"] == 1
    rows = data["species"]
    by_id = {row["id"]: row for row in rows}
    assert len(rows) == len(by_id) == 15
    # Approved V4.6 revisions, including later removal of chicken <-> peacock.
    withdrawn = ({"鸡", "鹿"}, {"鸡", "孔雀"}, {"鲨母", "虾兔"})
    data["evolutionLinks"] = [edge for edge in data["evolutionLinks"]
                              if {by_id[edge["from"]]["name"],
                                  by_id[edge["to"]]["name"]} not in withdrawn]
    assert len(data["evolutionLinks"]) == 20 and len(data["foodLinks"]) == 25
    assert all(edge["from"] in by_id and edge["to"] in by_id
               for edge in data["evolutionLinks"] + data["foodLinks"])
    folder(OUT)
    folder(SPECIES)
    folder(SPRITES)
    SOURCE.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n",
                      encoding="utf-8")
    (pathlib.Path(str(SOURCE) + ".meta")).write_text(
        "fileFormatVersion: 2\nguid: " + guid("source", "bioweb") +
        "\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n"
        "  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8")
    neighbors = {key: set() for key in by_id}
    prey = {key: set() for key in by_id}
    for edge in data["evolutionLinks"]:
        neighbors[edge["from"]].add(edge["to"])
        neighbors[edge["to"]].add(edge["from"])
    for edge in data["foodLinks"]:
        prey[edge["to"]].add(edge["from"])
    for row in rows:
        key = row["id"]
        name = row["name"]
        stem = f"{name}-{key[:8]}"
        sprite_guid = guid("sprite", key)
        asset_guid = guid("species", key)
        image_data = base64.b64decode(row["image"].split(",", 1)[1])
        image = Image.open(io.BytesIO(image_data)).convert("RGBA")
        image.save(SPRITES / f"{stem}.png")
        (SPRITES / f"{stem}.png.meta").write_text(
            SPRITE_META.replace("c0ef1ef03726ed343aed1e6385387bbd", sprite_guid),
            encoding="utf-8")
        fit_t, fit_h = FIT.get(name, (50, 50))
        evo_refs = "\n".join(
            "  - {fileID: 11400000, guid: " + guid("species", other) + ", type: 2}"
            for other in sorted(neighbors[key]))
        prey_refs = "\n".join(
            "  - {fileID: 11400000, guid: " + guid("species", other) + ", type: 2}"
            for other in sorted(prey[key]))
        evo_yaml = "\n" + evo_refs if evo_refs else " []"
        prey_yaml = "\n" + prey_refs if prey_refs else " []"
        ref = f"{{fileID: 21300000, guid: {sprite_guid}, type: 3}}"
        text = f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {SCRIPT_GUID}, type: 3}}
  m_Name: {yaml_string(name)}
  m_EditorClassIdentifier: Assembly-CSharp::SpeciesData
  speciesName: {yaml_string(name)}
  sprite: {ref}
  speciesId: {key.replace('-', '')}
  portrait: {ref}
  description: {yaml_string(row.get('description', ''))}
  evolutionTargets:{evo_yaml}
  preySpecies:{prey_yaml}
  trophicLevel: {row['trophicLevel']}
  bioenergyValue: {row['value']}
  economyId: {key.replace('-', '')}
  baseMovementAbility: {row['baseMovementAbility']}
  baseHabitatNiche: {0 if row['baseEcologicalNiche'] == 'Water' else 100 if row['baseEcologicalNiche'] == 'Air' else 50}
  baseFitTemperature: {fit_t}
  baseFitHumidity: {fit_h}
  baseSize: {row['baseSize']}
  baseFertility: {row['baseFertility']}
  baseEcologicalNiche: {NICHE[row['baseEcologicalNiche']]}
"""
        (SPECIES / f"{stem}.asset").write_text(text, encoding="utf-8")
        (SPECIES / f"{stem}.asset.meta").write_text(
            "fileFormatVersion: 2\nguid: " + asset_guid +
            "\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n"
            "  userData: \n  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8")
    scene = ROOT / "Assets" / "Scenes" / "WhiteBox.unity"
    scene_text = scene.read_text(encoding="utf-8")
    scene_text, initial_tile_count = re.subn(
        r"  initialTile: \{fileID: 11400000, guid: [0-9a-f]{32}, type: 2\}",
        "  initialTile: {fileID: 11400000, guid: 015789cd96df421cbdf7bdf8d9030566, type: 2}",
        scene_text, count=1)
    assert initial_tile_count == 1
    refs = "\n".join("  - {fileID: 11400000, guid: " +
                     guid("species", row["id"]) + ", type: 2}"
                     for row in rows)
    chicken = next(row for row in rows if row["name"] == "鸡")
    scene_text, changed_setup = re.subn(
        r"(m_EditorClassIdentifier: Assembly-CSharp::WhiteboxBiowebTestSetup\n"
        r"(?:.*\n)*?  species:\n)(?:  - .*\n)+"
        r"(  initialChickenSpecies: ).*",
        lambda match: match.group(1) + refs + "\n" + match.group(2) +
        "{fileID: 11400000, guid: " + guid("species", chicken["id"]) + ", type: 2}",
        scene_text, count=1)
    scene_text, changed_visuals = re.subn(
        r"(m_EditorClassIdentifier: Assembly-CSharp::PopulationVisualPresenter\n"
        r"(?:.*\n)*?  presetSpecies:\n)(?:  - .*\n)+",
        lambda match: match.group(1) + refs + "\n", scene_text, count=1)
    assert changed_setup == changed_visuals == 1
    scene.write_text(scene_text, encoding="utf-8", newline="\n")
    print(f"Generated {len(rows)} species, {len(data['evolutionLinks'])} evolution links, "
          f"{len(data['foodLinks'])} food links")


if __name__ == "__main__":
    main(sys.argv[1])
