using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TileStatusUIInstaller
{
    [MenuItem("Tools/Darwin Farm/UI/Install Tile Status UI", false, 305)]
    public static void Install()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play mode before installing tile status UI.");
        var information = Object.FindFirstObjectByType<GameInformationUI>();
        if (information == null) throw new System.InvalidOperationException("The scene requires GameInformationUI.");
        Undo.RegisterFullObjectHierarchyUndo(information.gameObject, "Install tile status UI");
        var presenter = information.GetComponent<TileStatusPresenter>();
        if (presenter == null) presenter = Undo.AddComponent<TileStatusPresenter>(information.gameObject);
        presenter.EnsureLayout();
        foreach (Component component in information.GetComponentsInChildren<Component>(true))
            if (component != null) EditorUtility.SetDirty(component);
        EditorSceneManager.MarkSceneDirty(information.gameObject.scene);
        EditorSceneManager.SaveScene(information.gameObject.scene);
        Debug.Log("Tile status UI installed: zoom in and center a tile to show its stock and applied tools.");
    }
}
