using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JungleDice.Editor.Tools
{
    public static class PlayFromFirstScene
    {
        static PlayFromFirstScene()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Tools/Play %l")]
        public static void Execute()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[PlayFromFirstScene] 이미 Play 중입니다.");
                return;
            }

            var sceneAsset = GetFirstEnabledSceneAsset();
            if (sceneAsset == null)
            {
                Debug.LogWarning("[PlayFromFirstScene] Build Settings에 활성화된 씬이 없습니다.");
                return;
            }

            EditorSceneManager.playModeStartScene = sceneAsset;
            EditorApplication.EnterPlaymode();
        }

        private static SceneAsset GetFirstEnabledSceneAsset()
        {
            var entry = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled);
            return entry != null ? AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.path) : null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorSceneManager.playModeStartScene = null;
        }
    }
}
