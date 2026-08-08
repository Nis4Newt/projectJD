using UnityEngine;
using UnityEngine.EventSystems;

namespace JungleDice.InGame
{
    public class FieldSlot : MonoBehaviour, IDropHandler
    {
        [SerializeField] private int _index; // 전체 필드 6자리 중 절대 번호(플레이어는 4/5/6)

        public int Index => _index;
        public bool IsOccupied => transform.childCount > 0;

        public void OnDrop(PointerEventData eventData)
        {
            var card = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<FriendCard>() : null;
            if (card == null) return; // FriendCard가 아닌 다른 드래그 대상은 무시(현재는 존재하지 않지만 방어)

            InGameSceneManager.Instance.TryPlaceFriendCard(this, card);
        }
    }
}
