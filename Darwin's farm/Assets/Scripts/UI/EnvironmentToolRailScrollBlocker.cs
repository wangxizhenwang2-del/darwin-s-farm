using UnityEngine;
using UnityEngine.EventSystems;

// Wheel input over category buttons belongs to the rail, not the paper panel.
public sealed class EnvironmentToolRailScrollBlocker : MonoBehaviour, IScrollHandler
{
    public void OnScroll(PointerEventData eventData) { }
}
