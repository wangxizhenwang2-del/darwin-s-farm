using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Darwin.Rendering.Editor.Paint
{
    public enum MoebiusPaintLayer { Color, Hatching, Detail, Highlight, Shadow }
    public static class MoebiusPaintAssets
    {
        public const string Folder="Assets/Rendering/Moebius/Content/Paint";
        public const string MaterialFolder=Folder+"/Materials";
        public const string TextureFolder=Folder+"/Textures";
        public static readonly string[] LayerNames={"Color","Hatching","Detail (Hatch + Stipple)","Highlight","Shadow"};
        public static string Property(MoebiusPaintLayer layer)=>layer==MoebiusPaintLayer.Color?"_PaintColorMap":"_ControlMap";
        public static bool IsMoebius(Material material)=>material && material.shader && material.shader.name=="Darwin/Moebius NPR";
        public static int MaterialUsers(Material material)=>Resources.FindObjectsOfTypeAll<Renderer>()
            .Where(r=>!EditorUtility.IsPersistent(r) && r.gameObject.scene.IsValid())
            .Sum(r=>r.sharedMaterials.Count(m=>m==material));
        public static bool CanEdit(Material material)=>IsMoebius(material) && MaterialUsers(material)==1
            && AssetDatabase.GetAssetPath(material).StartsWith(Folder+"/",StringComparison.Ordinal);
        public static string SafeName(string name)
        { foreach(char c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');return name.Replace('/','_').Replace('\\','_'); }
        public static Material MakeUnique(Renderer renderer,int slot)
        {
            var materials=renderer.sharedMaterials;
            if(slot<0 || slot>=materials.Length || !IsMoebius(materials[slot]))throw new InvalidOperationException("Select a material slot that uses Darwin/Moebius NPR.");
            Directory.CreateDirectory(MaterialFolder);AssetDatabase.Refresh();
            string path=AssetDatabase.GenerateUniqueAssetPath(MaterialFolder+"/"+SafeName(renderer.name+"_"+materials[slot].name)+"_Paint.mat");
            var material=new Material(materials[slot]);AssetDatabase.CreateAsset(material,path);
            Undo.RecordObject(renderer,"Make Moebius paint material unique");materials[slot]=material;renderer.sharedMaterials=materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);EditorUtility.SetDirty(renderer);
            return material;
        }
        public static Texture2D WriteTexture(byte[] png,string name,bool color)
        {
            Directory.CreateDirectory(TextureFolder);
            string path=AssetDatabase.GenerateUniqueAssetPath(TextureFolder+"/"+SafeName(name)+".png");
            File.WriteAllBytes(path,png);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default;importer.textureShape=TextureImporterShape.Texture2D;
            importer.sRGBTexture=color;importer.alphaSource=TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency=false;importer.mipmapEnabled=false;importer.isReadable=false;
            importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=2048;
            importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        [InitializeOnLoadMethod]
        static void ScheduleDefaultTexture() { EditorApplication.delayCall+=EnsureDefaultTexture; }
        public static void EnsureDefaultTexture()
        {
            var shader=Shader.Find("Darwin/Moebius NPR");if(!shader || shader.FindPropertyIndex("_PaintColorMap")<0)return;
            const string path="Assets/Rendering/Moebius/Defaults/Textures/TransparentPaint.png";
            if(!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));var texture=new Texture2D(1,1,TextureFormat.RGBA32,false);
                texture.SetPixel(0,0,Color.clear);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var settings=(TextureImporter)AssetImporter.GetAtPath(path);settings.alphaIsTransparency=false;
                settings.textureCompression=TextureImporterCompression.Uncompressed;settings.mipmapEnabled=false;settings.SaveAndReimport();
            }
            var importer=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(shader)) as ShaderImporter;
            var neutral=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(importer && importer.GetDefaultTexture("_PaintColorMap")!=neutral)
            { importer.SetDefaultTextures(new[]{"_PaintColorMap"},new Texture[]{neutral});importer.SaveAndReimport(); }
        }
    }
}
