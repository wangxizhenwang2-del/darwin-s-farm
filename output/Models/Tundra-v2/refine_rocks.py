"""Refine the saved tundra rocks without rebuilding the surrounding terrain."""
import ast
import json
import math
import os
import random

import maya.standalone
maya.standalone.initialize(name="python")
import maya.cmds as cmds
import maya.mel as mel
import maya.api.OpenMaya as om

OUT = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(os.path.dirname(OUT), "Tundra-v1")
RNG = random.Random(151027)
INK_V, INK_F = [], []
MAT = {}
GROUPS = {}

# Reuse the existing metric mesh, ink and terrain helpers without running its builder.
with open(os.path.join(SOURCE, "build_tundra.py"), encoding="utf-8") as stream:
    tree = ast.parse(stream.read())
helpers = ast.Module(body=[n for n in tree.body if isinstance(n, ast.FunctionDef)
                          and n.name in ("mesh", "rod", "ground_y", "material")], type_ignores=[])
exec(compile(helpers, "tundra_helpers", "exec"), globals())


def vec(p):
    return om.MVector(*p)


def normal(poly):
    n = om.MVector()
    for i, p in enumerate(poly):
        n += p ^ poly[(i+1) % len(poly)]
    return n.normal()


def center(poly):
    return sum(poly, om.MVector()) / len(poly)


def tidy(poly):
    result = []
    for p in poly:
        if not result or (p-result[-1]).length() > 1e-7:
            result.append(p)
    if len(result) > 1 and (result[0]-result[-1]).length() < 1e-7:
        result.pop()
    return result


def clip(polys, n, d):
    """Clip a closed convex solid, retaining the halfspace n dot p <= d."""
    result, intersections = [], []
    for poly in polys:
        clipped = []
        for i, a in enumerate(poly):
            b = poly[(i+1) % len(poly)]
            da, db = n*a-d, n*b-d
            if da <= 1e-8:
                clipped.append(a)
            if (da < -1e-8 and db > 1e-8) or (da > 1e-8 and db < -1e-8):
                hit = a + (b-a) * (da/(da-db))
                clipped.append(hit)
                intersections.append(hit)
        clipped = tidy(clipped)
        if len(clipped) >= 3 and normal(clipped).length() > .5:
            result.append(clipped)
    unique = {}
    for p in intersections:
        unique[tuple(round(c, 7) for c in p)] = p
    cap = list(unique.values())
    if len(cap) >= 3:
        mid = center(cap)
        unit = n.normal()
        ref = om.MVector(0,1,0) if abs(unit.y) < .9 else om.MVector(1,0,0)
        u = (unit ^ ref).normal()
        v = unit ^ u
        cap.sort(key=lambda p: math.atan2((p-mid)*v, (p-mid)*u))
        result.append(cap)
    return result


def topology(polys):
    verts, faces, lookup = [], [], {}
    for poly in polys:
        face = []
        for p in poly:
            key = tuple(round(c, 6) for c in p)
            if key not in lookup:
                lookup[key] = len(verts)
                verts.append(tuple(p))
            face.append(lookup[key])
        if len(set(face)) >= 3:
            faces.append(face)
    return verts, faces


def edges(polys):
    result = {}
    for poly in polys:
        n = normal(poly)
        for i, a in enumerate(poly):
            b = poly[(i+1) % len(poly)]
            key = tuple(sorted((tuple(round(c,6) for c in a), tuple(round(c,6) for c in b))))
            result.setdefault(key, [a, b, []])[2].append(n)
    return list(result.values())


