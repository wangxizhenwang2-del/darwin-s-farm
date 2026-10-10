"""Run with Maya mayapy; export supplied static scene meshes, material colors and import diagnostics."""
import os, json, traceback
import maya.standalone
maya.standalone.initialize(name='python')
import maya.cmds as cmds
import maya.mel as mel

root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
source = os.path.join(root, 'Temp', 'BiomeImportSources')
output = os.path.join(root, 'Assets', 'Art', 'ImportedBiomes', 'Models')
os.makedirs(output, exist_ok=True)
results = []
try:
    cmds.loadPlugin('fbxmaya', quiet=True)
    for name in ['Grassland', 'Desert', 'Forest', 'RainForest']:
        cmds.file(new=True, force=True)
        # Ignore embedded script nodes from third-party scenes.
        cmds.file(os.path.join(source, name+'.mb'), open=True, force=True, executeScriptNodes=False)
        meshes = [m for m in cmds.ls(type='mesh', long=True) or [] if not cmds.getAttr(m+'.intermediateObject')]
        transforms = list(set(cmds.listRelatives(meshes, parent=True, fullPath=True) or []))
        materials = []
        for material in cmds.ls(materials=True) or []:
            color = [0.65, 0.65, 0.65]
            for attribute in ['baseColor', 'color', 'diffuseColor']:
                if cmds.attributeQuery(attribute, node=material, exists=True):
                    try:
                        value = cmds.getAttr(material+'.'+attribute)
                    except RuntimeError:
                        continue
                    if value and isinstance(value, (tuple, list)) and isinstance(value[0], (tuple,list)) and len(value[0]) == 3:
                        color = list(value[0])
                        break
            materials.append({'name':material, 'rgb':color, 'type':cmds.nodeType(material)})
        textures = [{'node':n,'path':cmds.getAttr(n+'.fileTextureName')} for n in cmds.ls(type='file') or []]
        triangles = cmds.polyEvaluate(transforms, triangle=True) if transforms else 0
        bounds = cmds.exactWorldBoundingBox(transforms) if transforms else []
        if not transforms:
            raise RuntimeError(name+' has no mesh objects')
        cmds.select(transforms, replace=True)
        mel.eval('FBXResetExport;')
        mel.eval('FBXExportSmoothingGroups -v true;')
        mel.eval('FBXExportTriangulate -v true;')
        mel.eval('FBXExportCameras -v false; FBXExportLights -v false;')
        mel.eval('FBXExportAnimationOnly -v false; FBXExportBakeComplexAnimation -v false;')
        mel.eval('FBXExportSkins -v false; FBXExportShapes -v false;')
        mel.eval('FBXExportInputConnections -v false; FBXExportEmbeddedTextures -v false;')
        fbx = os.path.join(output, name+'.fbx').replace('\\','/')
        mel.eval('FBXExport -f "'+fbx+'" -s;')
        result = {'name':name,'meshes':len(meshes),'triangles':triangles,
                  'sourceUnit':cmds.currentUnit(query=True,linear=True),'bounds':bounds,
                  'materials':materials,'textures':textures}
        results.append(result)
        print('EXPORTED '+name+' '+str(triangles)+' triangles', flush=True)
    with open(os.path.join(output,'SourceMaterials.json'),'w',encoding='utf-8') as f:
        json.dump({'models':results},f,ensure_ascii=False,indent=2)
except Exception:
    traceback.print_exc()
    raise
finally:
    maya.standalone.uninitialize()
