using UnityEngine;
using UnityEngine.UI;

public class TilePaletteUI : MonoBehaviour
{
    [Header("UI引用")]
    [SerializeField] private RectTransform content;
    [SerializeField] private TilePaletteItemUI itemPrefab;
    [SerializeField] private ScrollRect scrollRect;

    [Header("拖拽管理")]
    [SerializeField]
    private MapTilePlacementController placementController;

    [Header("可选择的地块")]
    [SerializeField] private MapTileDefinition[] definitions;

    private void Start()
    {
        if (content == null ||
            itemPrefab == null ||
            scrollRect == null ||
            placementController == null)
        {
            Debug.LogError("地块列表的UI或拖拽引用未设置。", this);
            return;
        }

        if (definitions != null)
        {
            foreach (MapTileDefinition definition in definitions)
            {
                if (definition == null)
                    continue;

                TilePaletteItemUI item =
                    Instantiate(itemPrefab, content);

                item.Bind(definition, placementController);
            }
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        scrollRect.StopMovement();
        scrollRect.horizontalNormalizedPosition = 0f;
    }
}