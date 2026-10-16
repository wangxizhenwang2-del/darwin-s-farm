"""Place the supplied natural-history image behind both authored WhiteBox maps."""
from pathlib import Path
import json
import re
import shutil

ROOT = Path(__file__).resolve().parents[1]
SCENE = ROOT / "Assets/Scenes/WhiteBox.unity"
SOURCE = Path(r"C:\Users\17799\Downloads\复古自然博物志留白边框.png")
TARGET = ROOT / "Assets/Resources/GameUI/SpeciesMapBackground.png"
BACKGROUND_GUID = "718a12ec2ca84fe68a1efb13e8e07f91"

if not SOURCE.exists():
    raise FileNotFoundError(SOURCE)
shutil.copyfile(SOURCE, TARGET)
meta = (ROOT / "Assets/Resources/GameUI/SpeciesCircle.png.meta").read_text(encoding="utf-8")
meta = re.sub(r"(?m)^guid: \w+", "guid: " + BACKGROUND_GUID, meta)
TARGET.with_suffix(".png.meta").write_text(meta, encoding="utf-8")

scene = SCENE.read_text(encoding="utf-8")
if "SpeciesMapBackground" in scene:
    raise RuntimeError("Background already authored in WhiteBox")

parts = re.split(r"(?=^--- !u!)", scene, flags=re.M)
header = parts.pop(0)
blocks = {}
order = []
for part in parts:
    match = re.match(r"--- !u!(\d+) &(\d+)", part)
    if not match:
        raise RuntimeError("Scene block could not be parsed")
    fid = int(match.group(2))
    blocks[fid] = part
    order.append(fid)

def name_of(block):
    match = re.search(r"(?m)^  m_Name: (.*)$", block)
    if not match:
        return None
    raw = match.group(1)
    return json.loads(raw) if raw.startswith('"') else raw

def game_objects(name):
    return [fid for fid in order if blocks[fid].startswith("--- !u!1 &") and name_of(blocks[fid]) == name]

def components(go):
    return [int(v) for v in re.findall(r"- component: \{fileID: (\d+)\}", blocks[go])]

def component(go, kind):
    for fid in components(go):
        if blocks[fid].startswith(f"--- !u!{kind} &"):
            return fid
    raise RuntimeError(f"Missing component kind {kind} on {go}")

def script_component(go, class_name):
    for fid in components(go):
        if f"m_EditorClassIdentifier: {class_name}" in blocks[fid]:
            return fid
    raise RuntimeError(f"Missing {class_name} on {go}")

def replace_line(fid, field, value):
    pattern = rf"(?m)^(  {field}: ).*$"
    updated,count = re.subn(pattern,lambda m:m.group(1)+value,blocks[fid],count=1)
    if count != 1:
        raise RuntimeError(f"Missing {field} on {fid}")
    blocks[fid] = updated

def set_position(rect, x, y):
    replace_line(rect,"m_AnchoredPosition",f"{{x: {x:g}, y: {y:g}}}")

def set_size(rect, x, y):
    replace_line(rect,"m_SizeDelta",f"{{x: {x:g}, y: {y:g}}}")

def set_color(go, color, raycast=None):
    image = script_component(go,"UnityEngine.UI::UnityEngine.UI.Image")
    replace_line(image,"m_Color",color)
    if raycast is not None:
        replace_line(image,"m_RaycastTarget",str(raycast))

overlay = game_objects("Species Maps")
panels = [game_objects("Evolution Map"),game_objects("Food Web")]
viewports = [next(go for go in game_objects("Viewport")
                  if f"m_Father: {{fileID: {component(panel[0],224)}}}" in blocks[component(go,224)])
             for panel in panels]
contents = game_objects("Grid Content")
assert len(overlay)==1 and all(len(p)==1 for p in panels)
assert len(viewports)==len(contents)==2
set_color(overlay[0],"{r: 0, g: 0, b: 0, a: 0.62}")

for map_index in range(2):
    panel = panels[map_index][0]
    viewport = viewports[map_index]
    content = contents[map_index]
    set_position(component(panel,224),0,0)
    set_size(component(panel,224),0,0)
    set_color(panel,"{r: 0, g: 0, b: 0, a: 0}",0)
    set_position(component(viewport,224),0,0)
    set_size(component(viewport,224),0,0)
    set_color(viewport,"{r: 0, g: 0, b: 0, a: 0}",0)
    set_size(component(content,224),1448,1086)
    replace_line(component(content,224),"m_LocalScale","{x: 1, y: 1, z: 1}")

species_coords = {}
for path in (ROOT / "Assets/BiowebV46/Species").glob("*.asset"):
    asset = path.read_text(encoding="utf-8")
    guid = re.search(r"(?m)^guid: (\w+)",path.with_suffix(".asset.meta").read_text()).group(1)
    coords = [tuple(map(int,re.search(rf"(?m)^  {field}: \{{x: (\d+), y: (\d+)\}}",asset).groups()))
              for field in ("evolutionGridPos","foodGridPos")]
    species_coords[guid] = coords

positions = {}
for fid in order:
    if "m_EditorClassIdentifier: Assembly-CSharp::SpeciesMapNodeView" not in blocks[fid]:
        continue
    go = int(re.search(r"m_GameObject: \{fileID: (\d+)\}",blocks[fid]).group(1))
    rect = component(go,224)
    parent = int(re.search(r"m_Father: \{fileID: (\d+)\}",blocks[rect]).group(1))
    map_index = next((i for i,c in enumerate(contents) if component(c,224)==parent),None)
    if map_index is None:
        raise RuntimeError("Node is outside map content")
    guid = re.search(r"species: \{fileID: 11400000, guid: (\w+)",blocks[fid]).group(1)
    col,row = species_coords[guid][map_index]
    columns = 4 if map_index==0 else 6
    horizontal = 210 if map_index==0 else 145
    x = (col-(columns+1)/2)*horizontal
    y = (3-row)*145
    set_position(rect,x,y)
    positions[(map_index,name_of(blocks[go]))] = (x,y)

