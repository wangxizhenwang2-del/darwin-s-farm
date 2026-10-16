"""One-time authoring of the complete species-map UI hierarchy in WhiteBox.unity."""
from pathlib import Path
import json
import re
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SCENE = ROOT / "Assets/Scenes/WhiteBox.unity"
SPECIES = ROOT / "Assets/BiowebV46/Species"
ART = ROOT / "Assets/Resources/GameUI/SpeciesCircle.png"
CIRCLE_GUID = "379048da63d54f69a3246e9dbd112360"
IMAGE_GUID = "fe87c0e1cc204ed48ad3b37840f39efc"
TEXT_GUID = "5f7201a12d95ffc409449d95f23cf332"
BUTTON_GUID = "4e29b1a8efbd4b44bb3f3716e73f07ff"
MASK_GUID = "31a19414c41e5ae4aae2af33fee712f6"
RECTMASK_GUID = "3312d7739989d2b4e91e6319e9a96d76"
GRAPH_GUID = "3171fa2d27114c42acec9756bb7aa6ba"
NODE_GUID = "bf75190968c64b128ae5e3d64efbc73c"
FONT_GUID = "07522333e098a5741bd68818f4c8f22b"

scene = SCENE.read_text(encoding="utf-8")
if "m_Name: Species Maps" in scene:
    raise RuntimeError("Species maps are already authored in the scene")

ART.parent.mkdir(parents=True, exist_ok=True)
image = Image.new("RGBA", (128, 128))
pixels = image.load()
for y in range(128):
    for x in range(128):
        distance = ((x - 63.5) ** 2 + (y - 63.5) ** 2) ** .5
        pixels[x, y] = (255, 255, 255, max(0, min(255, round((64 - distance) * 255))))
image.save(ART)
template = next(p for p in ROOT.glob("Assets/**/*.png.meta") if "textureType: 8" in p.read_text(encoding="utf-8"))
metadata = template.read_text(encoding="utf-8")
metadata = re.sub(r"(?m)^guid: \w+", "guid: " + CIRCLE_GUID, metadata)
ART.with_suffix(".png.meta").write_text(metadata, encoding="utf-8")

species = []
for asset in SPECIES.glob("*.asset"):
    body = asset.read_text(encoding="utf-8")
    name = bytes(re.search(r'^  speciesName: "(.*)"$', body, re.M).group(1), "utf-8").decode("unicode_escape")
    guid = re.search(r"(?m)^guid: (\w+)", asset.with_suffix(".asset.meta").read_text(encoding="utf-8")).group(1)
    icon = re.search(r"(?m)^  portrait: \{fileID: \d+, guid: (\w+)", body).group(1)
    coords = [tuple(map(int, re.search(rf"(?m)^  {field}: \{{x: (\d+), y: (\d+)\}}", body).groups()))
              for field in ("evolutionGridPos", "foodGridPos")]
    trophic = int(re.search(r"(?m)^  trophicLevel: (\d+)", body).group(1))
    niche = int(re.search(r"(?m)^  baseEcologicalNiche: (\d+)", body).group(1))
    species.append(dict(name=name, guid=guid, icon=icon, coords=coords, trophic=trophic, niche=niche))
assert len(species) == 15
species.sort(key=lambda s: s["name"])

next_id = 3300000000
objects = []
blocks = []

def alloc():
    global next_id
    next_id += 1
    return next_id

def ref(value):
    return "{fileID: " + str(value) + "}"

def common(go, guid, class_name):
    return (f"  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n"
            f"  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n"
            f"  m_GameObject: {ref(go)}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n"
            f"  m_Script: {{fileID: 11500000, guid: {guid}, type: 3}}\n"
            f"  m_Name:\n  m_EditorClassIdentifier: {class_name}\n")

def add(name, parent=None, *, anchor=(.5,.5,.5,.5), pos=(0,0), size=(0,0), pivot=(.5,.5), scale=1, active=True):
    go, rect = alloc(), alloc()
    item = dict(go=go, rect=rect, name=name, parent=parent, children=[], components=[],
                anchor=anchor, pos=pos, size=size, pivot=pivot, scale=scale, active=active)
    objects.append(item)
    if parent is not None: parent["children"].append(rect)
    return item

def component(item, kind, body):
    cid = alloc()
    item["components"].append(cid)
    blocks.append((cid, kind, body))
    return cid

