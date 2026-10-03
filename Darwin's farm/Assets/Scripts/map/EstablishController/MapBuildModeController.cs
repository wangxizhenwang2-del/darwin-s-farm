using UnityEngine;
using UnityEngine.Rendering;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MapBuildModeController : MonoBehaviour
{
    [Header("地图和预览")]
    [SerializeField] private MapGridManager gridManager;
    [SerializeField] private Transform previewRoot;
    [SerializeField] private Material previewMaterial;

    [Header("建造UI")]
    [SerializeField] private GameObject buildPanel;

    [Header("玩家和相机")]
    [SerializeField] private ClickToMove clickToMove;
    [SerializeField] private ScreenEdgeCamera screenEdgeCamera;

    [Header("线框设置")]
    [SerializeField, Min(0.01f)]
    private float lineWidth = 0.12f;

    [SerializeField, Min(0.01f)]
    private float heightOffset = 0.08f;

    public bool IsBuildMode { get; private set; }

    private void Awake()
    {
        if (gridManager == null ||
            previewRoot == null ||
            previewMaterial == null ||
            buildPanel == null)
        {
            Debug.LogError("建造模式的地图、预览或UI引用未设置。", this);

            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (gridManager != null)
            gridManager.TilesChanged += HandleTilesChanged;
    }

    private void Start()
    {
        // 默认关闭建造模式。
        SetBuildMode(false);
    }

    private void Update()
    {
        bool pressedN;

#if ENABLE_INPUT_SYSTEM
        pressedN = Keyboard.current != null &&
                   Keyboard.current.nKey.wasPressedThisFrame;
#else
        pressedN = Input.GetKeyDown(KeyCode.N);
#endif

        if (pressedN)
            SetBuildMode(!IsBuildMode);
    }

    public void SetBuildMode(bool value)
    {
        IsBuildMode = value;

        if (buildPanel != null)
            buildPanel.SetActive(value);

        if (previewRoot != null)
            previewRoot.gameObject.SetActive(value);

        // 建造模式中，不接受新的点击移动指令。
        if (clickToMove != null)
            clickToMove.SetMovementInputEnabled(!value);

        // 建造模式中，暂停玩家触发的自动跟随。
        // 切换相机的建造控制与玩家跟随。
        if (screenEdgeCamera != null)
            screenEdgeCamera.SetBuildMode(value);

        if (value)
            RefreshPreview();
    }

    private void HandleTilesChanged()
    {
        if (IsBuildMode)
            RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (gridManager == null ||
            previewRoot == null ||
            previewMaterial == null)
        {
            return;
        }

        ClearPreview();

        foreach (Vector2Int coordinate
                 in gridManager.GetAvailablePositions())
        {
            CreateOutline(coordinate);
        }
    }

    private void CreateOutline(Vector2Int coordinate)
    {
        GameObject outline = new GameObject(
            $"BuildOutline_{coordinate.x}_{coordinate.y}");

        outline.transform.SetParent(previewRoot, false);

        LineRenderer line = outline.AddComponent<LineRenderer>();

        line.sharedMaterial = previewMaterial;
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = 4;

        line.startColor = Color.white;
        line.endColor = Color.white;

        line.startWidth = lineWidth;
        line.endWidth = lineWidth;

        line.numCornerVertices = 3;

        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;

        float halfSize = MapGridManager.TileSize * 0.5f;

        Vector3 center = gridManager.GridToWorld(coordinate);
        center.y += heightOffset;

        line.SetPositions(new Vector3[]
        {
            center + new Vector3(-halfSize, 0f, -halfSize),
            center + new Vector3(-halfSize, 0f,  halfSize),
            center + new Vector3( halfSize, 0f,  halfSize),
            center + new Vector3( halfSize, 0f, -halfSize)
        });
    }

    private void ClearPreview()
    {
        for (int i = previewRoot.childCount - 1; i >= 0; i--)
        {
            GameObject child = previewRoot.GetChild(i).gameObject;

            // Destroy会在帧末执行，先隐藏避免短暂重叠。
            child.SetActive(false);
            Destroy(child);
        }
    }

    private void OnDisable()
    {
        if (gridManager != null)
            gridManager.TilesChanged -= HandleTilesChanged;

        SetBuildMode(false);
    }
}