for go in game_objects("Title"):
    if not any(f"m_Father: {{fileID: {component(panel[0],224)}}}" in blocks[component(go,224)]
               for panel in panels):
        continue
    text = script_component(go,"UnityEngine.UI::UnityEngine.UI.Text")
    replace_line(text,"m_Color","{r: 0.17, g: 0.13, b: 0.09, a: 1}")

for fid in order:
    if not blocks[fid].startswith("--- !u!1 &"):
        continue
    name = name_of(blocks[fid])
    if not name or not name.endswith(" Caption"):
        continue
    rect = component(fid,224)
    parent = int(re.search(r"m_Father: \{fileID: (\d+)\}",blocks[rect]).group(1))
    map_index = next((i for i,c in enumerate(contents) if component(c,224)==parent),None)
    if map_index is None:
        raise RuntimeError("Caption is outside map content")
    x,y = positions[(map_index,name[:-8])]
    set_position(rect,x,y-76)
    set_size(rect,190 if map_index==0 else 170,38)
    text = script_component(fid,"UnityEngine.UI::UnityEngine.UI.Text")
    replace_line(text,"m_Color","{r: 0.18, g: 0.14, b: 0.1, a: 1}")
    blocks[text] = re.sub(r"(?m)^(    m_FontSize: )\d+",lambda m:m.group(1)+str(18 if map_index==0 else 16),blocks[text],count=1)

new = []
for map_index,content in enumerate(contents):
    base = 3400000001 + map_index*4
    go,rect,renderer,image = (base,base+1,base+2,base+3)
    parent = component(content,224)
    blocks[parent] = blocks[parent].replace("  m_Children:\n",f"  m_Children:\n  - {{fileID: {rect}}}\n",1)
    new.extend((
        f"--- !u!1 &{go}\nGameObject:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n"
        "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  serializedVersion: 6\n"
        f"  m_Component:\n  - component: {{fileID: {rect}}}\n  - component: {{fileID: {renderer}}}\n"
        f"  - component: {{fileID: {image}}}\n  m_Layer: 5\n  m_Name: SpeciesMapBackground\n"
        "  m_TagString: Untagged\n  m_Icon: {fileID: 0}\n  m_NavMeshLayer: 0\n"
        "  m_StaticEditorFlags: 0\n  m_IsActive: 1\n",
        f"--- !u!224 &{rect}\nRectTransform:\n  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n"
        f"  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {go}}}\n"
        "  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\n  m_LocalPosition: {x: 0, y: 0, z: 0}\n"
        "  m_LocalScale: {x: 1, y: 1, z: 1}\n  m_ConstrainProportionsScale: 0\n"
        f"  m_Children:\n  m_Father: {{fileID: {parent}}}\n"
        "  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}\n"
        "  m_AnchorMin: {x: 0, y: 0}\n  m_AnchorMax: {x: 1, y: 1}\n"
        "  m_AnchoredPosition: {x: 0, y: 0}\n  m_SizeDelta: {x: 0, y: 0}\n"
        "  m_Pivot: {x: 0.5, y: 0.5}\n",
        f"--- !u!222 &{renderer}\nCanvasRenderer:\n  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n"
        f"  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {go}}}\n  m_CullTransparentMesh: 1\n",
        f"--- !u!114 &{image}\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n"
        f"  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {go}}}\n"
        "  m_Enabled: 1\n  m_EditorHideFlags: 0\n"
        "  m_Script: {fileID: 11500000, guid: fe87c0e1cc204ed48ad3b37840f39efc, type: 3}\n"
        "  m_Name:\n  m_EditorClassIdentifier: UnityEngine.UI::UnityEngine.UI.Image\n"
        "  m_Material: {fileID: 0}\n  m_Color: {r: 1, g: 1, b: 1, a: 1}\n"
        "  m_RaycastTarget: 0\n  m_RaycastPadding: {x: 0, y: 0, z: 0, w: 0}\n"
        "  m_Maskable: 1\n  m_OnCullStateChanged:\n    m_PersistentCalls:\n      m_Calls: []\n"
        f"  m_Sprite: {{fileID: 21300000, guid: {BACKGROUND_GUID}, type: 3}}\n"
        "  m_Type: 0\n  m_PreserveAspect: 0\n  m_FillCenter: 1\n"
        "  m_FillMethod: 4\n  m_FillAmount: 1\n  m_FillClockwise: 1\n"
        "  m_FillOrigin: 0\n  m_UseSpriteMesh: 0\n  m_PixelsPerUnitMultiplier: 1\n"
    ))

controller = 1640010099
blocks[controller] = blocks[controller].replace("  defaultZoom: 0.85\n  minZoom: 0.5\n  maxZoom: 1.6\n",
    "  defaultZoomMultiplier: 1\n  maxZoomMultiplier: 2.2\n")
if "defaultZoomMultiplier: 1" not in blocks[controller]:
    raise RuntimeError("Controller zoom fields could not be updated")

root_id = next(fid for fid in order if blocks[fid].startswith("--- !u!1660057539 &"))
output = []
for fid in order:
    if fid==root_id:
        output.extend(new)
    output.append(blocks[fid])
SCENE.write_text(header+"".join(output),encoding="utf-8",newline="")
print("Applied image to two authored maps; centered 30 nodes")
