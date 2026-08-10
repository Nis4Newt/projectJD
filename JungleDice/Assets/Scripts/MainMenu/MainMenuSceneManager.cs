using JungleDice.Core;
using JungleDice.Core.Event;
using JungleDice.Core.Settings;
using JungleDice.Core.UI;
using UnityEngine;
using UnityEngine.UI;

namespace JungleDice.MainMenu
{
    public class MainMenuSceneManager : SceneSingleton<MainMenuSceneManager>
    {
        [SerializeField] private Button _soloButton;
        [SerializeField] private Button _battleButton;
        [SerializeField] private Button _optionButton;
        [SerializeField] private Transform _canvasTransform;

        private OptionPanel _optionPanel;
        private readonly CompositeDisposable _subs = new();
        private bool _hasRequestedPlay;

        protected override void OnAwake()
        {
            _optionPanel = UIManager.Load<OptionPanel>(_canvasTransform, p => p.Configure(OptionPanelMode.MainMenu));

            _soloButton.onClick.AddListener(() => OnPlayButtonClicked(GameType.Solo));
            _battleButton.onClick.AddListener(() => OnPlayButtonClicked(GameType.Battle));
            _optionButton.onClick.AddListener(_optionPanel.Show);
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
