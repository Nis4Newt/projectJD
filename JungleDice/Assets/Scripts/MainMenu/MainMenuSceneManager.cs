using JungleDice.Core;
using JungleDice.Core.Event;

namespace JungleDice.MainMenu
{
    public class MainMenuSceneManager : SceneSingleton<MainMenuSceneManager>
    {
        private readonly CompositeDisposable _subs = new();

        protected override void OnAwake()
        {
            // 씬 진입 시 초기화 로직 (배너/공지 갱신 등)
            // RefreshBanner();
        }

        protected override void OnDestroy()
        {
            _subs.Dispose();
            base.OnDestroy();
        }
    }
}
