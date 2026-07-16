using JungleDice.Core.Table;
using UnityEditor;
using UnityEngine;

namespace JungleDice.Core.Table.Editor
{
    [CustomEditor(typeof(TableAssetBase), true)]
    internal class TableAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Reload"))
                TableGenerator.ReloadTable((TableAssetBase)target);

            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
