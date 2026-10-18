"""Build an editable low-poly tundra tile using Maya 2027 standalone."""
import json
import math
import os
import random
import sys

import maya.standalone

maya.standalone.initialize(name="python")
import maya.cmds as cmds
import maya.mel as mel
import maya.api.OpenMaya as om

OUT = os.path.dirname(os.path.abspath(__file__))
RNG = random.Random(151026)
INK_V, INK_F = [], []
MAT = {}


def material(name, color, ambient=0.23):
    node = cmds.shadingNode("lambert", asShader=True, name=name)
    cmds.setAttr(node + ".color", *color, type="double3")
    cmds.setAttr(node + ".ambientColor", ambient, ambient, ambient, type="double3")
    cmds.setAttr(node + ".incandescence", *(c * 0.15 for c in color), type="double3")
    cmds.setAttr(node + ".diffuse", 0.68)
    sg = cmds.sets(renderable=True, noSurfaceShader=True, empty=True, name=name + "SG")
    cmds.connectAttr(node + ".outColor", sg + ".surfaceShader", force=True)
    MAT[name] = sg
    return sg


def mesh(name, vertices, faces, group, mat):
    fn = om.MFnMesh()
    # OpenMaya always takes centimeters even when maya.cmds uses meters.
    fn.create(om.MPointArray([om.MPoint(*(c * 100.0 for c in v)) for v in vertices]),
              [len(f) for f in faces], [i for f in faces for i in f])
    obj = cmds.rename(om.MFnDagNode(fn.parent(0)).fullPathName(), name)
    cmds.parent(obj, group)
    cmds.sets(obj, edit=True, forceElement=MAT[mat])
    cmds.polySoftEdge(obj, angle=0, constructionHistory=False)
    cmds.polyAutoProjection(obj, layout=2, planes=6, scaleMode=1,
                            percentageSpace=2, optimize=1, constructionHistory=False)
    return obj


def rod(a, b, radius=0.012):
    a, b = om.MVector(a), om.MVector(b)
    axis = b - a
    if axis.length() < 0.001:
        return
    axis.normalize()
    ref = om.MVector(0, 1, 0) if abs(axis.y) < 0.9 else om.MVector(1, 0, 0)
    u = (axis ^ ref).normal() * radius
    v = (axis ^ u).normal() * radius
    start = len(INK_V)
    for center in (a, b):
        for i in range(3):
            p = center + u * math.cos(i * math.tau / 3) + v * math.sin(i * math.tau / 3)
            INK_V.append(tuple(p))
    for i in range(3):
        j = (i + 1) % 3
        INK_F.append([start+i, start+j, start+3+j, start+3+i])
    INK_F.extend([[start+2, start+1, start], [start+3, start+4, start+5]])


def ground_y(x, z):
    edge = max(0.0, min(1.0, (8.5 - max(abs(x), abs(z))) / 1.8))
    h = 0.30 + edge * (0.065 + 0.035 * math.sin(x * 0.8 + z * 0.6)
                          + 0.028 * math.cos(z * 1.2 - x * 0.4))
    pond_distance = ((x - 3.9) / 2.2) ** 2 + ((z + 1.4) / 1.6) ** 2
    blend = max(0.0, min(1.0, (1.65 - pond_distance) / 0.45))
    return h * (1.0 - blend) + 0.33 * blend


def patch(name, x, z, rx, rz, mat, group, count=12, y=None):
    verts = [(x, ground_y(x, z) + 0.026 if y is None else y, z)]
    for i in range(count):
        angle = i * math.tau / count
        jitter = RNG.uniform(0.83, 1.10)
        px, pz = x + math.cos(angle) * rx * jitter, z + math.sin(angle) * rz * jitter
        verts.append((px, ground_y(px, pz) + 0.026 if y is None else y, pz))
    return mesh(name, verts, [[0, 1+(i+1)%count, 1+i] for i in range(count)], group, mat)


