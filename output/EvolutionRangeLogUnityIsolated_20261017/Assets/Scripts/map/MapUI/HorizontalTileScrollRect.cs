using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HorizontalTileScrollRect : ScrollRect
{
    public override void OnScroll(PointerEventData eventData)
    {
        if (!IsActive() || content == null || viewport == null)
            return;

        Canvas.ForceUpdateCanvases();

        float hiddenWidth =
            content.rect.width - viewport.rect.width;

        // 内容全部能显示时，不需要滚动。
        if (hiddenWidth <= 0f)
            return;

        float wheel = eventData.scrollDelta.y;

        // 兼容能直接产生水平滚动的设备。
        if (Mathf.Approximately(wheel, 0f))
            wheel = eventData.scrollDelta.x;

        StopMovement();

        horizontalNormalizedPosition = Mathf.Clamp01(
            horizontalNormalizedPosition -
            wheel * scrollSensitivity / hiddenWidth);
    }
}