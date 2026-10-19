using UnityEngine;

// The supplied hierarchy pictogram, drawn at UI resolution without a dark bitmap backing.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SpeciesNetworkIcon : UnityEngine.UI.MaskableGraphic
{
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        Vector2 Point(float x, float y) => new Vector2(r.xMin + x * r.width, r.yMin + y * r.height);
        void Box(float x, float y, float w, float h)
        {
            int start = vh.currentVertCount;
            vh.AddVert(Point(x,y),color,Vector2.zero); vh.AddVert(Point(x+w,y),color,Vector2.zero);
            vh.AddVert(Point(x+w,y+h),color,Vector2.zero); vh.AddVert(Point(x,y+h),color,Vector2.zero);
            vh.AddTriangle(start,start+1,start+2); vh.AddTriangle(start,start+2,start+3);
        }
        void Circle(float x, float y)
        {
            int start = vh.currentVertCount;
            vh.AddVert(Point(x,y),color,Vector2.zero);
            for (int i=0;i<=32;i++)
            {
                float a = i * Mathf.PI / 16f;
                vh.AddVert(Point(x+Mathf.Cos(a)*.115f,y+Mathf.Sin(a)*.115f),color,Vector2.zero);
                if(i>0) vh.AddTriangle(start,start+i,start+i+1);
            }
        }
        Box(.16f,.51f,.68f,.065f); Box(.16f,.40f,.065f,.12f);
        Box(.775f,.40f,.065f,.12f); Box(.4675f,.40f,.065f,.24f);
        Circle(.5f,.82f); Circle(.1925f,.2f); Circle(.5f,.2f); Circle(.8075f,.2f);
    }
}
