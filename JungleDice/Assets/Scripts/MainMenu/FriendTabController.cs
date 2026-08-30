using System.Collections;
using System.Collections.Generic;
using JungleDice.Core.Event;
using JungleDice.Core.User;
using JungleDice.Data.Table;
using UnityEngine;
using UnityEngine.UI;

namespace JungleDice.MainMenu
{
    public class FriendTabController : MonoBehaviour
    {
        private enum FriendTabState
        {
            List,
            Replace,
        }

        [SerializeField] private RectTransform _content; // ScrollView/Content(VerticalLayoutGroup+ContentSizeFitter) — _gridContent 높이 변경을 전파받는 상위
        [SerializeField] private Transform _gridContent;
        [SerializeField] private FriendListItem _listItemPrefab;
        [SerializeField] private GameObject _friendCardList;
        [SerializeField] private FriendSlot[] _slots;
        [SerializeField] private FriendPickPopup _pickPopup;
        [SerializeField] private GameObject _replaceUI;
        [SerializeField] private FriendCardMainControl _replaceCard;
        [SerializeField] private Button _panelBackgroundButton;
        [SerializeField] private Button[] _deckButtons; // 3개, UserData 덱 인덱스와 1:1

        private readonly CompositeDisposable _subs = new();
        private readonly List<FriendListItem> _listItems = new();
        private FriendTabState _state = FriendTabState.List;

        private void Awake()
        {
            PopulateList();
            RefreshSlots();
            RefreshListVisibility();

            _pickPopup.gameObject.SetActive(false); // 초기화 단계에서는 항상 비활성 — 씬 설정을 신뢰하지 않고 명시적으로 보장
            _replaceUI.SetActive(false);
            _panelBackgroundButton.onClick.AddListener(OnPanelBackgroundClicked);
            _subs.Add(EventBus.Subscribe<UserDataChanged>(_ =>
            {
                RefreshSlots();
                RefreshListVisibility();
            }));

            for (int i = 0; i < _deckButtons.Length; i++)
            {
                int index = i; // 클로저 캡처 방지
                _deckButtons[i].onClick.AddListener(() => UserManager.Current.SelectDeck(index));
            }
        }

        private void PopulateList()
        {
            foreach (var data in CardTable.Instance.GetAll())
            {
                var item = Instantiate(_listItemPrefab, _gridContent);
                item.SetKey(data.key);
                item.Clicked += OnListItemClicked;
                _listItems.Add(item);
            }

            // GridLayoutGroup(_gridContent)의 preferredHeight는 자식 수에 따라 바뀌지만 리빌드는 프레임 끝에 지연되므로,
            // 그 값을 곧바로 물려받는 Content(VerticalLayoutGroup+ContentSizeFitter)까지 강제로 즉시 리빌드한다.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        }

        private void RefreshSlots()
        {
            FriendDeckDisplay.Apply(_slots, UserManager.Current.Friends, (slot, key) => slot.SetKey(key));
        }

        private void RefreshListVisibility()
        {
            var friends = new HashSet<int>(UserManager.Current.Friends);
            foreach (var item in _listItems)
                item.gameObject.SetActive(!friends.Contains(item.Key));

            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        }

        private void OnListItemClicked(FriendListItem item)
        {
            _pickPopup.Show(item.Key, () => EnterReplaceMode(item.Key));
        }

        private void EnterReplaceMode(int key)
        {
            _state = FriendTabState.Replace;
            _friendCardList.SetActive(false);
            _replaceUI.SetActive(true);
            _replaceCard.Setup(key);
        }

        public void ExitReplaceMode()
        {
            _state = FriendTabState.List;
            _replaceUI.SetActive(false);
            _friendCardList.SetActive(true);
        }

        private void RequestReplace(int slotIndex, int key)
        {
            if (_state != FriendTabState.Replace) return; // OnSlotDropped는 List 상태에서도 들어올 수 있는 입력이라 가드 필요

            var friends = new List<int>(UserManager.Current.Friends);
            friends[slotIndex] = key;
            UserManager.Current.SetFriends(friends); // 내부에서 EventBus.Publish(new UserDataChanged())

            ExitReplaceMode();
        }

        public void OnSlotClicked(int slotIndex)
        {
            if (_state == FriendTabState.List)
            {
                _pickPopup.Show(_slots[slotIndex].Key, null, selectable: false);
                return;
            }

            RequestReplace(slotIndex, _replaceCard.Data.Key);
        }

        public void OnSlotDropped(int slotIndex, int key) => RequestReplace(slotIndex, key);

        private void OnPanelBackgroundClicked()
        {
            if (_state != FriendTabState.Replace) return; // List 상태에서는 아무 의미 없는 클릭
            ExitReplaceMode();
        }

        private void OnDestroy() => _subs.Dispose();
    }
}
