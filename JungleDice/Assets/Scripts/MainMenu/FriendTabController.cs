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

        [SerializeField] private Transform _gridContent;
        [SerializeField] private FriendListItem _listItemPrefab;
        [SerializeField] private GameObject _friendCardList;
        [SerializeField] private FriendSlot[] _slots;
        [SerializeField] private FriendPickPopup _pickPopup;
        [SerializeField] private GameObject _replaceUI;
        [SerializeField] private FriendCardMainControl _replaceCard;
        [SerializeField] private Button _panelBackgroundButton;

        private readonly CompositeDisposable _subs = new();
        private FriendTabState _state = FriendTabState.List;

        private void Awake()
        {
            PopulateList();
            RefreshSlots();

            _pickPopup.gameObject.SetActive(false); // 초기화 단계에서는 항상 비활성 — 씬 설정을 신뢰하지 않고 명시적으로 보장
            _replaceUI.SetActive(false);
            _panelBackgroundButton.onClick.AddListener(OnPanelBackgroundClicked);
            _subs.Add(EventBus.Subscribe<UserDataChanged>(_ => RefreshSlots()));
        }

        private void PopulateList()
        {
            foreach (var data in CardTable.Instance.GetAll())
            {
                var item = Instantiate(_listItemPrefab, _gridContent);
                item.SetKey(data.key);
                item.Clicked += OnListItemClicked;
            }
        }

        private void RefreshSlots()
        {
            var friends = UserManager.Current.Friends; // 항상 3개(UserData 기본값이자 SetFriends 호출부의 불변 조건)
            for (int i = 0; i < _slots.Length; i++)
                _slots[i].SetKey(friends[i]);
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
            if (_state != FriendTabState.Replace) return; // 슬롯 클릭은 List 상태에서도 들어올 수 있는 입력이라 가드 필요

            var friends = new List<int>(UserManager.Current.Friends);
            friends[slotIndex] = key;
            UserManager.Current.SetFriends(friends); // 내부에서 EventBus.Publish(new UserDataChanged())

            ExitReplaceMode();
        }

        public void OnSlotClicked(int slotIndex)
        {
            if (_state != FriendTabState.Replace) return;
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
