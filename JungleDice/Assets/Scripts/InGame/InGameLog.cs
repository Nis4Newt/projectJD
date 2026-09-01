using System;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace JungleDice.InGame
{
    public static class InGameLog
    {
        private const string EditorPrefsKey = "JungleDice.InGameLog.Enabled";
        private static bool _enabled = true;

#if UNITY_EDITOR
        static InGameLog()
        {
            _enabled = UnityEditor.EditorPrefs.GetBool(EditorPrefsKey, true);
        }

        public static bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                UnityEditor.EditorPrefs.SetBool(EditorPrefsKey, value);
            }
        }
#endif

        // message는 Func<string>로 지연 평가한다 — _enabled가 false면 messageFactory()를 아예 호출하지 않아,
        // 문자열 보간/조인 비용(예: string.Join으로 덱 전체를 나열하는 호출)이 토글 off일 때 발생하지 않는다.
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Log(Func<string> messageFactory,
            [CallerMemberName] string caller = "", [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            if (!_enabled) return;
            Debug.Log($"[InGame] ({Path.GetFileNameWithoutExtension(file)}.{caller}:{line}) {messageFactory()}");
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Warning(Func<string> messageFactory,
            [CallerMemberName] string caller = "", [CallerLineNumber] int line = 0, [CallerFilePath] string file = "")
        {
            if (!_enabled) return;
            Debug.LogWarning($"[InGame] ({Path.GetFileNameWithoutExtension(file)}.{caller}:{line}) {messageFactory()}");
        }
    }
}
