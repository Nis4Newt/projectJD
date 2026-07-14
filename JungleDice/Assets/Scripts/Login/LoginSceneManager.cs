using JungleDice.Core;
using JungleDice.Core.Event;

namespace JungleDice.Login
{
    public class LoginSceneManager : SceneSingleton<LoginSceneManager>
    {
        private readonly CompositeDisposable _subs = new();

        protected override void OnAwake()
        {
            _subs.Add(EventBus.Subscribe<AppFocusChanged>(OnAppFocusChanged));

            // 씬 진입 시 초기화 로직 (자동 로그인 시도 등)
            // TryAutoLogin();
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