def raw_boulder(seed, rx, rz, h):
    rng = random.Random(seed)
    xmin,xmax,zmin,zmax,y0,y1 = -1.14,1.14,-1.14,1.14,0,1.13
    polys = [[vec(p) for p in face] for face in [
        [(xmin,y0,zmin),(xmin,y0,zmax),(xmin,y1,zmax),(xmin,y1,zmin)],
        [(xmax,y0,zmax),(xmax,y0,zmin),(xmax,y1,zmin),(xmax,y1,zmax)],
        [(xmin,y0,zmin),(xmax,y0,zmin),(xmax,y0,zmax),(xmin,y0,zmax)],
        [(xmin,y1,zmax),(xmax,y1,zmax),(xmax,y1,zmin),(xmin,y1,zmin)],
        [(xmax,y0,zmin),(xmin,y0,zmin),(xmin,y1,zmin),(xmax,y1,zmin)],
        [(xmin,y0,zmax),(xmax,y0,zmax),(xmax,y1,zmax),(xmin,y1,zmax)],
    ]]
    phase = rng.uniform(0, math.tau)
    count = rng.choice([7,8,9])
    for i in range(count):
        angle = phase + i*math.tau/count + rng.uniform(-.12,.12)
        n = vec((math.cos(angle),rng.uniform(-.08,.38),math.sin(angle)))
        polys = clip(polys,n,rng.uniform(.91,1.08))
    # Oblique crown planes and broad, asymmetric corner breaks.
    for n,d in [((rng.uniform(-.23,.23),1,rng.uniform(-.25,.25)),.95),
                ((.48,1,-.38),1.13), ((-.43,1,.33),1.12),
                ((.72,-.55,.72),1.08), ((-.72,-.48,-.72),1.07)]:
        polys = clip(polys,vec(n),d)
    yaw = rng.uniform(-.65,.65)
    ca,sa = math.cos(yaw),math.sin(yaw)
    polys = [[vec((p.x*rx*ca-p.z*rz*sa,p.y*h,p.x*rx*sa+p.z*rz*ca)) for p in poly]
             for poly in polys]
    # Real narrow bevel faces along selected long, sharp edges.
    candidates = []
    for a,b,normals in edges(polys):
        if len(normals) == 2 and normals[0]*normals[1] < .82 and (b-a).length() > .23*h:
            n = (normals[0]+normals[1]).normal()
            if n.y > -.3:
                candidates.append(((b-a).length(),n,n*a))
    candidates.sort(key=lambda item: -item[0])
    for _,n,d in candidates[:8 if h > .6 else 4]:
        polys = clip(polys,n,d-min(rx,rz,h)*rng.uniform(.025,.055))
    return polys


def snow_cap(name, polys, height):
    vertices,faces = [],[]
    for poly in polys:
        n = normal(poly)
        if n.y < .62 or center(poly).y < height*.68:
            continue
        c = center(poly)
        top = [c+(p-c)*.86+n*.035 for p in poly]
        bottom = [p-n*.035 for p in top]
        start,count = len(vertices),len(poly)
        vertices.extend(tuple(p) for p in top+bottom)
        faces.append([start+i for i in range(count)])
        faces.append([start+count+i for i in reversed(range(count))])
        for i in range(count):
            j = (i+1)%count
            faces.append([start+i,start+count+i,start+count+j,start+j])
    if faces:
        return mesh(name,vertices,faces,GROUPS["Snow"],"Snow_Ivory")