def rock(name, x, z, rx, rz, height, cap=False):
    n = RNG.choice([6, 7, 8])
    base_y = ground_y(x, z) - 0.035
    phase = RNG.uniform(0, math.tau)
    radii = [RNG.uniform(0.84, 1.12) for _ in range(n)]
    crown = [RNG.uniform(-0.07, 0.07) for _ in range(n)]
    verts = []
    for level, factor in ((0.0, 1.0), (0.63, 0.91), (1.0, 0.57)):
        for i in range(n):
            a = phase + i * math.tau / n
            verts.append((x + math.cos(a) * rx * radii[i] * factor + level * rx * 0.13,
                          base_y + height * (level + crown[i] * level),
                          z + math.sin(a) * rz * radii[i] * factor + level * rz * 0.10))
    faces = [list(range(n))]
    for level in range(2):
        for i in range(n):
            j = (i+1)%n
            faces.append([level*n+i, (level+1)*n+i, (level+1)*n+j, level*n+j])
    faces.append(list(reversed(range(2*n, 3*n))))
    obj = mesh(name, verts, faces, GROUPS["Rocks"], "Rock_Slate")
    top_face = len(faces) - 1
    cmds.sets(obj + ".f[" + str(top_face) + "]", edit=True, forceElement=MAT["Rock_Light"])
    for fi, face in enumerate(faces[1:-1], start=1):
        a, b, c, d = [om.MVector(verts[v]) for v in face]
        normal = ((b-a) ^ (c-a)).normal()
        shade = "Rock_Lavender" if normal.x > 0.15 else "Rock_Light" if normal.z < -0.45 else "Rock_Slate"
        cmds.sets(obj + ".f[" + str(fi) + "]", edit=True, forceElement=MAT[shade])
        if height > 0.9 and normal.x > 0.25 and fi <= n:
            for t in (0.17, 0.30, 0.43, 0.56, 0.69, 0.82):
                left = a + (b-a) * t + (d-a) * 0.18 + normal * 0.013
                right = a + (b-a) * min(t+0.065, 0.95) + (d-a) * 0.77 + normal * 0.013
                rod(tuple(left), tuple(right), 0.008)
    if height > 0.35:
        for level in (1, 2):
            for i in range(n):
                rod(verts[level*n+i], verts[level*n+(i+1)%n], 0.010)
        for i in range(n):
            rod(verts[i], verts[n+i], 0.010)
            rod(verts[n+i], verts[2*n+i], 0.010)
    if cap:
        cap_verts = [(px, py+0.025, pz) for px, py, pz in verts[2*n:3*n]]
        mesh(name + "_SnowCap", cap_verts, [list(reversed(range(n)))], GROUPS["Snow"], "Snow_Ivory")
    return obj


def shrub(name, x, z, scale=1.0):
    group = cmds.group(empty=True, name=name + "_GRP", parent=GROUPS["Shrubs"])
    for i, (dx, dz, sx) in enumerate([(-0.22, 0, 0.43), (0.18, 0.07, 0.51), (0, -0.15, 0.31)]):
        y = ground_y(x+dx*scale, z+dz*scale)
        obj = cmds.polySphere(name=name+"_Lobe"+str(i+1), radius=1,
                              subdivisionsX=7, subdivisionsY=4, constructionHistory=False)[0]
        cmds.scale(sx*scale, 0.26*scale, sx*0.72*scale, obj)
        cmds.move(x+dx*scale, y+0.23*scale, z+dz*scale, obj)
        cmds.makeIdentity(obj, apply=True, translate=True, rotate=True, scale=True)
        cmds.parent(obj, group)
        cmds.sets(obj, edit=True, forceElement=MAT[["Shrub_Sage", "Shrub_Olive", "Shrub_Frost"][i]])
        cmds.polySoftEdge(obj, angle=0, constructionHistory=False)
    root = (x, ground_y(x, z)+0.01, z)
    for dx, dz in ((-0.28, 0), (0.29, 0.12), (0, -0.28)):
        rod(root, (x+dx*scale, root[1]+0.40*scale, z+dz*scale), 0.015)


def grass(name, x, z, scale=1):
    verts, faces = [], []
    for i in range(RNG.randint(8, 12)):
        a = RNG.random() * math.tau
        radius = RNG.uniform(0.0, 0.23) * scale
        bx, bz = x+math.cos(a)*radius, z+math.sin(a)*radius
        by = ground_y(bx, bz) + 0.025
        h = RNG.uniform(0.22, 0.58)*scale
        w = RNG.uniform(0.020, 0.045)*scale
        vx, vz = math.cos(a)*w, math.sin(a)*w
        lean_x, lean_z = 0.1*scale, -0.05*scale
        start = len(verts)
        verts.extend([(bx-vx, by, bz-vz), (bx+vx, by, bz+vz),
                      (bx+lean_x, by+h, bz+lean_z),
                      (bx-vz, by, bz+vx), (bx+vz, by, bz-vx)])
        faces.extend([[start, start+2, start+1], [start+3, start+2, start+4]])
    obj = mesh(name, verts, faces, GROUPS["Grass"], "Grass_Straw")
    for i in range(0, len(faces), 4):
        cmds.sets(obj+".f["+str(i)+"]", edit=True, forceElement=MAT["Grass_Ochre"])
    return obj


