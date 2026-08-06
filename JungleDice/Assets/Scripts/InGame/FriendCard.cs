using DG.Tweening;
using JungleDice.Core.Sprites;
using JungleDice.Data.Table;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JungleDice.InGame
{
    [RequireComponent(typeof(CanvasGroup))]
    public class FriendCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private Image _cardImage;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _descText;
        [SerializeField] private TextMeshProUGUI _attText;
        [SerializeField] private TextMeshProUGUI _hpText;

        private CanvasGroup _canvasGroup;
        private Transform _dragLayer;
        private HandSlot _homeSlot;
        private bool _wasPlaced;

        public int Key { get; private set; }
        public HandSlot HomeSlot => _homeSlot;

        private void Awake() => _canvasGroup = GetComponent<CanvasGroup>();

        public void SetKey(int key)
        {
            Key = key;

            var data = CardTable.Instance?.Get(key);
            if (data == null) return; // CardTable.Get이 이미 LogError를 남김

            _cardImage.sprite = SpriteManager.GetCard(key.ToString());
            _nameText.text = data.cardname;
            _descText.text = data.explain;
            _attText.text = data.att.ToString();
            _hpText.text = data.hp.ToString();
        }

        public void Initialize(Transform dragLayer) => _dragLayer = dragLayer;

        // 슬롯 위치까지 트윈으로 이동한 뒤 도착하면 그 슬롯의 자식으로 붙는다(덱 드로우/hand 정리 공용)
        public void MoveToSlot(HandSlot slot, float duration)
        {
            transform.DOMove(slot.transform.position, duration)
                .SetEase(Ease.OutQuint)
                .OnComplete(() => AttachToSlot(slot));
        }

        // 트윈 없이 즉시 슬롯의 자식으로 붙인다(드롭 실패 후 원래 자리 복귀 등)
        public void AttachToSlot(HandSlot slot)
        {
            _homeSlot = slot;
            transform.SetParent(slot.transform, worldPositionStays: false);
            ((RectTransform)transform).anchoredPosition = Vector2.zero;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _canvasGroup.blocksRaycasts = false; // 이 카드 자신이 아래 FieldSlot의 레이캐스트를 가로막지 않도록

            transform.SetParent(_dragLayer, worldPositionStays: true); // 자기 슬롯 밖으로 — 즉시 hand에서 빠짐(슬롯은 빈 채로 남음, 다른 카드가 채우지 않음)
            transform.SetAsLastSibling(); // 다른 UI보다 위에 그려지도록
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
            if (_wasPlaced) return; // 필드 배치 성공 — 이번 프레임 안에 파괴 예정, 되돌릴 필요 없음

            AttachToSlot(_homeSlot); // 드롭 실패 — 원래 있던 자기 슬롯으로 즉시 복귀
        }

        public void NotifyPlaced() => _wasPlaced = true;
    }
}
