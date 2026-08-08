using JungleDice.Core;
using JungleDice.Core.Event;
using UnityEngine;
using UnityEngine.UI;

namespace JungleDice.MainMenu
{
    public class MainMenuSceneManager : SceneSingleton<MainMenuSceneManager>
    {
        [SerializeField] private Button _soloButton;
        [SerializeField] private Button _battleButton;

        private readonly CompositeDisposable _subs = new();
        private bool _hasRequestedPlay;

        protected override void OnAwake()
        {
            _soloButton.onClick.AddListener(() => OnPlayButtonClicked(GameType.Solo));
            _battleButton.onClick.AddListener(() => OnPlayButtonClicked(GameType.Battle));
        }

        private void OnPlayButtonClicked(GameType type)
        {
            if (_hasRequestedPlay) return;
            _hasRequestedPlay = true;

            _soloButton.interactable = false;
            _battleButton.interactable = false;

            GameSession.SetGameType(type);
            EventBus.Publish(new MainMenuPlayRequested());
        }

        protected override void OnDestroy()
        {
            _subs.Dispose();
            base.OnDestroy();
        }
    }
}
