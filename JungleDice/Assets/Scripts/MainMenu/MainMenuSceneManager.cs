using JungleDice.Core;
using JungleDice.Core.Event;
using JungleDice.Core.Settings;
using JungleDice.Core.UI;
using JungleDice.Core.User;
using JungleDice.InGame;
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
        [SerializeField] private Friend[] _deckCards; // 모험탭 덱 미리보기 3개, UserData.Friends 인덱스와 1:1

        private OptionPanel _optionPanel;
        private readonly CompositeDisposable _subs = new();
        private bool _hasRequestedPlay;

        protected override void OnAwake()
        {
            _optionPanel = UIManager.Load<OptionPanel>(_canvasTransform, p => p.Configure(OptionPanelMode.MainMenu));

            _soloButton.onClick.AddListener(() => OnPlayButtonClicked(GameType.Solo));
            _battleButton.onClick.AddListener(() => OnPlayButtonClicked(GameType.Battle));
            _optionButton.onClick.AddListener(_optionPanel.Show);

            RefreshDeckCards();
            _subs.Add(EventBus.Subscribe<UserDataChanged>(_ => RefreshDeckCards()));
        }

        private void RefreshDeckCards()
        {
            var friends = UserManager.Current.Friends; // 항상 3개(UserData 기본값이자 SetFriends 호출부의 불변 조건)
            for (int i = 0; i < _deckCards.Length; i++)
                _deckCards[i].SetKey(friends[i]);
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
