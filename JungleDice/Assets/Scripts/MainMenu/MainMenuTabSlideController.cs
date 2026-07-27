using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JungleDice.MainMenu
{
    [RequireComponent(typeof(ScrollRect))]
    public class MainMenuTabSlideController : MonoBehaviour, IEndDragHandler
    {
        [SerializeField] private RectTransform[] _pages;       // Content 자식, 순서 = 페이지 순서
        [SerializeField] private Button[] _tabButtons;         // _pages와 1:1 인덱스 대응
        [SerializeField] private GameObject[] _tabIcons;       // 각 탭의 아이콘 이미지, _tabButtons와 1:1 대응
        [SerializeField] private float _snapDuration = 0.25f;
        [SerializeField] private Ease _snapEase = Ease.OutQuint;
        [SerializeField] private float _flickVelocityRatio = 3f; // 초당 뷰포트 폭의 배수 — 이 이상으로 빠르게 스와이프하면 드래그 거리와 무관하게 다음/이전 페이지로 전환
        [SerializeField] private float _iconScale = 1.1f;
        [SerializeField] private float _selectedYOffset = 10f;
        [SerializeField] private float _selectionTweenDuration = 0.2f;

        private static readonly Color _selectedIconColor = Color.white;
        private static readonly Color _unselectedIconColor = new(0.7f, 0.7f, 0.7f, 1f); // 밝은 회색

        private ScrollRect _scrollRect;
        private Tween _snapTween;
        private Image[] _iconImages;
        private Tween[] _tabPositionTweens;
        private Tween[] _iconScaleTweens;

        public int CurrentPage { get; private set; }

        private void Awake()
        {
            _scrollRect = GetComponent<ScrollRect>();

            if (_pages.Length != _tabButtons.Length || _pages.Length != _tabIcons.Length)
            {
                Debug.LogError($"[{nameof(MainMenuTabSlideController)}] _pages/_tabButtons/_tabIcons 길이가 일치하지 않습니다.");
                return;
            }

            _iconImages = new Image[_tabButtons.Length];
            _tabPositionTweens = new Tween[_tabButtons.Length];
            _iconScaleTweens = new Tween[_tabButtons.Length];

            for (int i = 0; i < _tabButtons.Length; i++)
            {
                _iconImages[i] = _tabIcons[i].GetComponent<Image>();

                int index = i; // 클로저 캡처 방지
                _tabButtons[i].onClick.AddListener(() => SetPage(index));
            }

            // CanvasScaler(Scale With Screen Size)의 스케일 적용은 이 프레임이 렌더링되기 직전에 처리되므로,
            // Awake() 시점엔 아직 반영 전일 수 있다. 강제로 먼저 끝내야 viewport.rect.width가 정확하다.
            Canvas.ForceUpdateCanvases();
            RecalculatePageWidths();
            SetPage(0);
        }

        public void SetPage(int index)
        {
            index = Mathf.Clamp(index, 0, _pages.Length - 1);
            CurrentPage = index;

            UpdateTabSelectionVisual(index);
            SnapToPage(index);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            float flickThreshold = _scrollRect.viewport.rect.width * _flickVelocityRatio;
            int target = Mathf.Abs(_scrollRect.velocity.x) >= flickThreshold
                ? CurrentPage + (_scrollRect.velocity.x < 0 ? 1 : -1) // 빠른 플릭: 거리 무관하게 한 페이지 이동 (SetPage가 범위 clamp)
                : CalculateNearestPageIndex();

            SetPage(target);
        }

        private void UpdateTabSelectionVisual(int index)
        {
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                bool selected = i == index;
                var buttonRect = _tabButtons[i].GetComponent<RectTransform>();

                Vector2 targetPos = buttonRect.anchoredPosition; // X는 HorizontalLayoutGroup이 관리하는 현재 값 그대로 유지
                targetPos.y = selected ? _selectedYOffset : 0f;

                Vector3 targetScale = selected ? Vector3.one * _iconScale : Vector3.one;

                Color targetColor = selected ? _selectedIconColor : _unselectedIconColor;

                _tabPositionTweens[i]?.Kill();
                _tabPositionTweens[i] = buttonRect.DOAnchorPos(targetPos, _selectionTweenDuration).SetEase(_snapEase);

                _iconScaleTweens[i]?.Kill();
                _iconScaleTweens[i] = _tabIcons[i].transform.DOScale(targetScale, _selectionTweenDuration).SetEase(_snapEase);

                _iconImages[i].color = targetColor;
            }
        }

        private void RecalculatePageWidths()
        {
            float width = _scrollRect.viewport.rect.width;
            foreach (var page in _pages)
                page.GetComponent<LayoutElement>().preferredWidth = width;

            // LayoutElement.preferredWidth는 HorizontalLayoutGroup에 대한 힌트일 뿐, 실제 리빌드는
            // Unity가 프레임 끝 무렵 처리한다. 이후 CalculateTargetPosition 등이 바로 새 크기를
            // 전제로 계산하므로, 여기서 즉시 리빌드를 강제해 같은 프레임 안에서 확정시킨다.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollRect.content);
        }

        private int CalculateNearestPageIndex()
        {
            float pageWidth = _scrollRect.viewport.rect.width;
            float x = -_scrollRect.content.anchoredPosition.x;
            return Mathf.Clamp(Mathf.RoundToInt(x / pageWidth), 0, _pages.Length - 1);
        }

        private Vector2 CalculateTargetPosition(int index)
        {
            float pageWidth = _scrollRect.viewport.rect.width;
            return new Vector2(-pageWidth * index, _scrollRect.content.anchoredPosition.y);
        }

        private void SnapToPage(int index)
        {
            Vector2 target = CalculateTargetPosition(index);

            _snapTween?.Kill();
            _snapTween = _scrollRect.content
                .DOAnchorPos(target, _snapDuration)
                .SetEase(_snapEase)
                .OnUpdate(() => _scrollRect.velocity = Vector2.zero);
        }

        private void OnDestroy()
        {
            _snapTween?.Kill();
            foreach (var t in _tabPositionTweens) t?.Kill();
            foreach (var t in _iconScaleTweens) t?.Kill();
        }

#if UNITY_EDITOR
        private void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled || !Application.isPlaying || _scrollRect == null) return;

            RecalculatePageWidths();
            UpdateTabSelectionVisual(CurrentPage); // 리사이즈로 인한 레이아웃 리빌드가 되돌릴 수 있는 선택 탭의 Y 오프셋/스케일/색을 즉시 재적용
            _snapTween?.Kill();
            _scrollRect.content.anchoredPosition = CalculateTargetPosition(CurrentPage);
        }
#endif
    }
}
