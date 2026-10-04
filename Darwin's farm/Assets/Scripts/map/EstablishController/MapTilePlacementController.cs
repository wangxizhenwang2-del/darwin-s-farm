using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MapTilePlacementController : MonoBehaviour
{
    [Header("场景引用")]
    [SerializeField] private Camera viewCamera;
    [SerializeField] private MapGridManager gridManager;
    [SerializeField] private MapBuildModeController buildMode;

    [Header("临时预览")]
    [SerializeField] private Material previewMaterial;

    [SerializeField]
    private Color validColor =
        new Color(0.2f, 1f, 0.3f);

    [SerializeField]
    private Color invalidColor =
        new Color(1f, 0.2f, 0.2f);

    private MapTileDefinition draggingDefinition;

    private GameObject previewObject;
    private Renderer[] previewRenderers;

    private MaterialPropertyBlock properties;

    private readonly List<RaycastResult> uiResults =
        new List<RaycastResult>();

    private Vector2Int currentCoordinate;
    private bool canPlace;

    private int rotationSteps;

    public bool IsDragging => draggingDefinition != null;

    private void Awake()
    {

        properties = new MaterialPropertyBlock();

        if (viewCamera == null)
            viewCamera = Camera.main;

        if (viewCamera == null ||
            gridManager == null ||
            buildMode == null ||
            previewMaterial == null)
        {
            Debug.LogError("地块拖拽的引用未设置完整。", this);
            enabled = false;
        }
    }

    // UI条目按下左键时调用。
    public void BeginDrag(MapTileDefinition definition)
    {
        if (!isActiveAndEnabled ||
            !buildMode.IsBuildMode ||
            definition == null)
        {
            return;
        }

        CancelDrag();

        draggingDefinition = definition;

        CreatePreview();

        // 鼠标此时还在UI上，先隐藏场景预览。
        previewObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (!IsDragging)
            return;

        if (!buildMode.IsBuildMode)
        {
            CancelDrag();
            return;
        }

        Vector2 mousePosition;
        bool released;
        bool held;
        bool cancel;
        bool rotate;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null)
        {
            CancelDrag();
            return;
        }

        mousePosition = Mouse.current.position.ReadValue();
        released = Mouse.current.leftButton.wasReleasedThisFrame;
        held = Mouse.current.leftButton.isPressed;

        cancel = Mouse.current.rightButton.wasPressedThisFrame ||
                 (Keyboard.current != null &&
                  Keyboard.current.escapeKey.wasPressedThisFrame);

        rotate = Keyboard.current != null &&
                 Keyboard.current.rKey.wasPressedThisFrame;
#else
    mousePosition = Input.mousePosition;
    released = Input.GetMouseButtonUp(0);
    held = Input.GetMouseButton(0);

    cancel = Input.GetMouseButtonDown(1) ||
             Input.GetKeyDown(KeyCode.Escape);

    rotate = Input.GetKeyDown(KeyCode.R);
#endif

        if (cancel || (!held && !released))
        {
            CancelDrag();
            return;
        }

        if (rotate)
            rotationSteps = (rotationSteps + 1) % 4;

        UpdatePreview(mousePosition);

        if (released)
        {
            if (canPlace)
            {
                gridManager.TryPlaceTile(
                    currentCoordinate,
                    draggingDefinition,
                    rotationSteps);
            }

            CancelDrag();
        }
    }

    private void UpdatePreview(Vector2 mousePosition)
    {
        canPlace = false;

        if (!viewCamera.pixelRect.Contains(mousePosition) ||
            IsPointerOverUI(mousePosition))
        {
            previewObject.SetActive(false);
            return;
        }

        // 目前所有地块的顶面位于同一高度。
        Vector3 planeOrigin =
    gridManager.GridToWorld(Vector2Int.zero) +
    Vector3.up *
    draggingDefinition.heightLevel *
    MapGridManager.HeightStep;

        Plane mapPlane = new Plane(Vector3.up, planeOrigin);

        Ray ray = viewCamera.ScreenPointToRay(mousePosition);

        if (!mapPlane.Raycast(ray, out float distance))
        {
            previewObject.SetActive(false);
            return;
        }

        Vector3 worldPosition = ray.GetPoint(distance);

        currentCoordinate = gridManager.WorldToGrid(worldPosition);
        canPlace = gridManager.CanPlaceAt(currentCoordinate);

        previewObject.transform.position =
    gridManager.GridToWorld(currentCoordinate) +
    Vector3.up *
    (draggingDefinition.heightLevel *
     MapGridManager.HeightStep + 0.06f);

        previewObject.transform.rotation =
    Quaternion.Euler(0f, rotationSteps * 90f, 0f);

        previewObject.SetActive(true);

        Color color = canPlace ? validColor : invalidColor;

        properties.Clear();
        properties.SetColor("_BaseColor", color);
        properties.SetColor("_Color", color);

        foreach (Renderer partRenderer in previewRenderers)
            partRenderer.SetPropertyBlock(properties);
    }

    private bool IsPointerOverUI(Vector2 mousePosition)
    {
        if (EventSystem.current == null)
            return false;

        PointerEventData pointer =
            new PointerEventData(EventSystem.current);

        pointer.position = mousePosition;

        uiResults.Clear();
        EventSystem.current.RaycastAll(pointer, uiResults);

        foreach (RaycastResult result in uiResults)
        {
            // 只把UI图形检测结果当作UI。
            if (result.module is GraphicRaycaster)
                return true;
        }

        return false;
    }

    private void CreatePreview()
    {
        previewObject = new GameObject("DraggingTilePreview");

        // 放在管理器下面，管理器必须位于MapGeometry外面。
        previewObject.transform.SetParent(transform, false);

        CreatePreviewPart(
            "Core",
            new Vector3(0f, -0.25f, 0f),
            new Vector3(15f, 0.5f, 15f));

        CreatePreviewPart(
            "North",
            new Vector3(0f, -0.25f, 8f),
            new Vector3(17f, 0.5f, 1f));

        CreatePreviewPart(
            "South",
            new Vector3(0f, -0.25f, -8f),
            new Vector3(17f, 0.5f, 1f));

        CreatePreviewPart(
            "East",
            new Vector3(8f, -0.25f, 0f),
            new Vector3(1f, 0.5f, 15f));

        CreatePreviewPart(
            "West",
            new Vector3(-8f, -0.25f, 0f),
            new Vector3(1f, 0.5f, 15f));

        previewRenderers =
            previewObject.GetComponentsInChildren<Renderer>();

        CreatePreviewPart(
    "DirectionMarker",
    new Vector3(0f, 0.12f, 4f),
    new Vector3(0.5f, 0.24f, 3f));//白盒阶段增加标记
    }

    private void CreatePreviewPart(
        string partName,
        Vector3 localPosition,
        Vector3 localScale)
    {
        GameObject part =
            GameObject.CreatePrimitive(PrimitiveType.Cube);

        part.name = partName;
        part.layer = LayerMask.NameToLayer("Ignore Raycast");

        part.transform.SetParent(previewObject.transform, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;

        // 预览不参与物理和导航。
        Collider partCollider = part.GetComponent<Collider>();

        partCollider.enabled = false;
        Destroy(partCollider);

        Renderer partRenderer = part.GetComponent<Renderer>();
        partRenderer.sharedMaterial = previewMaterial;
    }

    public void CancelDrag()
    {

        rotationSteps = 0;
        draggingDefinition = null;
        canPlace = false;

        if (previewObject != null)
        {
            previewObject.SetActive(false);
            Destroy(previewObject);
        }

        previewObject = null;
        previewRenderers = null;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            CancelDrag();
    }

    private void OnDisable()
    {
        CancelDrag();
    }
}