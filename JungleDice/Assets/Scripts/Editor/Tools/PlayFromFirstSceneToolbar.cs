using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace JungleDice.Editor.Tools
{
    [InitializeOnLoad]
    internal static class PlayFromFirstSceneToolbar
    {
        static PlayFromFirstSceneToolbar()
        {
            EditorApplication.delayCall += TryInject;
        }

        private static void TryInject()
        {
            var toolbarType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");
            var currentToolbar = Resources.FindObjectsOfTypeAll(toolbarType).FirstOrDefault();
            if (currentToolbar == null)
            {
                EditorApplication.delayCall += TryInject;
                return;
            }

            var root = toolbarType.GetField("m_Root", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(currentToolbar) as VisualElement;
            var zone = root?.Q("ToolbarZoneRightAlign");
            if (zone == null)
            {
                Debug.LogWarning("[PlayFromFirstScene] 메인 툴바 삽입 실패 - Unity 버전 호환성 확인 필요. 메뉴/단축키는 정상 동작합니다.");
                return;
            }

            zone.Add(new EditorToolbarButton("▶1", PlayFromFirstScene.Execute)
            {
                tooltip = "첫 씬부터 재생 (Ctrl+L)"
            });
        }
    }
}
