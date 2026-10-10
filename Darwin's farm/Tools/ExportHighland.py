"""Export the supplied Maya scene as FBX without running embedded script nodes.

Run with Maya's mayapy: ExportHighland.py source.mb destination.fbx report.json
The source is never saved or modified on disk.
"""
import json
import os
import sys

import maya.standalone

maya.standalone.initialize(name="python")
try:
    import maya.cmds as cmds
    import maya.mel as mel
    source, destination, report_path = map(os.path.abspath, sys.argv[1:4])
    cmds.file(source, open=True, force=True, ignoreVersion=True, executeScriptNodes=False)
    meshes = cmds.ls(type="mesh", long=True, noIntermediate=True) or []
    report = {"source": source, "destination": destination, "meshes": [], "materials": [], "textures": []}
    for mesh in meshes:
        uv_sets = cmds.polyUVSet(mesh, query=True, allUVSets=True) or []
        uv_values = cmds.polyEditUV(mesh + ".map[*]", query=True) or []
        report["meshes"].append({
            "name": mesh,
            "vertices": cmds.polyEvaluate(mesh, vertex=True),
            "triangles": cmds.polyEvaluate(mesh, triangle=True),
            "uvSets": uv_sets,
            "uvCount": len(uv_values) // 2,
            "uvMin": [min(uv_values[::2]), min(uv_values[1::2])] if uv_values else None,
            "uvMax": [max(uv_values[::2]), max(uv_values[1::2])] if uv_values else None,
            "bounds": cmds.exactWorldBoundingBox(mesh),
        })
    for material in cmds.ls(materials=True) or []:
        color = cmds.getAttr(material + ".color")[0] if cmds.attributeQuery("color", node=material, exists=True) else [0.7, 0.7, 0.7]
        report["materials"].append({"name": material, "rgb": list(color), "type": cmds.nodeType(material)})
    for node in cmds.ls(type="file") or []:
        report["textures"].append({"node": node, "path": cmds.getAttr(node + ".fileTextureName")})
    os.makedirs(os.path.dirname(destination), exist_ok=True)
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    cmds.loadPlugin("fbxmaya", quiet=True)
    mel.eval("FBXResetExport;")
    mel.eval("FBXExportCameras -v false;")
    mel.eval("FBXExportLights -v false;")
    mel.eval("FBXExportBakeComplexAnimation -v false;")
    mel.eval("FBXExportInputConnections -v false;")
    mel.eval("FBXExportSmoothingGroups -v true;")
    transforms = list(set(cmds.listRelatives(meshes, parent=True, fullPath=True) or []))
    if not transforms:
        raise RuntimeError("The source scene has no polygon meshes.")
    cmds.select(transforms, replace=True)
    mel.eval('FBXExport -f "' + destination.replace("\\", "/") + '" -s;')
    with open(report_path, "w", encoding="utf-8") as stream:
        json.dump(report, stream, ensure_ascii=False, indent=2)
    print("HIGHland_EXPORT_OK meshes=" + str(len(meshes)))
finally:
    maya.standalone.uninitialize()
