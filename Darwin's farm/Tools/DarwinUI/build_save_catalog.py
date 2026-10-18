"""Build stable asset-reference catalog for runtime save files."""
import re
import uuid
from pathlib import Path

root = Path(__file__).resolve().parents[2]
scripts = root / "Assets/Scripts/UI"
for name in ("SaveAssetCatalog", "SaveFieldCodec", "GameSaveService"):
    meta = scripts / (name + ".cs.meta")
    if not meta.exists():
        meta.write_text(f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nMonoImporter:\n  externalObjects: {{}}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {{instanceID: 0}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8")

source = (scripts / "SaveAssetCatalog.cs.meta").read_text()
script_guid = re.search(r"guid: ([a-f0-9]+)", source).group(1)
entries = []
for folder, kind in ((root / "Assets/Scripts/map/data", "tiles"),
                     (root / "Assets/BiowebV46/Species", "species"),
                     (root / "Assets/TestBioweb/Species", "species")):
    for asset in sorted(folder.glob("*.asset")):
        expected = "MapTileDefinition" if kind == "tiles" else "SpeciesData"
        if expected not in asset.read_text(encoding="utf-8-sig"): continue
        guid = re.search(r"(?m)^guid: ([a-f0-9]+)$", asset.with_suffix(".asset.meta").read_text(encoding="utf-8-sig")).group(1)
        entries.append((kind, guid))
output = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:", "--- !u!114 &11400000", "MonoBehaviour:", "  m_ObjectHideFlags: 0", "  m_CorrespondingSourceObject: {fileID: 0}", "  m_PrefabInstance: {fileID: 0}", "  m_PrefabAsset: {fileID: 0}", "  m_GameObject: {fileID: 0}", "  m_Enabled: 1", "  m_EditorHideFlags: 0", f"  m_Script: {{fileID: 11500000, guid: {script_guid}, type: 3}}", "  m_Name: SaveAssetCatalog", "  m_EditorClassIdentifier: Assembly-CSharp::SaveAssetCatalog"]
for kind in ("tiles", "species"):
    output.append(f"  {kind}:")
    for item_kind, guid in entries:
        if item_kind == kind:
            output += [f"  - id: {guid}", f"    asset: {{fileID: 11400000, guid: {guid}, type: 2}}"]
path = root / "Assets/Resources/DarwinJournal/SaveAssetCatalog.asset"
path.write_text("\n".join(output)+"\n", encoding="utf-8")
meta = path.with_suffix(".asset.meta")
if not meta.exists():
    meta.write_text(f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8")
print(f"Catalog: {sum(k=='tiles' for k,_ in entries)} tiles, {sum(k=='species' for k,_ in entries)} species")
