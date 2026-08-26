using DG.Tweening;
using JungleDice.InGame;
using UnityEngine;
using UnityEngine.EventSystems;

namespace JungleDice.MainMenu
{
    [RequireComponent(typeof(FriendCard))]
    [RequireComponent(typeof(CanvasGroup))]
    public class FriendCardMainControl : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private Transform _dragLayer;

        private CanvasGroup _canvasGroup;
        private FriendCard _friendCard;
        private Transform _originParent;
        private Vector2 _originAnchoredPosition; // Awake 1회 캐싱 — Setup마다 다시 읽지 않음(원위치는 프리팹 배치상 고정값)
        private bool _wasDropped;

        public FriendCard Data => _friendCard;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _friendCard = GetComponent<FriendCard>();
            _originParent = transform.parent;
            _originAnchoredPosition = ((RectTransform)transform).anchoredPosition;
        }

        public void Setup(int key)
        {
            _friendCard.SetKey(key);
            _wasDropped = false;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _canvasGroup.blocksRaycasts = false;
            transform.SetParent(_dragLayer, worldPositionStays: true);
            transform.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_dragLayer, eventData.position, eventData.pressEventCamera, out var localPoint);
            ((RectTransform)transform).localPosition = localPoint;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _canvasGroup.blocksRaycasts = true;
            if (_wasDropped) return; // 슬롯이 처리 완료 — ExitReplaceMode가 곧 _replaceUI를 비활성화함

            transform.SetParent(_originParent, worldPositionStays: false);
            ((RectTransform)transform).DOAnchorPos(_originAnchoredPosition, 0.2f).SetEase(Ease.OutQuint);
        }

        public void NotifyDropped() => _wasDropped = true; // FriendSlot.OnDrop이 호출
    }
}
