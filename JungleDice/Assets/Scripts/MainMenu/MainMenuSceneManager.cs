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
        [SerializeField] private Button _friendButton;
        [SerializeField] private Button _storeButton;
        [SerializeField] private Button _panelCloseButton; // FriendPanel/StorePanel 공용 닫기 버튼 — 열려있는 패널을 전부 닫음
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
            _friendButton.onClick.AddListener(() => UIManager.Show<FriendPanel>(_canvasTransform));
            _storeButton.onClick.AddListener(() => UIManager.Show<StorePanel>(_canvasTransform));
            _panelCloseButton.onClick.AddListener(UIManager.HideAll);

            UpdatePanelToggleButtons(UIManager.HasOpenPanel);
            _subs.Add(EventBus.Subscribe<PopupStackChanged>(e => UpdatePanelToggleButtons(e.HasOpenPanel)));

            RefreshDeckCards();
            _subs.Add(EventBus.Subscribe<UserDataChanged>(_ => RefreshDeckCards()));
        }

        // _optionButton과 _panelCloseButton은 같은 자리에 겹쳐있다 — 패널(Friend/Store)이 열려있는 동안만 옵션 대신 닫기 버튼을 보여준다.
        private void UpdatePanelToggleButtons(bool hasOpenPanel)
        {
            _optionButton.gameObject.SetActive(!hasOpenPanel);
            _panelCloseButton.gameObject.SetActive(hasOpenPanel);
        }

        private void RefreshDeckCards()
        {
            FriendDeckDisplay.Apply(_deckCards, UserManager.Current.Friends, (card, key) => card.SetKey(key));
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
