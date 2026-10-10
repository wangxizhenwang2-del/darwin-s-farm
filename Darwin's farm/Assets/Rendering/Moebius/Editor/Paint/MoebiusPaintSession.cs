using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Darwin.Rendering.Editor.Paint
{
    // Unity Undo serializes one compressed snapshot per complete stroke, not per mouse event.
    public sealed class MoebiusPaintUndoState : ScriptableObject { public byte[] png; }

    public sealed class MoebiusPaintSession : IDisposable
    {
        public readonly MoebiusPaintMesh surface;
        public readonly Material material;
        public readonly int slot;
        public MoebiusPaintLayer layer;
        public RenderTexture Current { get; private set; }
        public bool InStroke { get; private set; }
        public bool Dirty=>!state.png.SequenceEqual(savedPng);
        public bool TargetValid=>surface.renderer && surface.renderer.GetComponent<MeshFilter>()
            && surface.renderer.GetComponent<MeshFilter>().sharedMesh==surface.mesh
            && slot<surface.renderer.sharedMaterials.Length && surface.renderer.sharedMaterials[slot]==material
            && MoebiusPaintAssets.CanEdit(material);
        public string SavedPath { get; private set; }
        readonly bool color;
        readonly string property;
        RenderTexture spare;
        readonly Material brush;
        readonly MaterialPropertyBlock original=new MaterialPropertyBlock(),preview=new MaterialPropertyBlock();
        readonly MoebiusPaintUndoState state;
        byte[] savedPng;
        bool strokeChanged;
        int undoGroup;
        bool disposed;

        public MoebiusPaintSession(MoebiusPaintMesh mesh,int materialSlot,MoebiusPaintLayer targetLayer,int resolution)
        {
            surface=mesh;slot=materialSlot;layer=targetLayer;color=layer==MoebiusPaintLayer.Color;
            if(slot<0 || slot>=mesh.renderer.sharedMaterials.Length || slot>=mesh.mesh.subMeshCount)throw new ArgumentException("The selected material slot has no matching mesh submesh.");
            material=mesh.renderer.sharedMaterials[slot];
            if(!MoebiusPaintAssets.CanEdit(material))throw new InvalidOperationException("Create a unique paint material for this object first. Shared and original materials are protected.");
            if(material.GetTextureScale("_BaseMap")!=Vector2.one || material.GetTextureOffset("_BaseMap")!=Vector2.zero)
                throw new InvalidOperationException("Painting requires Base Map Tiling = (1, 1) and Offset = (0, 0).");
            property=MoebiusPaintAssets.Property(layer);
            mesh.renderer.GetPropertyBlock(original,slot);
            var existing=new MaterialPropertyBlock();mesh.renderer.GetPropertyBlock(existing,slot);
            if(existing.isEmpty)mesh.renderer.GetPropertyBlock(existing);
            if(existing.HasTexture(Shader.PropertyToID(property)) || (color && existing.HasFloat(Shader.PropertyToID("_UsePaintColor"))))
                throw new InvalidOperationException("Another script is overriding this paint input. Remove the texture or paint-toggle override before painting.");
            var shader=Shader.Find("Hidden/Darwin/Moebius Paint Brush");
            if(!shader)throw new InvalidOperationException("The Moebius Paint Brush shader could not be found.");
            var source=material.GetTexture(property);
            int width=resolution,height=resolution;
            if(source && AssetDatabase.Contains(source) && source.width>=16 && source.height>=16)
            {width=Mathf.Min(source.width,2048);height=Mathf.Min(source.height,2048);}
            Current=NewTarget(width,height,color);spare=NewTarget(width,height,color);
            if(source && AssetDatabase.Contains(source))Blit(source,Current);
            else {var old=RenderTexture.active;RenderTexture.active=Current;GL.Clear(false,true,color?Color.clear:Color.white);RenderTexture.active=old;}
            brush=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            state=ScriptableObject.CreateInstance<MoebiusPaintUndoState>();state.hideFlags=HideFlags.HideAndDontSave;
            state.png=ReadPng();savedPng=state.png.ToArray();SavedPath=source?AssetDatabase.GetAssetPath(source):"";
            Undo.undoRedoPerformed+=OnUndo;
            RenderPipelineManager.beginCameraRendering+=BeforeCamera;
            ApplyPreview();
        }
        static RenderTexture NewTarget(int width,int height,bool color)
        {
            var target=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,color?RenderTextureReadWrite.sRGB:RenderTextureReadWrite.Linear)
            {name="Moebius working paint",hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};target.Create();return target;
        }
        static void Blit(Texture source,RenderTexture destination,Material material=null,int pass=-1)
        {
            // Graphics.Blit changes RenderTexture.active. Never leave a paint target bound for Scene Handles.
            var previous=RenderTexture.active;bool sRGB=GL.sRGBWrite;
            try{if(material)Graphics.Blit(source,destination,material,pass);else Graphics.Blit(source,destination);}
            finally{RenderTexture.active=previous;GL.sRGBWrite=sRGB;}
        }
        void BeforeCamera(ScriptableRenderContext context,Camera camera){ApplyPreview();}
        public void ApplyPreview()
        {
            if(disposed || !surface.renderer)return;
            // Merge the latest block, including a spatial-gradient controller's current anchor values.
            surface.renderer.GetPropertyBlock(preview,slot);if(preview.isEmpty)surface.renderer.GetPropertyBlock(preview);
            preview.SetTexture(property,Current);if(color)preview.SetFloat("_UsePaintColor",1);
            surface.renderer.SetPropertyBlock(preview,slot);
        }
        public void BeginStroke()
        {
            if(InStroke)return;
            if(!TargetValid)throw new InvalidOperationException("The mesh or material binding has changed, or the material is now shared. Restore the original unique binding before painting.");
            Undo.IncrementCurrentGroup();undoGroup=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Moebius paint stroke");
            Undo.RegisterCompleteObjectUndo(state,"Moebius paint stroke");InStroke=true;strokeChanged=false;
        }
        void SetBrush(float strength,float falloff,Color brushColor,float value,bool erase)
        {
            brush.SetFloat("_Channel",color?-1:(int)layer-1);brush.SetFloat("_Erase",erase?1:0);
            brush.SetFloat("_PaintValue",Mathf.Clamp01(value));brush.SetFloat("_BrushStrength",Mathf.Clamp01(strength));
            brush.SetFloat("_BrushFalloff",Mathf.Clamp(falloff,.01f,1));
            var c=QualitySettings.activeColorSpace==ColorSpace.Linear?brushColor.linear:brushColor;brush.SetVector("_BrushColor",c);
        }
        public void Dab(Vector3 center,Vector3 normal,float radius,float strength,float falloff,Color brushColor,float value,bool erase)
        {
            if(!InStroke)throw new InvalidOperationException("BeginStroke first.");
            SetBrush(strength,falloff,brushColor,value,erase);
            brush.SetVector("_BrushCenter",center);brush.SetVector("_BrushNormal",normal.normalized);brush.SetFloat("_BrushRadius",Mathf.Max(radius,.001f));
            var matrix=surface.renderer.localToWorldMatrix;brush.SetMatrix("_PaintObjectToWorld",matrix);brush.SetMatrix("_PaintWorldToObject",matrix.inverse);
            var previous=RenderTexture.active;
            Blit(Current,spare);brush.SetTexture("_MainTex",Current);
            var commands=new CommandBuffer{name="Moebius surface paint"};commands.SetRenderTarget(spare);
            commands.SetViewport(new Rect(0,0,spare.width,spare.height));commands.DisableScissorRect();
            commands.DrawMesh(surface.mesh,matrix,brush,slot,0);Graphics.ExecuteCommandBuffer(commands);commands.Release();
            RenderTexture.active=previous;
            Swap();strokeChanged=true;ApplyPreview();SceneView.RepaintAll();
        }
        void Swap(){var old=Current;Current=spare;spare=old;}
        public void EndStroke()
        {
            if(!InStroke)return;
            if(strokeChanged){state.png=ReadPng();EditorUtility.SetDirty(state);}
            InStroke=false;Undo.CollapseUndoOperations(undoGroup);
        }
        public void ClearLayer()
        {
            BeginStroke();SetBrush(1,1,Color.white,1,true);
            Blit(Current,spare,brush,1);Swap();strokeChanged=true;EndStroke();ApplyPreview();SceneView.RepaintAll();
        }
        public void Revert()
        {
            BeginStroke();Upload(savedPng);strokeChanged=true;EndStroke();ApplyPreview();SceneView.RepaintAll();
        }
        public Texture2D Save()
        {
            EndStroke();
            if(!TargetValid)throw new InvalidOperationException("Cannot save to a replaced or shared material. Restore the original unique material binding before saving.");
            var texture=MoebiusPaintAssets.WriteTexture(state.png,surface.renderer.name+"_"+material.name+(color?"_Color":"_Control"),color);
            material.SetTexture(property,texture);if(color)material.SetFloat("_UsePaintColor",1);
            EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();savedPng=state.png.ToArray();SavedPath=AssetDatabase.GetAssetPath(texture);
            ApplyPreview();return texture;
        }
        public Texture2D SaveRecovery()
        {
            EndStroke();
            // Keep unsaved work if an external material/mesh edit made normal saving unsafe.
            return MoebiusPaintAssets.WriteTexture(state.png,material.name+(color?"_Color":"_Control")+"_Recovery",color);
        }
        void OnUndo(){if(disposed || !state)return;InStroke=false;Upload(state.png);ApplyPreview();SceneView.RepaintAll();}
        void Upload(byte[] png)
        {
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,!color);texture.LoadImage(png);
            Blit(texture,Current);UnityEngine.Object.DestroyImmediate(texture);
        }
        public byte[] ReadPng()
        {
            var old=RenderTexture.active;RenderTexture.active=Current;
            var texture=new Texture2D(Current.width,Current.height,TextureFormat.RGBA32,false,!color);
            texture.ReadPixels(new Rect(0,0,Current.width,Current.height),0,0);texture.Apply();var png=texture.EncodeToPNG();
            RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(texture);return png;
        }
        public void Dispose()
        {
            if(disposed)return;EndStroke();disposed=true;
            Undo.undoRedoPerformed-=OnUndo;RenderPipelineManager.beginCameraRendering-=BeforeCamera;
            if(surface.renderer)surface.renderer.SetPropertyBlock(original.isEmpty?null:original,slot);
            Current.Release();spare.Release();UnityEngine.Object.DestroyImmediate(Current);UnityEngine.Object.DestroyImmediate(spare);
            UnityEngine.Object.DestroyImmediate(brush);Undo.ClearUndo(state);UnityEngine.Object.DestroyImmediate(state);SceneView.RepaintAll();
        }
    }
}
