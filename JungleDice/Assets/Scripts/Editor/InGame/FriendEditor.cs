using JungleDice.InGame;
using UnityEditor;
using UnityEngine;

namespace JungleDice.InGame.Editor
{
    [CustomEditor(typeof(Friend))]
    public class FriendEditor : UnityEditor.Editor
    {
        private int _previewKey;

        private void OnEnable()
        {
            _previewKey = ((Friend)target).Key;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            _previewKey = EditorGUILayout.IntField("Card Key", _previewKey);
            if (GUILayout.Button("Apply Key"))
            {
                var friend = (Friend)target;
                friend.SetKey(_previewKey);
                EditorUtility.SetDirty(friend);
            }
        }
    }
}