def detailed_rock(name,x,z,rx,rz,h,cap=False,seed=1):
    polys = raw_boulder(seed,rx,rz,h)
    base_y = ground_y(x,z)-.018
    polys = [[p+vec((x,base_y,z)) for p in poly] for poly in polys]
    # Indent a broad exposed face to create real fracture relief.
    fractured=[]
    dent_used=False
    for poly in polys:
        n,c = normal(poly),center(poly)
        area = sum(((poly[i]-c) ^ (poly[(i+1)%len(poly)]-c)).length()/2 for i in range(len(poly)))
        if not dent_used and h>1 and area>.4 and abs(n.y)<.45 and n.z<-.35:
            dent = c-n*min(rx,rz)*.055
            fractured.extend([[poly[i],poly[(i+1)%len(poly)],dent] for i in range(len(poly))])
            dent_used=True
        else:
            fractured.append(poly)
    verts,faces = topology(fractured)
    obj = mesh(name,verts,faces,GROUPS["Rocks"],"Rock_Slate")
    mats={}
    broad=[]
    for index,poly in enumerate(fractured):
        n,c = normal(poly),center(poly)
        key = "Rock_Light" if n.y>.5 else "Rock_Lavender" if n.x>.25 else "Rock_Slate"
        if n.y>.1 and n.y<.5 and index%4==0:
            key="Rock_Weathered"
        mats.setdefault(key,[]).append(obj+".f[%d]"%index)
        area = sum(((poly[i]-c) ^ (poly[(i+1)%len(poly)]-c)).length()/2 for i in range(len(poly)))
        if area>.22 and abs(n.y)<.65 and (n.x>.3 or n.z<-.35):
            broad.append((area,poly,n))
    for key,components in mats.items():
        cmds.sets(components,edit=True,forceElement=MAT[key])
    if h>.30:
        for a,b,normals in edges(fractured):
            if len(normals)==2 and normals[0]*normals[1]<.89:
                offset=(normals[0]+normals[1]).normal()*.005
                rod(tuple(a+offset),tuple(b+offset),.0065 if h<1 else .008)
    broad.sort(key=lambda item:-item[0])
    for _,poly,n in broad[:2 if h>1 else 1]:
        c=center(poly)
        if h>.55:
            # Broken, branching fissure on a single planar face.
            points=[c+(poly[0]-c)*.48,c+(poly[1]-c)*.10,
                    c+(poly[len(poly)//2]-c)*.16,c+(poly[-1]-c)*.43]
            for a,b in zip(points,points[1:]):
                rod(tuple(a+n*.008),tuple(b+n*.008),.007)
            rod(tuple(points[1]+n*.008),tuple(c+(poly[2]-c)*.26+n*.008),.0045)
        if h>1.0:
            # Small localized hatch patches; avoid a complete wireframe.
            a,b=poly[0],poly[1]
            for t in (.25,.37,.49,.61,.73):
                p=a*(1-t)+b*t
                q=p*.49+c*.51
                r=p*.16+c*.84+(b-a)*.025
                rod(tuple(q+n*.009),tuple(r+n*.009),.0045)
    if cap:
        snow_cap(name+"_SnowCap",polys,base_y+h)
    return obj


def save_and_render(root):
    cmds.select(clear=True)
    cmds.file(rename=os.path.join(OUT,"Tundra_Rocks_v2.ma"))
    cmds.file(save=True,type="mayaAscii",force=True)
    cmds.loadPlugin("fbxmaya",quiet=True)
    cmds.select(root,replace=True)
    mel.eval("FBXResetExport; FBXExportSmoothingGroups -v true; FBXExportCameras -v false; FBXExportLights -v false;")
    mel.eval("FBXExportInputConnections -v false; FBXExportBakeComplexAnimation -v false;")
    mel.eval('FBXExport -f "'+os.path.join(OUT,"Tundra_Rocks_v2.fbx").replace("\\","/")+'" -s;')
    transforms=cmds.listRelatives(cmds.ls(type="mesh",long=True,noIntermediate=True),parent=True,fullPath=True)
    report={"triangles":cmds.polyEvaluate(transforms,triangle=True),"meshes":len(transforms),
            "bounds":cmds.exactWorldBoundingBox(root),
            "nonManifold":cmds.polyInfo(transforms,nonManifoldEdges=True) or []}
    with open(os.path.join(OUT,"validation.json"),"w",encoding="utf-8") as stream:
        json.dump(report,stream,indent=2)
    print("ROCKS_V2_SAVED "+json.dumps(report),flush=True)
    cmds.setAttr("defaultRenderGlobals.imageFilePrefix",os.path.join(OUT,"Tundra_Preview_v2").replace("\\","/"),type="string")
    camera=cmds.listRelatives(cmds.ls("Tundra_PreviewCameraShape*",type="camera")[0],parent=True,fullPath=True)[0]
    cmds.colorManagementPrefs(edit=True,cmEnabled=False)
    cmds.render(camera,x=1440,y=1080)


try:
    cmds.file(os.path.join(SOURCE,"Tundra_Blockout_v1.ma"),open=True,force=True,executeScriptNodes=False)
    for name in cmds.ls(materials=True):
        groups=cmds.listConnections(name+".outColor",type="shadingEngine") or []
        if groups:
            MAT[name]=groups[0]
    material("Rock_Weathered",(.65,.65,.61))
    GROUPS={key:key+"_GRP" for key in ("Ground","Rocks","Snow","Shrubs","Grass","Water","Ink")}
    old_rocks=cmds.listRelatives("Rocks_GRP",children=True,fullPath=True) or []
    old_caps=cmds.ls("Rock_*_SnowCap",type="transform") or []
    old_ink=cmds.listRelatives("Ink_GRP",children=True,fullPath=True) or []
    cmds.delete(old_rocks+old_caps+old_ink)
    # Restore the existing ground and shrub ink separately from the new rock ink.
    edge=[(-8.48,.016,-8.48),(-8.48,.016,8.48),(8.48,.016,8.48),(8.48,.016,-8.48)]
    for i in range(4):
        j=(i+1)%4
        a,b=edge[i],edge[j]
        rod(a,b,.010)
        rod((a[0],.3,a[2]),(b[0],.3,b[2]),.010)
        rod(a,(a[0],.3,a[2]),.010)
    for x,z,s in [(-5.2,4.1,1),(-3.0,3.8,.8),(4.3,4.7,.9),(-6.2,-.2,1.1),
                  (-5.4,-3.7,.9),(-3.8,-6.3,.85),(3.1,-5.4,.9),(6.1,-2.9,1),(5.8,2.5,1.05)]:
        y=ground_y(x,z)+.01
        for dx,dz in [(-.28,0),(.29,.12),(0,-.28)]:
            rod((x,y,z),(x+dx*s,y+.4*s,z+dz*s),.015)
    mesh("Ink_GroundAndShrubs",INK_V,INK_F,GROUPS["Ink"],"Ink_Charcoal")
    INK_V,INK_F=[],[]
    rocks=[(-3.8,5.5,1.2,.95,1.7,False),(-1.1,6.0,1.65,1.15,2.65,True),
           (1.9,5.4,1.45,1.08,2.0,True),(4.3,5.8,.75,.72,.9,False),
           (-5.8,2.3,.95,.72,1.1,True),(-6.3,-2.0,1.12,.86,1.45,False),
           (-4.5,-5.4,.86,.66,.75,False),(1.5,-6.1,1.0,.74,1.02,True),
           (6.2,1.3,.85,.73,.95,False),(6.2,-4.9,.7,.5,.55,False)]
    for i,data in enumerate(rocks,1):
        detailed_rock("Rock_%02d"%i,*data,seed=630+i*31)
        if data[4]>1:
            x,z,rx,rz,h,_=data
            for j,(dx,dz,size) in enumerate([(-.65,-.60,.22),(.65,-.52,.16)]):
                detailed_rock("Rock_%02d_Talus_%d"%(i,j+1),x+dx*rx,z+dz*rz,
                              rx*size,rz*size,h*.13,seed=1100+i*17+j)
    for i,(x,z) in enumerate([(-4.5,4.8),(-2.2,4.6),(3.6,4.9),(-5.1,-4.7),(1.2,-5.1),(5.5,.1)],1):
        detailed_rock("RockChip_%02d"%i,x,z,.26,.22,.23,seed=205+i)
    for i,(x,z) in enumerate([(2.5,-.95),(3.0,-2.2),(4.9,-2.1),(5.3,-1.1),(4.6,-.45)],1):
        detailed_rock("PondPebble_%02d"%i,x,z,.22,.19,.18,seed=430+i)
    mesh("Ink_RockEdgesAndFractures",INK_V,INK_F,GROUPS["Ink"],"Ink_Charcoal")
    for key in ("Rocks","Snow","Ink"):
        cmds.editDisplayLayerMembers(key+"_Layer",GROUPS[key],noRecurse=False)
    save_and_render("Tundra_17m_GRP")
finally:
    maya.standalone.uninitialize()
