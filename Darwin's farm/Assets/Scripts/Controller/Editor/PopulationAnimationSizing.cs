using UnityEditor;
using UnityEngine;

// Called by the existing asset builders. No texture reimport or Read/Write setting is needed.
public static class PopulationAnimationSizing
{
    public static float Bake(PopulationAnimationSet set)
    {
        if (set == null || set.IdleSprite == null) return 0f;
        Sprite sprite = set.IdleSprite;
        Texture2D source = sprite.texture;
        RenderTexture previous = RenderTexture.active;
        RenderTexture target = RenderTexture.GetTemporary(source.width, source.height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Texture2D readable = null;
        try
        {
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply();
            Color32[] pixels = readable.GetPixels32();
            Rect rect = sprite.rect;
            int bottom = int.MaxValue, top = -1;
            for (int y = Mathf.FloorToInt(rect.yMin); y < Mathf.CeilToInt(rect.yMax); y++)
                for (int x = Mathf.FloorToInt(rect.xMin); x < Mathf.CeilToInt(rect.xMax); x++)
                    if (pixels[y * source.width + x].a >= 26)
                    { bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y); }
            float height = top >= bottom ? (top - bottom + 1) / sprite.pixelsPerUnit : sprite.bounds.size.y;
            var data = new SerializedObject(set);
            data.FindProperty("referenceVisualHeight").floatValue = Mathf.Max(.01f, height);
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(set);
            return height;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            if (readable != null) Object.DestroyImmediate(readable);
        }
    }
}
