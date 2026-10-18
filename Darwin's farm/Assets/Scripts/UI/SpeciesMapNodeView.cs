using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class SpeciesMapNodeView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private SpeciesData species;
    [SerializeField] private Image portrait;
    [SerializeField] private Text label;
    private Action<SpeciesData> hovered;
    private Action<SpeciesMapNodeView> clicked;
    private bool hideLockedName;
    public SpeciesData Species => species;
    public Vector2 NodePosition => ((RectTransform)transform).anchoredPosition;

    public void Bind(Action<SpeciesData> onHover, Action<SpeciesMapNodeView> onClick, bool concealLockedName)
    {
        hovered = onHover;
        clicked = onClick;
        hideLockedName = concealLockedName;
        Refresh();
    }

    public void Refresh()
    {
        if (species == null || portrait == null || label == null) return;
        portrait.sprite = species.Portrait;
        portrait.color = species.HasEverUnlocked ? Color.white : Color.black;
        string niche = species.baseEcologicalNiche == EcologicalNiche.Water ? "水生" :
            species.baseEcologicalNiche == EcologicalNiche.Air ? "空中" : "陆生";
        label.text = hideLockedName && !species.HasEverUnlocked ? "？？？" :
            species.SpeciesName + " | L" + species.trophicLevel + " " + niche;
    }

    public void OnPointerEnter(PointerEventData eventData) { hovered?.Invoke(species); }
    public void OnPointerExit(PointerEventData eventData) { hovered?.Invoke(null); }
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        var map = GetComponentInParent<SpeciesMapsUI>();
        if (eventData.dragging || (map != null && map.SuppressNodeClicks)) return;
        if (ContainsPortrait(eventData.position, eventData.pressEventCamera)) clicked?.Invoke(this);
    }

    public bool ContainsPortrait(Vector2 screenPoint, Camera eventCamera)
    {
        if (portrait == null) return false;
        RectTransform rect = portrait.rectTransform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,
            screenPoint, eventCamera, out Vector2 point)) return false;
        Vector2 radius = rect.rect.size * .5f;
        Vector2 normalized = new Vector2(point.x / Mathf.Max(1f, radius.x),
            point.y / Mathf.Max(1f, radius.y));
        return normalized.sqrMagnitude <= 1f;
    }
}
