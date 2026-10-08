using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

public class TilePaletteItemUI : MonoBehaviour,
    IPointerDownHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    [SerializeField] private Image colorPreview;
    [SerializeField] private TMP_Text nameText;

    private static TMP_FontAsset mapLabelFont;

    private MapTilePlacementController placementController;

    public MapTileDefinition Definition { get; private set; }

    public void Bind(
        MapTileDefinition definition,
        MapTilePlacementController controller)
    {
        Definition = definition;
        placementController = controller;

        if (definition == null)
            return;

        if (colorPreview != null)
            colorPreview.color = definition.mainColor;

        if (nameText != null)
        {
            if (mapLabelFont == null)
            {
                Font sourceFont = Resources.Load<Font>("Fonts/NotoSansSC-MapLabels");
                if (sourceFont != null)
                    mapLabelFont = TMP_FontAsset.CreateFontAsset(sourceFont, 48, 4,
                        GlyphRenderMode.SDFAA, 512, 512, AtlasPopulationMode.Dynamic, false);
            }

            if (mapLabelFont != null)
                nameText.font = mapLabelFont;

            nameText.text = definition.displayName;
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        if (placementController != null)
            placementController.BeginDrag(Definition);
    }

    // 条目接收拖拽事件，避免ScrollRect把它当成拖动列表。
    // 实际预览和松手处理由场景中的管理器完成。
    public void OnBeginDrag(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData) { }

    public void OnEndDrag(PointerEventData eventData) { }
}