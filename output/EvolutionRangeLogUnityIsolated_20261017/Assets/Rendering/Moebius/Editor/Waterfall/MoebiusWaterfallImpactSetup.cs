using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Darwin.Rendering.Editor
{
    // Creates a persistent renderer binding. All wave lifetimes and motion remain in the GPU shader.
    public sealed class MoebiusWaterfallImpactSetup : EditorWindow
    {
        [SerializeField] MeshRenderer sheet,water;
        [MenuItem("Tools/Moebius/Waterfall/Bind Impact to Water")]
        public static void Open(){GetWindow<MoebiusWaterfallImpactSetup>("Waterfall Impact").Show();}
        void OnEnable()
        {
            var selected=Selection.activeGameObject;
            if(selected)
            {
                sheet=selected.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r=>r.sharedMaterial&&r.sharedMaterial.shader.name=="Darwin/Moebius Waterfall");
                var renderer=selected.GetComponent<MeshRenderer>();
                if(renderer&&renderer.sharedMaterial&&renderer.sharedMaterial.shader.name=="Darwin/Moebius Water")water=renderer;
            }
        }
        void OnGUI()
        {
            sheet=(MeshRenderer)EditorGUILayout.ObjectField("Waterfall Sheet",sheet,typeof(MeshRenderer),true);
            water=(MeshRenderer)EditorGUILayout.ObjectField("Receiving Water",water,typeof(MeshRenderer),true);
            EditorGUILayout.HelpBox("Bind once to add automatic impact tracking to the receiving water. Endpoints follow the sheet transform and Sheet Size in Edit and Play Mode. Shared receiving materials are copied before editing. No shoreline bake is required.",MessageType.Info);
            using(new EditorGUI.DisabledScope(!sheet||!water))if(GUILayout.Button("Bind Impact"))
            {
                try{Bind(sheet,water);ShowNotification(new GUIContent("Impact bound to water"));}
                catch(Exception exception){Debug.LogError(exception.Message);}
            }
        }
        public static void Bind(MeshRenderer sheet,MeshRenderer water)
        {
            if(!sheet||!water||!sheet.sharedMaterial||sheet.sharedMaterial.shader.name!="Darwin/Moebius Waterfall"||!water.sharedMaterial||water.sharedMaterial.shader.name!="Darwin/Moebius Water")
                throw new ArgumentException("Select a Moebius Waterfall sheet and a Moebius Water surface.");
            var material=water.sharedMaterial;
            int users=Resources.FindObjectsOfTypeAll<Renderer>().Count(r=>!EditorUtility.IsPersistent(r)&&r.sharedMaterials.Contains(material));
            if(users>1)
            {
                const string folder="Assets/Rendering/Moebius/Content/Water";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
                var copy=new Material(material){name=material.name+"_WaterfallImpact"};
                string path=AssetDatabase.GenerateUniqueAssetPath(folder+"/WaterfallImpact.mat");AssetDatabase.CreateAsset(copy,path);
                Undo.RecordObject(water,"Assign unique impact water material");water.sharedMaterial=copy;material=copy;
                PrefabUtility.RecordPrefabInstancePropertyModifications(water);
            }
            Undo.RecordObject(material,"Bind waterfall impact");
            var size=sheet.sharedMaterial.GetVector("_SheetSize");
            var a=sheet.transform.TransformPoint(new Vector3(-size.x*.5f,-size.y*.5f,0));
            var b=sheet.transform.TransformPoint(new Vector3(size.x*.5f,-size.y*.5f,0));
            material.SetVector("_ImpactPointA",a);material.SetVector("_ImpactPointB",b);
            material.SetVector("_ImpactFlowDirection",material.GetVector("_FlowDirection"));
            material.SetFloat("_EnableWaterfallImpact",1);
            var binding=water.GetComponent<MoebiusWaterfallImpactBinding>();
            if(!binding)binding=Undo.AddComponent<MoebiusWaterfallImpactBinding>(water.gameObject);
            Undo.RecordObject(binding,"Track waterfall impact");
            binding.waterfallSheet=sheet;binding.enabled=true;binding.SynchronizeNow();
            EditorUtility.SetDirty(binding);PrefabUtility.RecordPrefabInstancePropertyModifications(binding);
            if(water.gameObject.scene.IsValid())UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(water.gameObject.scene);
            EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();SceneView.RepaintAll();
        }
    }
}
