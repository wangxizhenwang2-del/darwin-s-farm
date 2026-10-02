using System.Collections;
using UnityEngine;
using Unity.AI.Navigation;

[RequireComponent(typeof(NavMeshSurface))]
public class RuntimeMapNavigation : MonoBehaviour
{
    private NavMeshSurface surface;

    private bool updateRequested;
    private bool updating;

    // 点击移动脚本用它判断当前能否接受新的指令。
    public bool IsUpdating => updating;

    private void Awake()
    {
        surface = GetComponent<NavMeshSurface>();
    }

    private void Start()
    {
        // 游戏开始时同步当前地形。
        RequestUpdate();
    }

    // 正式地形更新完成后调用。
    public void RequestUpdate()
    {
        if (!isActiveAndEnabled)
        {
            Debug.LogWarning(
                "导航管理对象未启用，无法更新导航。",
                this);
            return;
        }

        updateRequested = true;

        // 已经在更新时，记下请求，完成后再处理。
        if (updating)
            return;

        updating = true;
        StartCoroutine(UpdateRoutine());
    }

    private IEnumerator UpdateRoutine()
    {
        try
        {
            while (updateRequested)
            {
                // 等待本帧的地形修改完成。
                // 同一帧的多个请求会合并处理。
                yield return null;

                updateRequested = false;

                // 同步新增地形的位置和碰撞体。
                Physics.SyncTransforms();

                if (surface.navMeshData == null)
                {
                    // 首次构建是同步操作。
                    surface.BuildNavMesh();
                }
                else
                {
                    // 已有导航数据时异步更新。
                    AsyncOperation operation =
                        surface.UpdateNavMesh(surface.navMeshData);

                    yield return operation;
                }
            }

            Debug.Log("地图导航更新完成。", this);
        }
        finally
        {
            updating = false;
        }
    }

    // 运行模式下，通过组件菜单手动测试。
    [ContextMenu("更新地图导航（运行时测试）")]
    private void UpdateFromContextMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("请在运行模式下执行这个测试。", this);
            return;
        }

        RequestUpdate();
    }
}