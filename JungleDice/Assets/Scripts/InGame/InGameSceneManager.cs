using JungleDice.Core;
using JungleDice.Core.Event;

namespace JungleDice.InGame
{
    public class InGameSceneManager : SceneSingleton<InGameSceneManager>
    {
        private readonly CompositeDisposable _subs = new();

        protected override void OnAwake()
        {
            _subs.Add(EventBus.Subscribe<GameStateChanged>(OnGameStateChanged));
        }

        private void OnGameStateChanged(GameStateChanged e)
        {
            // InGame ↔ Pause는 SceneLoader의 _stateSceneMap에 없어 씬 전환이 일어나지 않음
            // → 오버레이 표시/숨김은 이 씬 매니저가 전담
            if (e.Next == GameState.Pause)
            {
                // ShowPauseOverlay();
            }
            else if (e.Previous == GameState.Pause && e.Next == GameState.InGame)
            {
                // HidePauseOverlay();
            }
        }

        protected override void OnDestroy()
        {
            _subs.Dispose();
            base.OnDestroy();
        }
    }
}
