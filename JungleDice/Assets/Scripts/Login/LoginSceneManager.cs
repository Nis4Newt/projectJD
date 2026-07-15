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

            // TODO(plan-loginscene-googleauth.md): 실제 Google 로그인 자동 시도로 교체
            // 지금은 Login 씬 진입 시 로그인에 성공했다고 가정하고 즉시 발행
            EventBus.Publish(new GoogleLoginSucceeded());
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
