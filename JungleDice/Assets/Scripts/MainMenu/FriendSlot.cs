using UnityEngine;
using UnityEngine.EventSystems;

namespace JungleDice.MainMenu
{
    public class FriendSlot : MonoBehaviour, IDropHandler
    {
        [SerializeField] private int _index; // 0~2, UserData.Friends 인덱스와 1:1
        [SerializeField] private FriendListItem _item; // FriendCard + Button 조합을 그대로 재사용(별도 Button 필드를 두지 않음)
        [SerializeField] private FriendTabController _controller;

        public int Index => _index;

        private void Awake() => _item.Clicked += _ => _controller.OnSlotClicked(_index);

        public void SetKey(int key) => _item.SetKey(key);

        public void OnDrop(PointerEventData eventData)
        {
            var card = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<FriendCardMainControl>() : null;
            if (card == null) return;

            card.NotifyDropped();
            _controller.OnSlotDropped(_index, card.Data.Key);
        }
    }
}
