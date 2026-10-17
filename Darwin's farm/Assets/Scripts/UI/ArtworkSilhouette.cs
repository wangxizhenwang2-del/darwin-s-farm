using UnityEngine;

// Clips the supplied opaque paper artwork at its drawn outer border, without
// changing the original image or relying on external Sprite geometry caches.
[RequireComponent(typeof(UnityEngine.UI.Image))]
public sealed class ArtworkSilhouette : UnityEngine.UI.BaseMeshEffect
{
    [SerializeField] private Vector2[] normalizedOutline;

    public void Configure(Sprite sprite, Vector2[] outline)
    {
        normalizedOutline = new Vector2[outline.Length];
        for (int i=0;i<outline.Length;i++)
            normalizedOutline[i] = new Vector2(outline[i].x * sprite.pixelsPerUnit / sprite.rect.width + .5f,
                outline[i].y * sprite.pixelsPerUnit / sprite.rect.height + .5f);
        graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(UnityEngine.UI.VertexHelper mesh)
    {
        if (!IsActive() || normalizedOutline == null || normalizedOutline.Length < 3) return;
        var image = graphic as UnityEngine.UI.Image;
        if (image == null || image.sprite == null) return;
        var rect = graphic.GetPixelAdjustedRect();
        Vector4 uv = UnityEngine.Sprites.DataUtility.GetOuterUV(image.sprite);
        void Vertex(Vector2 point)
        {
            var vertex = UnityEngine.UIVertex.simpleVert;
            vertex.position = new Vector3(rect.xMin + point.x*rect.width,rect.yMin + point.y*rect.height);
            vertex.uv0 = new Vector2(Mathf.Lerp(uv.x,uv.z,point.x),Mathf.Lerp(uv.y,uv.w,point.y));
            vertex.color = graphic.color;
            mesh.AddVert(vertex);
        }
        mesh.Clear();
        Vertex(new Vector2(.5f,.5f));
        foreach (var point in normalizedOutline) Vertex(point);
        for (int i=0;i<normalizedOutline.Length;i++) mesh.AddTriangle(0,i+1,(i+1)%normalizedOutline.Length+1);
    }
}
