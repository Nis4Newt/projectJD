using System.Collections;
using UnityEngine;
using JungleDice.Core;
using JungleDice.Core.Event;

namespace JungleDice.Login
{
    public class LoginSceneManager : SceneSingleton<LoginSceneManager>
    {
        private static readonly LoginTask[] _tasks =
        {
            new("설정 로드", () => PlaceholderTask(0.3f)),
            new("유저 데이터 로드", () => PlaceholderTask(0.5f)),
            new("서버 시간 동기화", () => PlaceholderTask(0.3f)),
        };

        private readonly CompositeDisposable _subs = new();

        protected override void OnAwake()
        {
            _subs.Add(EventBus.Subscribe<AppFocusChanged>(OnAppFocusChanged));
            StartCoroutine(TaskSequenceRoutine());
        }

        private IEnumerator TaskSequenceRoutine()
        {
            for (int i = 0; i < _tasks.Length; i++)
            {
                yield return StartCoroutine(_tasks[i].Run());
                EventBus.Publish(new LoginProgressChanged(i + 1, _tasks.Length, _tasks[i].Name));
            }

            Debug.Log("[LoginSceneManager] task 시퀀스 완료");

            // TODO(plan-loginscene-googleauth.md): 실제 Google 로그인 자동 시도로 교체
            EventBus.Publish(new GoogleLoginSucceeded());
        }

        private static IEnumerator PlaceholderTask(float duration)
        {
            yield return new WaitForSeconds(duration);
        }

        private void OnAppFocusChanged(AppFocusChanged e)
        {
            // 포커스 복귀 시 로그인 화면 갱신 등
        }

        protected override void OnDestroy()
        {
            _subs.Dispose();
            base.OnDestroy();
        }
    }
}
