using UnityEngine;

// The source artwork has 80-pixel transparent margins. Cropping before slicing
// keeps the visible frame and the UI layout in the same rectangle.
public static class PanelParchmentArt
{
    private static Sprite cropped;

    public static Sprite CroppedPaper
    {
        get
        {
            if (cropped != null) return cropped;
            Sprite source = Resources.Load<Sprite>("GameUI/SpeciesCluePaper");
            if (source == null) return null;
            Rect bounds = source.rect;
            Rect visible = new Rect(bounds.x + 31f, bounds.y + 78f,
                bounds.width - 61f, bounds.height - 160f);
            cropped = Sprite.Create(source.texture, visible, new Vector2(.5f, .5f),
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect,
                new Vector4(55f, 55f, 55f, 55f));
            cropped.name = "PanelParchmentCropped";
            return cropped;
        }
    }
}
