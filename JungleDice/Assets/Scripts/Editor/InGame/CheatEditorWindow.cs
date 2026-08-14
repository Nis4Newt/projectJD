using JungleDice.InGame;
using UnityEditor;
using UnityEngine;

namespace JungleDice.InGame.Editor
{
    public class CheatEditorWindow : EditorWindow
    {
        private const int SlotCount = 6;

        private readonly int[] _setKeys = new int[SlotCount];
        private readonly int[] _mergeKeys = new int[SlotCount];
        private readonly int[] _damages = new int[SlotCount];

        [MenuItem("Tools/InGame/Cheat Editor")]
        private static void Open() => GetWindow<CheatEditorWindow>("InGame Cheat");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("InGame 필드 슬롯 치트", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            bool canOperate = EditorApplication.isPlaying && InGameSceneManager.Instance != null;
            if (!canOperate)
                EditorGUILayout.HelpBox("Play 모드에서 InGame 씬에 진입한 뒤 사용할 수 있습니다.", MessageType.Info);

            using (new EditorGUI.DisabledScope(!canOperate))
            {
                for (int i = 0; i < SlotCount; i++)
                {
                    int slotIndex = i + 1;

                    EditorGUILayout.LabelField($"슬롯 {slotIndex}", EditorStyles.boldLabel);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _setKeys[i] = EditorGUILayout.IntField("Key", _setKeys[i]);
                        if (GUILayout.Button("추가", GUILayout.Width(60)))
                            InGameSceneManager.Instance.CheatSetSlot(slotIndex, _setKeys[i]);
                        if (GUILayout.Button("비우기", GUILayout.Width(60)))
                            InGameSceneManager.Instance.CheatClearSlot(slotIndex);
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _mergeKeys[i] = EditorGUILayout.IntField("머지 Key", _mergeKeys[i]);
                        if (GUILayout.Button("머지", GUILayout.Width(60)))
                            InGameSceneManager.Instance.CheatMergeIntoSlot(slotIndex, _mergeKeys[i]);
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _damages[i] = EditorGUILayout.IntField("데미지", _damages[i]);
                        if (GUILayout.Button("데미지", GUILayout.Width(60)))
                            InGameSceneManager.Instance.CheatDamageSlot(slotIndex, _damages[i]);
                    }

                    EditorGUILayout.Space();
                }
            }
        }
    }
}
