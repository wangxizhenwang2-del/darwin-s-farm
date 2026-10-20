using UnityEngine;

// References the original font asset so it is included in builds without duplicating the TTF.
public sealed class GameUITheme : ScriptableObject
{
    [SerializeField] private Font font;
    [SerializeField] private TMPro.TMP_FontAsset textMeshProFont;
    [SerializeField] private Font handwrittenFont;
    private static GameUITheme loaded;
    private static TMPro.TMP_FontAsset tmpFont;
    private static TMPro.TMP_FontAsset handwrittenTMPFont;
    // Keep the existing title/body hierarchy with a modest increase for small copy.
    public static int ReadableFontSize(float size) =>
        size >= 48f ? Mathf.RoundToInt(size) : Mathf.Max(20, Mathf.RoundToInt(size * 1.14f));
    public static TMPro.TMP_FontAsset TMPFont
    {
        get
        {
            if (loaded == null) loaded = Resources.Load<GameUITheme>("GameUI/Theme");
            if (loaded != null && loaded.textMeshProFont != null) return loaded.textMeshProFont;
            if (tmpFont == null)
            {
                tmpFont = TMPro.TMP_FontAsset.CreateFontAsset(Font, 48, 4,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
                    TMPro.AtlasPopulationMode.Dynamic, true);
                tmpFont.name = "乐米小奶泡体 UI SDF";
            }
            return tmpFont;
        }
    }
    public static Font Font
    {
        get
        {
            if (loaded == null) loaded = Resources.Load<GameUITheme>("GameUI/Theme");
            return loaded != null && loaded.font != null ? loaded.font :
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }

    public static TMPro.TMP_FontAsset HandwrittenTMPFont
    {
        get
        {
            if (loaded == null) loaded = Resources.Load<GameUITheme>("GameUI/Theme");
            if (loaded == null || loaded.handwrittenFont == null) return TMPFont;
            if (handwrittenTMPFont == null)
            {
                handwrittenTMPFont = TMPro.TMP_FontAsset.CreateFontAsset(loaded.handwrittenFont, 48, 4,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
                    TMPro.AtlasPopulationMode.Dynamic, true);
                handwrittenTMPFont.name = "乐米拾光手札体 UI SDF";
            }
            return handwrittenTMPFont;
        }
    }
}