def renderer(item):
    return component(item, 222, f"CanvasRenderer:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {ref(item['go'])}\n  m_CullTransparentMesh: 1\n")

def graphic(item, cls, guid, color, sprite="{fileID: 0}", raycast=1):
    body = common(item["go"], guid, cls)
    body += (f"  m_Material: {{fileID: 0}}\n  m_Color: {color}\n  m_RaycastTarget: {raycast}\n"
             "  m_RaycastPadding: {x: 0, y: 0, z: 0, w: 0}\n  m_Maskable: 1\n"
             "  m_OnCullStateChanged:\n    m_PersistentCalls:\n      m_Calls: []\n")
    if cls.endswith("Image"):
        body += (f"  m_Sprite: {sprite}\n  m_Type: 0\n  m_PreserveAspect: 1\n"
                 "  m_FillCenter: 1\n  m_FillMethod: 4\n  m_FillAmount: 1\n"
                 "  m_FillClockwise: 1\n  m_FillOrigin: 0\n  m_UseSpriteMesh: 0\n"
                 "  m_PixelsPerUnitMultiplier: 1\n")
    return component(item, 114, "MonoBehaviour:\n" + body)

def ui_image(item, color="{r: 1, g: 1, b: 1, a: 1}", sprite="{fileID: 0}", raycast=1):
    renderer(item)
    return graphic(item, "UnityEngine.UI::UnityEngine.UI.Image", IMAGE_GUID, color, sprite, raycast)

def ui_text(item, value, size=19, color="{r: 0.94, g: 0.96, b: 0.9, a: 1}"):
    renderer(item)
    body = common(item["go"], TEXT_GUID, "UnityEngine.UI::UnityEngine.UI.Text")
    body += (f"  m_Material: {{fileID: 0}}\n  m_Color: {color}\n  m_RaycastTarget: 0\n"
             "  m_RaycastPadding: {x: 0, y: 0, z: 0, w: 0}\n  m_Maskable: 1\n"
             "  m_OnCullStateChanged:\n    m_PersistentCalls:\n      m_Calls: []\n"
             f"  m_FontData:\n    m_Font: {{fileID: 12800000, guid: {FONT_GUID}, type: 3}}\n"
             f"    m_FontSize: {size}\n    m_FontStyle: 0\n    m_BestFit: 0\n"
             "    m_MinSize: 3\n    m_MaxSize: 40\n    m_Alignment: 4\n"
             "    m_AlignByGeometry: 0\n    m_RichText: 1\n"
             "    m_HorizontalOverflow: 0\n    m_VerticalOverflow: 0\n"
             "    m_LineSpacing: 1\n"
             f"  m_Text: {json.dumps(value, ensure_ascii=False)}\n")
    return component(item, 114, "MonoBehaviour:\n" + body)

def button(item, label):
    image_id = ui_image(item, "{r: 0.23, g: 0.39, b: 0.38, a: 1}")
    body = common(item["go"], BUTTON_GUID, "UnityEngine.UI::UnityEngine.UI.Button")
    body += ("  m_Navigation:\n    m_Mode: 3\n    m_WrapAround: 0\n"
             "    m_SelectOnUp: {fileID: 0}\n    m_SelectOnDown: {fileID: 0}\n"
             "    m_SelectOnLeft: {fileID: 0}\n    m_SelectOnRight: {fileID: 0}\n"
             "  m_Transition: 1\n  m_Colors:\n"
             "    m_NormalColor: {r: 1, g: 1, b: 1, a: 1}\n"
             "    m_HighlightedColor: {r: 0.96, g: 0.96, b: 0.96, a: 1}\n"
             "    m_PressedColor: {r: 0.78, g: 0.78, b: 0.78, a: 1}\n"
             "    m_SelectedColor: {r: 0.96, g: 0.96, b: 0.96, a: 1}\n"
             "    m_DisabledColor: {r: 0.78, g: 0.78, b: 0.78, a: 0.5}\n"
             "    m_ColorMultiplier: 1\n    m_FadeDuration: 0.1\n"
             "  m_SpriteState:\n    m_HighlightedSprite: {fileID: 0}\n"
             "    m_PressedSprite: {fileID: 0}\n    m_SelectedSprite: {fileID: 0}\n"
             "    m_DisabledSprite: {fileID: 0}\n"
             "  m_AnimationTriggers:\n    m_NormalTrigger: Normal\n"
             "    m_HighlightedTrigger: Highlighted\n    m_PressedTrigger: Pressed\n"
             "    m_SelectedTrigger: Selected\n    m_DisabledTrigger: Disabled\n"
             f"  m_Interactable: 1\n  m_TargetGraphic: {ref(image_id)}\n"
             "  m_OnClick:\n    m_PersistentCalls:\n      m_Calls: []\n")
    button_id = component(item, 114, "MonoBehaviour:\n" + body)
    text_item = add("Label", item, anchor=(0,0,1,1), size=(0,0))
    ui_text(text_item, label, 23)
    return button_id

