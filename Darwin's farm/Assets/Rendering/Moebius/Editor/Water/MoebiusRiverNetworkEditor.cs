using UnityEditor;
using UnityEngine;

namespace Darwin.Rendering.Editor
{
    [CustomEditor(typeof(MoebiusRiverNetwork))]
    public sealed class MoebiusRiverNetworkEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var network = (MoebiusRiverNetwork)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Place River Tile wrappers under this Network at 17 metre centre spacing. Match their mouth arrows and keep wrapper scale at 1. Move / rotate wrappers or change Water Level to update connections in Edit and Play Mode. A tile without a profile is land. Unmatched mouths close with rounded banks.", MessageType.Info);
            EditorGUILayout.LabelField("Joined Edges", network.JoinedCount.ToString());
            EditorGUILayout.LabelField("Rounded Ends", network.ClosedCount.ToString());
            EditorGUILayout.LabelField("Waterfalls", network.WaterfallCount.ToString());
            if (!string.IsNullOrEmpty(network.LastError)) EditorGUILayout.HelpBox(network.LastError, MessageType.Error);
            if (GUILayout.Button("Rebuild Connections")) { network.RebuildNow(); SceneView.RepaintAll(); }
        }
    }
}
