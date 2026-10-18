using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class SpeciesMapNodeView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private SpeciesData species;
    [SerializeField] private Image portrait;
    [SerializeField] private Text label;
    private Action<SpeciesData> hovered;
    public SpeciesData Species => species;
    public Vector2 NodePosition => ((RectTransform)transform).anchoredPosition;

    public void Bind(Action<SpeciesData> onHover)
    {
        hovered = onHover;
        Refresh();
    }

    public void Refresh()
    {
        if (species == null || portrait == null || label == null) return;
        portrait.sprite = species.Portrait;
        portrait.color = species.HasEverUnlocked ? Color.white : Color.black;
        string niche = species.baseEcologicalNiche == EcologicalNiche.Water ? "水生" :
            species.baseEcologicalNiche == EcologicalNiche.Air ? "空中" : "陆生";
        label.text = species.SpeciesName + " | L" + species.trophicLevel + " " + niche;
    }

    public void OnPointerEnter(PointerEventData eventData) { hovered?.Invoke(species); }
    public void OnPointerExit(PointerEventData eventData) { hovered?.Invoke(null); }
}