def simple_component(item, guid, class_name, extra):
    return component(item, 114, "MonoBehaviour:\n" + common(item["go"], guid, class_name) + extra)

overlay = add("Species Maps", anchor=(0,0,1,1), size=(0,0), active=False)
ui_image(overlay, "{r: 0.02, g: 0.035, b: 0.045, a: 0.9}")
pages = []
for map_index, (name, cols, spacing_x) in enumerate((("Evolution Map",4,245),("Food Web",6,255))):
    panel = add(name, overlay, anchor=(0,0,1,1), size=(-76,-60), active=map_index == 0)
    ui_image(panel, "{r: 0.085, g: 0.13, b: 0.15, a: 1}")
    title = add("Title", panel, anchor=(0,1,1,1), pos=(0,-37), size=(-340,50), pivot=(.5,.5))
    ui_text(title, "食物网 / Food Web" if map_index else "进化网 / Evolution Map", 30)
    switch = add("Switch Map", panel, anchor=(1,1,1,1), pos=(-165,-36), size=(145,48))
    switch_id = button(switch, "进化网" if map_index else "食物网")
    close = add("Close Map", panel, anchor=(1,1,1,1), pos=(-45,-36), size=(64,48))
    close_id = button(close, "×")
    viewport = add("Viewport", panel, anchor=(0,0,1,1), pos=(0,-30), size=(-44,-104))
    ui_image(viewport, "{r: 0.13, g: 0.18, b: 0.2, a: 1}")
    simple_component(viewport, RECTMASK_GUID, "UnityEngine.UI::UnityEngine.UI.RectMask2D",
                     "  m_Padding: {x: 0, y: 0, z: 0, w: 0}\n  m_Softness: {x: 0, y: 0}\n")
    content = add("Grid Content", viewport, size=((cols-1)*spacing_x+330, 4*190+300), scale=.85)
    graph = add("Relationship Lines", content, anchor=(0,0,1,1), size=(0,0))
    renderer(graph)
    graph_id = graphic(graph, "Assembly-CSharp::SpeciesMapGraphGraphic", GRAPH_GUID,
                       "{r: 1, g: 1, b: 1, a: 1}", raycast=0)
    node_ids = []
    for entry in species:
        col,row = entry["coords"][map_index]
        x = (col - (cols+1)/2) * spacing_x
        y = (3-row) * 190
        root = add(entry["name"], content, pos=(x,y), size=(104,104))
        circle_ref = f"{{fileID: 21300000, guid: {CIRCLE_GUID}, type: 3}}"
        ui_image(root, "{r: 0.9, g: 0.94, b: 0.88, a: 1}", circle_ref)
        simple_component(root, MASK_GUID, "UnityEngine.UI::UnityEngine.UI.Mask", "  m_ShowMaskGraphic: 1\n")
        portrait = add("Portrait", root, anchor=(0,0,1,1), size=(0,0))
        portrait_ref = f"{{fileID: 21300000, guid: {entry['icon']}, type: 3}}"
        portrait_id = ui_image(portrait, sprite=portrait_ref, raycast=0)
        caption = add(entry["name"] + " Caption", content, pos=(x,y-78), size=(220,38))
        niche = ("陆生","水生","空中")[entry["niche"]]
        caption_id = ui_text(caption, f"{entry['name']} | L{entry['trophic']} {niche}")
        extra = (f"  species: {{fileID: 11400000, guid: {entry['guid']}, type: 2}}\n"
                 f"  portrait: {ref(portrait_id)}\n  label: {ref(caption_id)}\n")
        node_ids.append(simple_component(root, NODE_GUID, "Assembly-CSharp::SpeciesMapNodeView", extra))
    pages.append(dict(panel=panel["go"], viewport=viewport["rect"], content=content["rect"],
                      graph=graph_id, switch=switch_id, close=close_id, nodes=node_ids))

