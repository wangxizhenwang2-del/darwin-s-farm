using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Darwin.Rendering.Editor.Paint
{
    // Editor-only BVH. Reads imported meshes without requiring a MeshCollider or changing Read/Write settings.
    public sealed class MoebiusPaintMesh
    {
        // Built-in Unity meshes can overshoot the UV border by around 0.001.
        // Normalize only the editor's copied coordinates, never the source mesh.
        const float UVBorderTolerance=.002f;
        public struct Hit { public Vector3 point, normal; public Vector2 uv; public int slot; public float distance; }
        struct Triangle { public int a,b,c,slot; public Bounds bounds; public Vector3 center; }
        sealed class Node { public Bounds bounds; public int start,count; public Node left,right; }
        readonly Vector3[] vertices,normals;
        readonly Vector2[] uv;
        readonly Triangle[] triangles;
        readonly int[] order;
        readonly Node root;
        public readonly Mesh mesh;
        public readonly MeshRenderer renderer;
        public string Warning { get; private set; }

        public MoebiusPaintMesh(MeshRenderer target)
        {
            renderer=target;
            var filter=target.GetComponent<MeshFilter>();
            if(!filter || !filter.sharedMesh) throw new ArgumentException("Select a static mesh with a MeshFilter and MeshRenderer. Skinned meshes are not supported.");
            mesh=filter.sharedMesh;
            using(var array=MeshUtility.AcquireReadOnlyMeshData(mesh))
            {
                var data=array[0];
                if(!data.HasVertexAttribute(VertexAttribute.TexCoord0)) throw new ArgumentException("This mesh has no UV0. Unwrap it in your modeling software before painting. The tool does not modify UVs.");
                if(!data.HasVertexAttribute(VertexAttribute.Normal)) throw new ArgumentException("This mesh has no normals. Import or calculate normals in the model import settings before painting.");
                using(var v=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp)) { data.GetVertices(v);vertices=v.ToArray(); }
                using(var u=new NativeArray<Vector2>(data.vertexCount,Allocator.Temp)) { data.GetUVs(0,u);uv=u.ToArray(); }
                for(int i=0;i<uv.Length;i++)
                {
                    var value=uv[i];
                    if(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.x) || float.IsInfinity(value.y))
                        throw new ArgumentException("UV0 contains non-finite coordinates. Repair the UV layout before painting.");
                    if(value.x < -UVBorderTolerance || value.y < -UVBorderTolerance || value.x > 1+UVBorderTolerance || value.y > 1+UVBorderTolerance)
                        throw new ArgumentException("UV0 must be within 0-1; minor edge imprecision is allowed. This mesh exceeds that range. Use a unique, untiled UV layout for painting.");
                    uv[i]=new Vector2(Mathf.Clamp01(value.x),Mathf.Clamp01(value.y));
                }
                if(data.HasVertexAttribute(VertexAttribute.Normal))
                    using(var n=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp)) { data.GetNormals(n);normals=n.ToArray(); }
                var list=new List<Triangle>();
                for(int slot=0;slot<data.subMeshCount;slot++)
                {
                    var sub=data.GetSubMesh(slot);
                    if(sub.topology!=MeshTopology.Triangles) continue;
                    using(var indices=new NativeArray<int>(sub.indexCount,Allocator.Temp))
                    {
                        data.GetIndices(indices,slot,true);
                        for(int i=0;i<indices.Length;i+=3)
                        {
                            int a=indices[i],b=indices[i+1],c=indices[i+2];
                            if(Mathf.Abs(Cross(uv[b]-uv[a],uv[c]-uv[a]))<.00000001f) continue;
                            var bounds=new Bounds(vertices[a],Vector3.zero);bounds.Encapsulate(vertices[b]);bounds.Encapsulate(vertices[c]);
                            bounds.Expand(.00001f);
                            list.Add(new Triangle{a=a,b=b,c=c,slot=slot,bounds=bounds,center=bounds.center});
                        }
                    }
                }
                triangles=list.ToArray();
            }
            if(triangles.Length==0) throw new ArgumentException("This mesh has no UV triangles with usable area.");
            Warning="Overlapping UVs share paint. Automatic UV unwrapping and seam padding are not supported.";
            order=new int[triangles.Length];for(int i=0;i<order.Length;i++)order[i]=i;
            root=Build(0,order.Length);
        }
        static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        Node Build(int start,int count)
        {
            var node=new Node{start=start,count=count,bounds=triangles[order[start]].bounds};
            for(int i=start+1;i<start+count;i++)node.bounds.Encapsulate(triangles[order[i]].bounds);
            if(count<=12)return node;
            var size=node.bounds.size;int axis=size.x>size.y?(size.x>size.z?0:2):(size.y>size.z?1:2);
            Array.Sort(order,start,count,Comparer<int>.Create((a,b)=>triangles[a].center[axis].CompareTo(triangles[b].center[axis])));
            int half=count/2;node.left=Build(start,half);node.right=Build(start+half,count-half);return node;
        }
        public bool Raycast(Ray worldRay,out Hit hit)
        {
            hit=default;
            var matrix=renderer.localToWorldMatrix;var inverse=matrix.inverse;
            var ray=new Ray(inverse.MultiplyPoint(worldRay.origin),inverse.MultiplyVector(worldRay.direction).normalized);
            float best=float.PositiveInfinity;int found=-1;Vector3 bary=default;
            Search(root,ray,ref best,ref found,ref bary);
            if(found<0)return false;
            var tri=triangles[found];
            var localPoint=vertices[tri.a]*bary.x+vertices[tri.b]*bary.y+vertices[tri.c]*bary.z;
            var localNormal=normals!=null ? normals[tri.a]*bary.x+normals[tri.b]*bary.y+normals[tri.c]*bary.z
                : Vector3.Cross(vertices[tri.b]-vertices[tri.a],vertices[tri.c]-vertices[tri.a]);
            hit.point=matrix.MultiplyPoint(localPoint);hit.normal=inverse.transpose.MultiplyVector(localNormal).normalized;
            hit.uv=uv[tri.a]*bary.x+uv[tri.b]*bary.y+uv[tri.c]*bary.z;
            hit.slot=tri.slot;hit.distance=Vector3.Distance(worldRay.origin,hit.point);return true;
        }
        void Search(Node node,Ray ray,ref float best,ref int found,ref Vector3 bary)
        {
            if(!node.bounds.IntersectRay(ray,out float near) || near>best)return;
            if(node.left!=null){Search(node.left,ray,ref best,ref found,ref bary);Search(node.right,ray,ref best,ref found,ref bary);return;}
            for(int i=node.start;i<node.start+node.count;i++)
            {
                int index=order[i];var tri=triangles[index];
                var e1=vertices[tri.b]-vertices[tri.a];var e2=vertices[tri.c]-vertices[tri.a];
                var p=Vector3.Cross(ray.direction,e2);float determinant=Vector3.Dot(e1,p);
                if(Mathf.Abs(determinant)<1e-8f)continue;
                var t=ray.origin-vertices[tri.a];float u=Vector3.Dot(t,p)/determinant;
                if(u<0 || u>1)continue;
                var q=Vector3.Cross(t,e1);float v=Vector3.Dot(ray.direction,q)/determinant;
                if(v<0 || u+v>1)continue;
                float distance=Vector3.Dot(e2,q)/determinant;
                if(distance<0 || distance>=best)continue;
                best=distance;found=index;bary=new Vector3(1-u-v,u,v);
            }
        }
    }
}
