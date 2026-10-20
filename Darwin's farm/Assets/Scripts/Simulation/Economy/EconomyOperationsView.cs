using UnityEngine;

// Keeps the existing game-over prompt while the legacy operations panel is retired.
public sealed class EconomyOperationsView : MonoBehaviour
{
    private PlayerEconomyController operations;
    private static EconomyOperationsView active;
    private Rect lossRect;

    private void Awake() => operations = GetComponent<PlayerEconomyController>();
    private void OnEnable() => active = this;
    private void OnDisable()
    {
        if (active == this) active = null;
    }

    public static bool IsPointerOverUi(Vector2 screenPosition)
    {
        if (active == null || active.operations == null || !active.operations.IsGameLost)
            return false;
        Vector2 gui = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        return active.lossRect.Contains(gui);
    }

    private void OnGUI()
    {
        if (operations == null || !operations.IsGameLost) return;
        lossRect = new Rect(Screen.width * .5f - 160, Screen.height * .5f - 45, 320, 90);
        GUI.Box(lossRect, "所有野外动物灭绝，且没有生态仓备份");
        if (GUI.Button(new Rect(lossRect.x + 70, lossRect.y + 48, 180, 27), "重新开局"))
            operations.RestartGame();
    }
}
