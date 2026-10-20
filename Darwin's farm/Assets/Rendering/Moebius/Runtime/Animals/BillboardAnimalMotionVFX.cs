using UnityEngine;
using UnityEngine.Rendering;

namespace Darwin.Rendering
{
    [ExecuteAlways,RequireComponent(typeof(MeshFilter),typeof(MeshRenderer))]
    public sealed class BillboardAnimalMotionVFX : MonoBehaviour
    {
        [Header("Water wake / Air stream")]
        [Range(.5f,4)] public float trailSeconds=2.2f;
        [Range(.01f,.15f)] public float lineWidth=.035f;
        [Range(0,1)] public float intensity=.8f;
        public Color ink=new Color(.42f,.64f,.65f,1);
        public Color foam=new Color(.91f,.98f,.94f,1);
        [Header("Detached swimming ripples")]
        [Range(.3f,2)] public float rippleInterval=.65f;
        [Range(.5f,4)] public float rippleLifetime=2.8f;
        [Range(.1f,1)] public float rippleExpansion=.42f;
        const int Segments=48, Ribbons=3, Rings=5;
        const int StripSize=(Segments+1)*2, Count=(Ribbons+Rings)*StripSize;
        Mesh mesh;
        readonly Vector3[] vertices=new Vector3[Count];
        readonly Color[] colors=new Color[Count];
        readonly Vector2[] uv=new Vector2[Count];
        int[] indices;
        public int LiveMeshVertices=>mesh?mesh.vertexCount:0;
        void OnEnable() => EnsureMesh();
        void EnsureMesh()
        {
            if(mesh)
            {
                if(GetComponent<MeshFilter>().sharedMesh!=mesh)GetComponent<MeshFilter>().sharedMesh=mesh;
                return;
            }
            mesh=new Mesh{name="Billboard motion strokes",hideFlags=HideFlags.DontSave};mesh.MarkDynamic();
            indices=new int[(Ribbons+Rings)*Segments*6];int k=0;
            for(int strip=0;strip<Ribbons+Rings;strip++)
                for(int j=0;j<=Segments;j++)
                {
                    int i=strip*StripSize+j*2;uv[i]=new Vector2(0,j/(float)Segments);uv[i+1]=new Vector2(1,j/(float)Segments);
                    if(j==Segments)continue;
                    indices[k++]=i;indices[k++]=i+2;indices[k++]=i+1;
                    indices[k++]=i+1;indices[k++]=i+2;indices[k++]=i+3;
                }
            mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.triangles=indices;
            GetComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=GetComponent<MeshRenderer>();renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
        public void Evaluate(BillboardAnimalMotion animal,float time,Camera camera)
        {
            EnsureMesh();transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);transform.localScale=Vector3.one;
            bool swimming=animal.medium==BillboardAnimalMotion.Medium.Swimming;
            for(int lane=0;lane<Ribbons;lane++)for(int j=0;j<=Segments;j++)
            {
                float age=j/(float)Segments, delay=age*trailSeconds;
                animal.Pose(time-delay,out Vector3 p,out Vector3 forward);
                Vector3 side=Vector3.Cross(Vector3.up,forward).normalized;
                float width=lineWidth*Mathf.Sin(Mathf.PI*Mathf.Clamp(age,.015f,.985f));
                Color color=swimming?(lane==2?ink:foam):foam;
                float alpha=Mathf.Pow(1-age,1.4f)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.1f,age))*intensity;
                if(swimming)
                {
                    p-=forward*.4f;p.y=animal.transform.position.y+.035f+lane*.002f;
                    p+=side*(lane==2?0:(lane==0?-1:1)*(.32f+age*.85f));
                    p+=side*(Mathf.Sin(age*9+time*1.4f+lane)*.035f);
                    if(lane==2)alpha*=.3f;
                }
                else
                {
                    p-=forward*.6f;
                    p+=camera.transform.up*((lane-1)*animal.spriteHeight*.12f+Mathf.Sin(age*8+time*2+lane)*.045f);
                    side=camera.transform.up;
                    alpha*=.65f+.35f*Mathf.Sin(age*4+lane);
                }
                int i=lane*StripSize+j*2;
                vertices[i]=p-side*width;vertices[i+1]=p+side*width;
                color.a=alpha;colors[i]=colors[i+1]=color;
            }
            int emission=Mathf.FloorToInt(time/Mathf.Max(.1f,rippleInterval));
            for(int ring=0;ring<Rings;ring++)
            {
                float born=(emission-ring)*rippleInterval,age=time-born,life=age/Mathf.Max(.1f,rippleLifetime);
                animal.Pose(born,out Vector3 center,out Vector3 forward);center.y=animal.transform.position.y+.041f;
                Vector3 side=Vector3.Cross(Vector3.up,forward).normalized;
                float fade=swimming&&born>=0&&life>=0&&life<1?Mathf.Sin(Mathf.PI*life)*Mathf.Pow(1-life,1.3f)*intensity:0;
                float radius=.32f+age*rippleExpansion;
                for(int j=0;j<=Segments;j++)
                {
                    float angle=j/(float)Segments*Mathf.PI*2;
                    float distortion=1+.055f*Mathf.Sin(angle*3+born)+.025f*Mathf.Sin(angle*7-born);
                    Vector3 radial=side*Mathf.Cos(angle)+forward*Mathf.Sin(angle)*.65f;
                    Vector3 p=center+radial*radius*distortion;
                    float gap=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.15f,.2f,Mathf.Sin(angle*3.0f+born*1.7f)+.3f*Mathf.Sin(angle*5-born)));
                    float width=lineWidth*(1-life*.65f)*.7f;
                    int i=(Ribbons+ring)*StripSize+j*2;
                    vertices[i]=p-radial*width;vertices[i+1]=p+radial*width;
                    var color=foam;color.a=fade*gap*.8f;colors[i]=colors[i+1]=color;
                }
            }
            mesh.vertices=vertices;mesh.colors=colors;mesh.RecalculateBounds();
        }
        void OnDisable()
        {
            if(!mesh)return;
            GetComponent<MeshFilter>().sharedMesh=null;
            if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);
            mesh=null;
        }
    }
}