def fmt(v): return f"{v:g}"
for item in objects:
    comps = [item["rect"], *item["components"]]
    game = (f"--- !u!1 &{item['go']}\nGameObject:\n  m_ObjectHideFlags: 0\n"
            "  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n"
            "  m_PrefabAsset: {fileID: 0}\n  serializedVersion: 6\n  m_Component:\n")
    game += "".join(f"  - component: {ref(cid)}\n" for cid in comps)
    game += (f"  m_Layer: 5\n  m_Name: {json.dumps(item['name'], ensure_ascii=False)}\n"
             "  m_TagString: Untagged\n  m_Icon: {fileID: 0}\n  m_NavMeshLayer: 0\n"
             f"  m_StaticEditorFlags: 0\n  m_IsActive: {int(item['active'])}\n")
    ax,ay,bx,by=item["anchor"]; px,py=item["pos"]; sx,sy=item["size"]; vx,vy=item["pivot"]
    parent_rect = item["parent"]["rect"] if item["parent"] else 1640010070
    rect = (f"--- !u!224 &{item['rect']}\nRectTransform:\n  m_ObjectHideFlags: 0\n"
            "  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n"
            f"  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {ref(item['go'])}\n"
            "  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\n  m_LocalPosition: {x: 0, y: 0, z: 0}\n"
            f"  m_LocalScale: {{x: {fmt(item['scale'])}, y: {fmt(item['scale'])}, z: {fmt(item['scale'])}}}\n"
            "  m_ConstrainProportionsScale: 0\n  m_Children:\n")
    rect += "".join(f"  - {ref(cid)}\n" for cid in item["children"])
    rect += (f"  m_Father: {ref(parent_rect)}\n  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}\n"
             f"  m_AnchorMin: {{x: {fmt(ax)}, y: {fmt(ay)}}}\n"
             f"  m_AnchorMax: {{x: {fmt(bx)}, y: {fmt(by)}}}\n"
             f"  m_AnchoredPosition: {{x: {fmt(px)}, y: {fmt(py)}}}\n"
             f"  m_SizeDelta: {{x: {fmt(sx)}, y: {fmt(sy)}}}\n"
             f"  m_Pivot: {{x: {fmt(vx)}, y: {fmt(vy)}}}\n")
    blocks.extend(((item["go"],None,game),(item["rect"],None,rect)))

all_blocks = "".join(body if kind is None else f"--- !u!{kind} &{cid}\n{body}" for cid,kind,body in blocks)
scene = scene.replace("  - {fileID: 770669926}\n  m_Father: {fileID: 0}",
                      f"  - {{fileID: 770669926}}\n  - {ref(overlay['rect'])}\n  m_Father: {{fileID: 0}}",1)
serialized = (f"  overlay: {ref(overlay['go'])}\n  pages:\n" +
              "".join(f"  - panel: {ref(page['panel'])}\n    viewport: {ref(page['viewport'])}\n"
                      f"    content: {ref(page['content'])}\n    graph: {ref(page['graph'])}\n"
                      f"    switchButton: {ref(page['switch'])}\n    closeButton: {ref(page['close'])}\n"
                      "    nodes:\n" + "".join(f"    - {ref(node)}\n" for node in page["nodes"])
                      for page in pages) +
              "  defaultZoom: 0.85\n  minZoom: 0.5\n  maxZoom: 1.6\n  keyboardPanSpeed: 650\n")
pattern = r"(--- !u!114 &1640010099\nMonoBehaviour:.*?  m_EditorClassIdentifier: Assembly-CSharp::SpeciesMapsUI\n).*?(?=--- !u!)"
scene,count = re.subn(pattern,lambda m:m.group(1)+serialized,scene,flags=re.S)
assert count==1
scene = scene.replace("--- !u!1660057539 &9223372036854775807\nSceneRoots:",
                      all_blocks+"--- !u!1660057539 &9223372036854775807\nSceneRoots:",1)
SCENE.write_text(scene,encoding="utf-8",newline="")
print(f"Authored {len(objects)} UI objects, 30 species nodes, 2 panels")
