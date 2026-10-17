"""Reopen the actual Maya and FBX deliverables and check geometry and scale."""
import json
import os
import maya.standalone

maya.standalone.initialize(name="python")
import maya.cmds as cmds

out = os.path.dirname(os.path.abspath(__file__))


def inspect():
    shapes = cmds.ls(type="mesh", long=True, noIntermediate=True) or []
    transforms = cmds.listRelatives(shapes, parent=True, fullPath=True) or []
    bounds = cmds.exactWorldBoundingBox(transforms)
    assert abs(bounds[3]-bounds[0]-17.0) < 0.002, bounds
    assert abs(bounds[5]-bounds[2]-17.0) < 0.002, bounds
    assert abs(bounds[1]) < 0.002, bounds
    assert len(shapes) > 60, len(shapes)
    assert not (cmds.polyInfo(transforms, nonManifoldEdges=True) or [])
    assert all(cmds.polyEvaluate(m, uvcoord=True) > 0 for m in shapes)
    assert not cmds.ls(type="file"), "Unexpected external texture dependency"
    assert cmds.objExists("Tundra_17m_GRP")
    for key in ("Ground", "Rocks", "Snow", "Shrubs", "Grass", "Water", "Ink"):
        assert cmds.objExists(key+"_GRP"), key
    return {"meshes": len(shapes), "triangles": cmds.polyEvaluate(transforms, triangle=True),
            "bounds": bounds, "units": cmds.currentUnit(query=True, linear=True)}


try:
    cmds.file(os.path.join(out, "Tundra_Blockout_v1.ma"), open=True, force=True, executeScriptNodes=False)
    native = inspect()
    cmds.file(new=True, force=True)
    cmds.currentUnit(linear="m")
    cmds.loadPlugin("fbxmaya", quiet=True)
    cmds.file(os.path.join(out, "Tundra_Blockout_v1.fbx"), i=True, type="FBX", ignoreVersion=True,
              mergeNamespacesOnClash=False, namespace=":", executeScriptNodes=False)
    fbx = inspect()
    assert native["meshes"] == fbx["meshes"], (native, fbx)
    assert native["triangles"] == fbx["triangles"], (native, fbx)
    result = {"passed": True, "mayaReopen": native, "fbxReimport": fbx}
    with open(os.path.join(out, "reopen-validation.json"), "w", encoding="utf-8") as stream:
        json.dump(result, stream, indent=2)
    print("TUNDRA_VERIFIED " + json.dumps(result), flush=True)
finally:
    maya.standalone.uninitialize()