def build():
    cmds.file(new=True, force=True)
    cmds.currentUnit(linear="m", angle="deg", time="film")
    cmds.upAxis(axis="y")
    palette = {
        "Ground_Moss": (0.67, 0.73, 0.52), "Ground_Sage": (0.60, 0.68, 0.52),
        "Ground_Lichen": (0.77, 0.79, 0.60), "Ground_Frost": (0.71, 0.77, 0.66),
        "Ground_Soil": (0.77, 0.72, 0.62), "Slab_Edge": (0.66, 0.66, 0.52),
        "Rock_Slate": (0.58, 0.61, 0.63), "Rock_Light": (0.73, 0.73, 0.70),
        "Rock_Lavender": (0.52, 0.53, 0.61), "Snow_Ivory": (0.90, 0.94, 0.92),
        "Shrub_Sage": (0.51, 0.60, 0.43), "Shrub_Olive": (0.57, 0.64, 0.45),
        "Shrub_Frost": (0.64, 0.71, 0.55), "Grass_Straw": (0.79, 0.70, 0.45),
        "Grass_Ochre": (0.62, 0.53, 0.34), "Water_Mist": (0.48, 0.69, 0.74),
        "Shore_Mud": (0.65, 0.65, 0.57), "Ink_Charcoal": (0.20, 0.23, 0.24),
    }
    for name, color in palette.items():
        material(name, color)
    root = cmds.group(empty=True, name="Tundra_17m_GRP")
    global GROUPS
    GROUPS = {key: cmds.group(empty=True, name=key+"_GRP", parent=root)
              for key in ["Ground", "Rocks", "Snow", "Shrubs", "Grass", "Water", "Ink"]}
    cmds.addAttr(root, longName="tileWidthMeters", attributeType="double", defaultValue=17)
    cmds.addAttr(root, longName="description", dataType="string")
    cmds.setAttr(root+".description", "Darwin's Farm tundra blockout. Y up. 17m square. Origin at base center.", type="string")

    n = 26
    vertices, faces, mids = [], [], []
    for iz in range(n+1):
        for ix in range(n+1):
            x, z = -8.5+17*ix/n, -8.5+17*iz/n
            if 0 < ix < n and 0 < iz < n:
                x += RNG.uniform(-0.12, 0.12)
                z += RNG.uniform(-0.12, 0.12)
            vertices.append((x, ground_y(x, z), z))
    for iz in range(n):
        for ix in range(n):
            a = iz*(n+1)+ix
            fs = [[a, a+n+1, a+n+2], [a, a+n+2, a+1]]
            for face in fs:
                faces.append(face)
                x = sum(vertices[i][0] for i in face)/3
                z = sum(vertices[i][2] for i in face)/3
                v = math.sin(x*1.23+math.cos(z*1.4)) + 0.6*math.cos(z*1.65-x*0.43)
                soil = (abs(x)>5.1 and z>3.1) or (z>5.0 and abs(x)<4.2)
                mids.append("Ground_Soil" if soil else "Ground_Lichen" if v>0.75
                            else "Ground_Sage" if v < -0.65 else "Ground_Frost" if x<-4 and z<-1 else "Ground_Moss")
    surface = mesh("Ground_Surface", vertices, faces, GROUPS["Ground"], "Ground_Moss")
    for mat in set(mids):
        selected = [surface+".f["+str(i)+"]" for i, value in enumerate(mids) if value==mat]
        cmds.sets(selected, edit=True, forceElement=MAT[mat])
    skirt_v = [(-8.5,0,-8.5), (-8.5,0,8.5), (8.5,0,8.5), (8.5,0,-8.5),
               (-8.5,.30,-8.5), (-8.5,.30,8.5), (8.5,.30,8.5), (8.5,.30,-8.5)]
    mesh("Ground_SlabSides", skirt_v, [[0,4,7,3],[3,7,6,2],[2,6,5,1],[1,5,4,0],[0,3,2,1]],
         GROUPS["Ground"], "Slab_Edge")
    outline_v = [(max(-8.48,min(8.48,x)),max(.016,y),max(-8.48,min(8.48,z)))
                 for x,y,z in skirt_v]
    for i in range(4):
        j = (i+1)%4
        rod(outline_v[i], outline_v[j], .010)
        rod(outline_v[i+4], outline_v[j+4], .010)
        rod(outline_v[i], outline_v[i+4], .010)

    rocks = [(-3.8,5.5,1.2,.95,1.7,False),(-1.1,6.0,1.65,1.15,2.65,True),
             (1.9,5.4,1.45,1.08,2.0,True),(4.3,5.8,.75,.72,.9,False),
             (-5.8,2.3,.95,.72,1.1,True),(-6.3,-2.0,1.12,.86,1.45,False),
             (-4.5,-5.4,.86,.66,.75,False),(1.5,-6.1,1.0,.74,1.02,True),
             (6.2,1.3,.85,.73,.95,False),(6.2,-4.9,.7,.5,.55,False)]
    for i, data in enumerate(rocks, 1):
        rock("Rock_%02d"%i, *data)
    for i,(x,z) in enumerate([(-4.5,4.8),(-2.2,4.6),(3.6,4.9),(-5.1,-4.7),(1.2,-5.1),(5.5,.1)],1):
        rock("RockChip_%02d"%i,x,z,.26,.22,.23)

    snow = [(-2.6,4.7,1.5,.65),(0.3,5.0,1.1,.66),(3.3,5.6,.95,.67),
            (-5.6,1.5,.9,.66),(-6.0,-3.0,1.2,.55),(-4.6,-5.8,.7,.38),
            (1.3,-6.8,1.4,.50),(2.6,-5.8,.67,.45),(6.4,1.7,.65,.9),
            (5.9,-4.6,.65,.55),(-1.8,6.7,.65,.42)]
    for i, data in enumerate(snow,1):
        patch("SnowPatch_%02d"%i,*data,"Snow_Ivory",GROUPS["Snow"])

    for i,(x,z,s) in enumerate([(-5.2,4.1,1),(-3.0,3.8,.8),(4.3,4.7,.9),
                                (-6.2,-.2,1.1),(-5.4,-3.7,.9),(-3.8,-6.3,.85),
                                (3.1,-5.4,.9),(6.1,-2.9,1),(5.8,2.5,1.05)],1):
        shrub("DwarfShrub_%02d"%i,x,z,s)
    for i,(x,z,s) in enumerate([(-5.0,5.9,1),(-3.1,5.5,.9),(.9,3.8,1),(3.7,5.9,.85),
                                (5.9,4.5,1),(-6.1,3.4,.7),(-4.4,1.2,.8),(-6.5,-3.5,1),
                                (-4.4,-4.1,.9),(-3.0,-5.7,1),(-1.7,-6.4,.8),(.3,-4.7,.75),
                                (2.9,-6.1,.7),(5.8,-5.5,.9),(6.4,-.3,.8),(2.4,.8,.8),
                                (5.9,2.0,.7),(-2.5,2.2,.7),(4.5,3.5,.65)],1):
        grass("SedgeTuft_%02d"%i,x,z,s)

    patch("Pond_Shore",3.9,-1.4,1.75,1.15,"Shore_Mud",GROUPS["Water"],count=14,y=.349)
    patch("Pond_ShallowWater",3.9,-1.4,1.54,.98,"Water_Mist",GROUPS["Water"],count=14,y=.365)
    for i,(x,z) in enumerate([(2.5,-.95),(3.0,-2.2),(4.9,-2.1),(5.3,-1.1),(4.6,-.45)],1):
        rock("PondPebble_%02d"%i,x,z,.22,.19,.18)
    mesh("Ink_EdgesAndHatching", INK_V, INK_F, GROUPS["Ink"], "Ink_Charcoal")
    for key, group in GROUPS.items():
        layer = cmds.createDisplayLayer(group, name=key+"_Layer", noRecurse=True)
        cmds.setAttr(layer+".color", 17 if key=="Snow" else 22 if key=="Ground" else 0)

    setup = cmds.group(empty=True, name="Preview_Setup_GRP")
    cam, cam_shape = cmds.camera(name="Tundra_PreviewCamera", orthographic=True)
    cmds.parent(cam, setup)
    cmds.setAttr(cam_shape+".orthographicWidth",25.5)
    cmds.setAttr(cam_shape+".backgroundColor",.37,.65,.69,type="double3")
    cmds.setAttr(cam_shape+".nearClipPlane",.01)
    cmds.setAttr(cam_shape+".farClipPlane",300)
    cmds.xform(cam,worldSpace=True,translation=(23,22,-28))
    aim = cmds.aimConstraint(root,cam,aimVector=(0,0,-1),upVector=(0,1,0),worldUpType="scene")
    cmds.delete(aim)
    cmds.xform("persp",worldSpace=True,matrix=cmds.xform(cam,query=True,worldSpace=True,matrix=True))
    cmds.setAttr("perspShape.orthographic",True)
    cmds.setAttr("perspShape.orthographicWidth",25.5)
    light = cmds.directionalLight(name="Preview_KeyLight",intensity=.93)
    light_t = cmds.listRelatives(light,parent=True)[0]
    cmds.parent(light_t,setup)
    cmds.setAttr(light_t+".rotate",-48,-32,-15,type="double3")
    cmds.setAttr(light+".useDepthMapShadows",True)
    cmds.setAttr(light+".dmapResolution",2048)
    cmds.setAttr(light+".dmapFilterSize",3)
    fill = cmds.ambientLight(name="Preview_Fill",intensity=.35)
    cmds.parent(cmds.listRelatives(fill,parent=True)[0],setup)
    cmds.setAttr("defaultResolution.width",1440)
    cmds.setAttr("defaultResolution.height",1080)
    cmds.setAttr("defaultResolution.deviceAspectRatio",4/3)
    cmds.setAttr("defaultRenderGlobals.imageFormat",32)
    cmds.setAttr("defaultRenderGlobals.currentRenderer","mayaSoftware",type="string")
    cmds.setAttr("defaultRenderQuality.edgeAntiAliasing",1)
    cmds.setAttr("defaultRenderQuality.shadingSamples",2)
    cmds.setAttr("defaultRenderQuality.maxShadingSamples",8)
    cmds.colorManagementPrefs(edit=True,cmEnabled=False)
    for shape in cmds.ls(type="camera"):
        cmds.setAttr(shape+".renderable",shape==cam_shape)
    cmds.select(root,replace=True)
    for panel in cmds.getPanel(type="modelPanel") or []:
        cmds.modelEditor(panel,edit=True,displayAppearance="smoothShaded",displayTextures=True,grid=False)
        cmds.lookThru(panel,cam)
    cmds.file(rename=os.path.join(OUT,"Tundra_Blockout_v1.ma"))
    cmds.file(save=True,type="mayaAscii",force=True)
    cmds.loadPlugin("fbxmaya",quiet=True)
    cmds.select(root,replace=True)
    mel.eval("FBXResetExport;")
    mel.eval("FBXExportSmoothingGroups -v true; FBXExportCameras -v false; FBXExportLights -v false;")
    mel.eval("FBXExportInputConnections -v false; FBXExportBakeComplexAnimation -v false;")
    mel.eval('FBXExport -f "'+os.path.join(OUT,"Tundra_Blockout_v1.fbx").replace("\\","/")+'" -s;')
    meshes = cmds.listRelatives(root,allDescendents=True,type="mesh",fullPath=True) or []
    mesh_transforms = cmds.listRelatives(meshes,parent=True,fullPath=True)
    report = {"mayaVersion":cmds.about(version=True),"units":cmds.currentUnit(query=True,linear=True),
              "bounds":cmds.exactWorldBoundingBox(root),"meshes":len(meshes),
              "triangles":cmds.polyEvaluate(mesh_transforms,triangle=True),"groups":list(GROUPS),
              "nonManifold":cmds.polyInfo(mesh_transforms,nonManifoldEdges=True) or [],
              "missingUVs":[m for m in meshes if not cmds.polyEvaluate(m,uvcoord=True)],
              "scene":os.path.join(OUT,"Tundra_Blockout_v1.ma"),
              "fbx":os.path.join(OUT,"Tundra_Blockout_v1.fbx")}
    with open(os.path.join(OUT,"validation.json"),"w",encoding="utf-8") as stream:
        json.dump(report,stream,ensure_ascii=False,indent=2)
    print("TUNDRA_SAVED "+json.dumps(report,ensure_ascii=True),flush=True)
    try:
        cmds.setAttr("defaultRenderGlobals.imageFilePrefix",os.path.join(OUT,"Tundra_Preview").replace("\\","/"),type="string")
        image = cmds.render(cam,x=1440,y=1080)
        print("TUNDRA_RENDER "+str(image),flush=True)
    except Exception as exc:
        print("TUNDRA_RENDER_FAILED "+repr(exc),flush=True)


try:
    build()
finally:
    maya.standalone.uninitialize()
