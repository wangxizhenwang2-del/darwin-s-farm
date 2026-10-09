using UnityEngine;

// View: screen-sized, non-interactive labels projected above their owning tiles.
public sealed class WhiteboxEcologyDebugView : MonoBehaviour
{
    [SerializeField] private WhiteboxEcologyDebugController controller;
    [SerializeField] private Camera viewCamera;
    [SerializeField, Range(9, 18)] private int fontSize = 11;
    [SerializeField] private float heightAboveTile = 2.5f;
    private GUIStyle textStyle;
    private GUIStyle backgroundStyle;

    private void Awake()
    {
        if (controller == null) controller = GetComponent<WhiteboxEcologyDebugController>();
        if (viewCamera == null) viewCamera = Camera.main;
    }

    private void OnGUI()
    {
        if (controller == null || viewCamera == null) return;
        if (textStyle == null || textStyle.fontSize != fontSize)
        {
            textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize, alignment = TextAnchor.UpperLeft,
                normal = { textColor = Color.white }
            };
            backgroundStyle = new GUIStyle(GUI.skin.box);
        }
        GUI.Label(new Rect(12f, 8f, 90f, 22f), $"第{controller.Day}天", textStyle);

        foreach (WhiteboxEcologyDebugController.TileLabel label in controller.Labels)
        {
            if (label.tile == null || label.lines == null) continue;
            Vector3 anchor = label.tile.transform.position + Vector3.up * heightAboveTile;
            Vector3 screen = viewCamera.WorldToScreenPoint(anchor);
            if (screen.z <= 0f || screen.x < 0f || screen.x > Screen.width ||
                screen.y < 0f || screen.y > Screen.height) continue;
            float lineHeight = fontSize + 4f;
            float width = 260f;
            float height = label.lines.Length * lineHeight + 8f;
            Rect rect = new Rect(screen.x - width * 0.5f, Screen.height - screen.y - height,
                width, height);
            GUI.Box(rect, GUIContent.none, backgroundStyle);
            for (int i = 0; i < label.lines.Length; i++)
                GUI.Label(new Rect(rect.x + 5f, rect.y + 4f + i * lineHeight, width - 10f,
                    lineHeight), label.lines[i], textStyle);
        }
    }
}
