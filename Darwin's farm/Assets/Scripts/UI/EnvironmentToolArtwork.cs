using DarwinFarm.Environment;
using TMPro;
using UnityEngine;

// Both presentations use the same authored sprites and catalog order.
public sealed class EnvironmentToolArtwork : ScriptableObject
{
    [System.Serializable]
    public sealed class ClippedSprite
    {
        public Sprite sprite;
        public Vector2[] outline;
    }
    public Sprite toolbar, actionBackground, hintBackground, exitBadge;
    public Sprite wideToolbar, placementBackground;
    public Sprite[] temperature, elevation, humidity, plantGrowth, plantStock;
    public ClippedSprite[] clippedSprites;
    private static EnvironmentToolArtwork loaded;
    public static EnvironmentToolArtwork Current => loaded != null ? loaded :
        loaded = Resources.Load<EnvironmentToolArtwork>("GameUI/ToolArtwork");

    public Sprite Option(EnvironmentTechnologyKind kind, int index)
    {
        Sprite[] sprites = kind == EnvironmentTechnologyKind.TemperatureChange ? temperature :
            kind == EnvironmentTechnologyKind.ElevationChange ? elevation :
            kind == EnvironmentTechnologyKind.HumidityChange ? humidity :
            kind == EnvironmentTechnologyKind.PlantRecoveryChange ? plantGrowth : plantStock;
        return sprites != null && index >= 0 && index < sprites.Length ? sprites[index] : null;
    }

    public static void RefreshOption(UnityEngine.UI.Button button, EnvironmentToolSession session, int index)
    {
        bool enabled = session.OptionEnabled(index);
        button.gameObject.SetActive(enabled);
        if (!enabled) return;
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        Sprite sprite = Current != null ? Current.Option(session.Choice.Kind, index) : null;
        if (label != null) { label.text = session.OptionLabel(index); label.enabled = sprite == null; }
        var image = button.targetGraphic as UnityEngine.UI.Image;
        if (image != null && sprite != null)
        {
            image.sprite = sprite;
            image.type = UnityEngine.UI.Image.Type.Simple;
            image.preserveAspect = true;
        }
        button.targetGraphic.color = Color.white;
        var outline = button.GetComponent<UnityEngine.UI.Outline>();
        if (outline != null) outline.enabled = false;
        var marker = button.transform.Find("SelectedMarker");
        if (marker != null) marker.gameObject.SetActive(session.OptionSelected(index));
    }
}
