"""Render the revised rocks in a separate Maya renderer process."""
import os
import maya.standalone
maya.standalone.initialize(name="python")
import maya.cmds as cmds

out=os.path.dirname(os.path.abspath(__file__))
try:
    cmds.file(os.path.join(out,"Tundra_Rocks_v2.ma"),open=True,force=True,executeScriptNodes=False)
    cam,shape=cmds.camera(name="Rock_DetailCamera",orthographic=True)
    cmds.setAttr(shape+".orthographicWidth",8.5)
    cmds.setAttr(shape+".nearClipPlane",.01)
    cmds.setAttr(shape+".farClipPlane",300)
    cmds.setAttr(shape+".backgroundColor",.37,.65,.69,type="double3")
    cmds.xform(cam,worldSpace=True,translation=(10,9,-10))
    target=cmds.spaceLocator(name="RockDetailAim")[0]
    cmds.move(-.5,1.4,5.1,target)
    constraint=cmds.aimConstraint(target,cam,aimVector=(0,0,-1),upVector=(0,1,0),worldUpType="scene")
    cmds.delete(constraint,target)
    cmds.colorManagementPrefs(edit=True,cmEnabled=False)
    for camera in cmds.ls(type="camera"):
        cmds.setAttr(camera+".renderable",camera==shape)
    # The close crop can make Maya Software's auto-fitted depth shadow map fail.
    for light in cmds.ls(type="directionalLight"):
        cmds.setAttr(light+".useDepthMapShadows",False)
    cmds.setAttr("defaultResolution.width",1200)
    cmds.setAttr("defaultResolution.height",900)
    cmds.setAttr("defaultResolution.deviceAspectRatio",4/3)
    cmds.setAttr("defaultRenderGlobals.imageFilePrefix",os.path.join(out,"Rocks_Detail_v2").replace("\\","/"),type="string")
    print("DETAIL_RENDER "+str(cmds.render(cam,x=1200,y=900)),flush=True)
finally:
    maya.standalone.uninitialize()
