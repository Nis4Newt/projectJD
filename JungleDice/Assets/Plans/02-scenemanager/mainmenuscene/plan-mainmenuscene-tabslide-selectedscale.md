# 메인메뉴 탭 선택 상태 시각 효과 구현 계획

> 상위 문서: [메인메뉴 슬라이드/탭 구현 계획](./plan-mainmenuscene-tabslide.md) (`UpdateTabHighlights`의 active 토글 방식을 대체하며 파생)   
> 관련 문서: [탭/슬라이드 DOTween 전환 계획](./plan-mainmenuscene-tabslide-dotween.md) (이 문서가 도입한 `Tween`/`Ease` 패턴을 그대로 재사용)   
> 의존 관계: `Assets/Plugins/Demigiant/DOTween` (이미 설치·사용 중)   
> 범위: 선택된 탭의 시각 피드백을 `_tabHighlights[i].SetActive(i == index)`에서 "버튼을 위로 살짝 들어올림(Y 오프셋) + 아이콘 크기 10% 증가" 트윈과 "아이콘 색(선택=흰색 고정/비선택=진한 회색 고정) 즉시 전환"으로 교체하고, `_tabHighlights` 필드를 실제 성격(각 탭의 아이콘 이미지)에 맞게 `_tabIcons`로 이름을 바꾼다. 탭 바(`footer`)가 화면비에 맞춰 가로로 꽉 차는 것은 `footer`의 RectTransform 앵커(Bottom+Stretch)가 담당하며 이 문서의 범위 밖이다. 탭 전환 로직(`SetPage`/`OnEndDrag`), 페이지 스냅, 페이지 폭 계산 자체는 변경하지 않는다.

---

## 배경

현재 `MainMenuTabSlideController.UpdateTabHighlights(int index)`는 선택된 탭의 하이라이트 오브젝트만 `SetActive(true)`로 켜고 나머지는 끈다(이진 on/off, `MainMenuTabSlideController.cs:65-69`). 이 배열(`_tabHighlights`)이 실제로 들고 있는 것은 각 탭 버튼 자식의 아이콘 이미지이므로, 이번 문서에서 `_tabIcons`로 이름을 바로잡는다. 새 디자인 요구는 다음과 같다:

1. 아이콘은 켜고 끄는 대신 **크기**로 선택 여부를 표현한다 — 선택 시 10% 커짐
2. 선택된 탭 **버튼 자체가 위로 살짝 들어올려진다**(세로 크기를 늘리는 게 아니라 Y 위치만 살짝 이동)
3. 아이콘 색은 선택 시 **흰색 고정**, 비선택 시 **진한 회색 고정** — 둘 다 씬의 원래 스프라이트 색과 무관한 상수

씬(`MainMenu.unity`) 확인 결과, 탭 버튼(예: fileID `255766767`, `RectTransform.sizeDelta: {x: 0, y: 100}`)은 `footer`(`HorizontalLayoutGroup`, `m_ChildControlHeight: 0`, `m_ChildControlWidth: 1`, `m_ChildForceExpandWidth: 1`)의 자식이다. 레이아웃 그룹이 자식의 **너비**만 제어하고 **높이/세로 위치**는 강제로 다시 계산하지 않는 한 건드리지 않으므로, 탭 버튼의 `RectTransform.anchoredPosition.y`를 스크립트로 바꿔도 평소엔 되돌아가지 않는다. 아이콘 오브젝트(`_tabIcons[i]`, 기존 필드명 `_tabHighlights[i]`)는 각 탭 버튼의 자식으로 이미 배열에 연결돼 있고, `Image` 컴포넌트를 갖고 있다(예: fileID `296572910` "Image (4)").

`footer` 자신은 Bottom+Stretch 앵커(`AnchorMin: (0,0)`, `AnchorMax: (1,0)`)로 부모(`Viewport`) 폭을 자동으로 따라가고, `ChildForceExpandWidth: 1`인 `HorizontalLayoutGroup`이 그 폭을 4개 버튼에게 나눠준다 — 탭 바가 화면비에 맞춰 가로로 꽉 차는 것은 이 앵커 설정만으로 해결되며, 별도 코드가 필요 없다.

---

## 설계 목표

- 4개 탭 아이콘은 항상 보이는 상태를 기본으로 하고(`SetActive` 토글 제거), 선택 여부에 따라 크기와 색이 달라진다
- 선택된 탭 버튼: 세로 크기는 그대로 두고, Y 위치만 `_selectedYOffset`만큼 살짝 위로 이동(들어올림)
- 선택된 탭 아이콘 크기: 비선택 100%(`Vector3.one` 기준) ↔ 선택 110%
- 아이콘 색: 선택 시 흰색 고정, 비선택 시 진한 회색 고정 — 둘 다 씬 값을 캡처하지 않는 상수이며, 즉시 전환한다(트윈 없음)
- 버튼 위치/아이콘 크기는 즉시 전환이 아니라 DOTween으로 부드럽게 보간(`plan-mainmenuscene-tabslide-dotween.md`의 `Tween`/`Ease` 패턴 재사용)
- `_tabHighlights`를 실제 성격에 맞는 이름(`_tabIcons`)으로 바꾸되, 이미 씬에 연결된 4개 참조는 그대로 유지되도록 한다
- `SetPage` 단일 진입점, 탭 인덱스-페이지 매칭 등 기존 구조는 그대로 유지 — 이번 문서는 `UpdateTabHighlights`의 내부 구현과 필드명만 다룬다

---

## 핵심 설계 결정

### 1. 버튼은 `anchoredPosition.y` 트윈, 아이콘은 `localScale` 트윈 — 서로 다른 트랜스폼 요소를 쓰는 이유

버튼의 세로 크기(`sizeDelta.y`)를 늘리는 대신 버튼을 위로 살짝 이동시킨다 — `sizeDelta`를 바꾸면 버튼의 배경 이미지 자체가 늘어나 보이지만, `anchoredPosition`만 바꾸면 버튼의 모양·크기는 그대로 유지한 채 위치만 옮겨져 배경 이미지가 찌그러지지 않는다. 아이콘은 버튼의 자식이므로 버튼이 위로 이동하면 아이콘도 자동으로 함께 따라 올라간다 — 아이콘 위치를 별도로 옮기는 코드는 필요 없다. 아이콘 자신의 확대(10%)는 `localScale`로 독립적으로 적용된다(버튼이 스케일을 쓰지 않으므로 "부모 스케일이 자식에 곱연산으로 전파"되는 문제 자체가 없다).

### 2. 기준값은 캡처하지 않고 상수로 고정 — 아이콘 스케일 1, 버튼 Y 오프셋 0, 색상 2종

씬의 모든 아이콘은 `localScale`이 항상 `Vector3.one`이고, 모든 탭 버튼은 비선택 상태의 `anchoredPosition.y`가 항상 `0`이다 — 색상과 마찬가지로 이 두 값도 "씬마다 다를 수 있어 캡처해야 하는 값"이 아니라 "이 씬의 고정 전제"이므로, `Awake()`에서 배열로 캡처하지 않고 코드에 직접 `Vector3.one`/`0f`로 못박는다.

```csharp
[SerializeField] private float _iconScale = 1.1f;
[SerializeField] private float _selectedYOffset = 10f;
[SerializeField] private float _selectionTweenDuration = 0.2f;

private static readonly Color _selectedIconColor = Color.white;
private static readonly Color _unselectedIconColor = new(0.7f, 0.7f, 0.7f, 1f); // 진한 회색

private Image[] _iconImages;
private Tween[] _tabPositionTweens;
private Tween[] _iconScaleTweens;

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

    Canvas.ForceUpdateCanvases();
    RecalculatePageWidths();
    SetPage(0);
}
```

`_selectedIconColor`/`_unselectedIconColor`는 인스펙터로 노출하지 않는다(`[SerializeField]` 없음) — 요구사항 자체가 "고정값"이므로 디자이너가 씬마다 조정할 여지를 열어둘 이유가 없다. 조정이 필요해지면 그때 `[SerializeField]`로 바꾼다(YAGNI).

버튼의 X 위치(`anchoredPosition.x`)는 `HorizontalLayoutGroup`이 계속 관리하는 값이라 여기서 캡처하지 않는다 — 아래 결정 3에서 `UpdateTabSelectionVisual`이 매번 그 시점의 현재 X를 그대로 읽어 Y만 덮어쓴다.

### 3. `UpdateTabHighlights` → `UpdateTabSelectionVisual`로 교체 (위치 + 크기는 트윈, 색상은 즉시 전환)

```csharp
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

public void SetPage(int index)
{
    index = Mathf.Clamp(index, 0, _pages.Length - 1);
    CurrentPage = index;

    UpdateTabSelectionVisual(index);
    SnapToPage(index);
}
```

매 `SetPage` 호출마다 4개 탭 전부를 순회해 "선택된 탭은 들어올려짐+확대+흰색, 나머지는 기준 위치+기준 크기+회색"을 다시 계산한다 — 이전 선택 탭을 별도로 추적해 그것만 되돌리는 방식보다 단순하고, `SetPage`가 어떤 인덱스에서 호출되든(중복 클릭 포함) 항상 올바른 최종 상태로 수렴한다. 색상은 트윈 없이 매번 즉시 대입되므로 별도 `Kill()`/추적이 필요 없다.

기존 `_snapEase`(`Ease.OutQuint`, 페이지 스냅과 동일 필드)를 그대로 재사용한다 — 탭 전환과 아이콘 크기 전환이 같은 타이밍 감각을 가져야 자연스러우므로 새 이징 필드를 따로 만들지 않는다.

### 4. 탭 바 반응형 너비는 코드가 아니라 `footer`의 앵커에 맡긴다

`footer`는 Bottom+Stretch 앵커로 부모(`Viewport`) 폭을 자동으로 따라가고, `HorizontalLayoutGroup`이 그 폭을 4개 버튼에게 나눠주므로 화면비가 바뀌어도 별도 재계산 코드가 필요 없다. `RectTransform.sizeDelta`나 `anchoredPosition.x`를 건드리는 코드를 여기 추가하지 않는다 — 스트레치 앵커에서 `sizeDelta.x`는 "폭"이 아니라 "부모 폭에 얹는 추가 마진"이므로, 폭 값을 대입하면 실제 폭이 의도치 않게 배로 커진다. `plan-mainmenuscene-tabslide.md`의 `RecalculatePageWidths()`는 `Content`/페이지에 대한 것으로 `footer`와는 무관하다 — 페이지는 `ChildControlWidth: 1`이라 각자의 `LayoutElement.preferredWidth`를 그대로 쓰는 방식이라 애초에 `footer`와 폭 계산 방식이 다르다.

### 5. `_tabHighlights` → `_tabIcons`로 필드명 변경

이 배열은 원래 선택 하이라이트용으로 이름 붙었지만 실제로는 각 탭의 아이콘 이미지 자체이므로 `_tabIcons`로 이름을 바로잡는다.

```csharp
[SerializeField] private GameObject[] _tabIcons; // _tabButtons와 1:1 대응
```

타입은 `GameObject[]`(스케일 트윈 대상은 `.transform`, 색상 대상은 `GetComponent<Image>()`로 접근)로 기존과 동일하다. `SetActive` 호출만 사라진다 — 대신 씬에 저장된 4개 아이콘 오브젝트가 모두 `Is Active` 켜진 상태여야 한다(기존엔 런타임에 `SetPage(0)`이 3개를 꺼서 눈에 안 띄었을 뿐).

### 6. `OnDestroy`에서 트윈 배열 정리

```csharp
private void OnDestroy()
{
    _snapTween?.Kill();
    foreach (var t in _tabPositionTweens) t?.Kill();
    foreach (var t in _iconScaleTweens) t?.Kill();
}
```

---

## 클래스 구조

```
MainMenuTabSlideController : MonoBehaviour, IEndDragHandler   (기존 파일 수정, MainMenu/)
├── (기존 필드/메서드 유지: _pages, _tabButtons, _snapDuration, _snapEase,
│    _flickVelocityRatio, _scrollRect, _snapTween, CurrentPage, OnEndDrag,
│    RecalculatePageWidths, CalculateNearestPageIndex, CalculateTargetPosition, SnapToPage)
├── _tabIcons : GameObject[]                       ← 기존 파일 수정, _tabHighlights를 개명
│    (씬 참조는 에디터에서 4개 다시 연결)
├── _iconScale : float = 1.1f                      ← 신규 [SerializeField]
├── _selectedYOffset : float = 10f                 ← 신규 [SerializeField]
├── _selectionTweenDuration : float = 0.2f         ← 신규 [SerializeField]
├── _selectedIconColor : Color = Color.white               ← 신규 private static readonly (상수)
├── _unselectedIconColor : Color = (0.7,0.7,0.7,1)         ← 신규 private static readonly (상수)
├── _iconImages : Image[]                          ← 신규 private, Awake에서 캡처
├── _tabPositionTweens : Tween[]                   ← 신규 private
├── _iconScaleTweens : Tween[]                     ← 신규 private
├── Awake()                                        ← 기존 파일 수정, _iconImages/트윈 배열 캡처 추가
│    (아이콘 기본 스케일=1, 버튼 기본 Y=0은 캡처하지 않고 상수로 가정)
├── UpdateTabHighlights(int)                       ← 제거
├── UpdateTabSelectionVisual(int)                  ← 신규, 위 로직 대체 (위치+크기는 트윈, 색상은 즉시 대입)
├── SetPage(int)                                   ← 기존 파일 수정, 호출부만 교체
├── OnRectTransformDimensionsChange()              ← 기존 파일 수정, 리사이즈 후 UpdateTabSelectionVisual 재적용 추가 (#if UNITY_EDITOR)
└── OnDestroy()                                    ← 기존 파일 수정, 트윈 배열 정리 확장
```

---

## 파일 구성

```
Assets/
└── Scripts/
    └── MainMenu/
        └── MainMenuTabSlideController.cs   ← 기존 파일 수정 (신규 파일 없음)
```

---

## Unity 씬/오브젝트 구성

이번 문서는 씬 구조를 새로 만들지 않고, 기존 `MainMenu.unity`의 `footer`(`HorizontalLayoutGroup`) 하위 4개 탭 버튼과 그 자식 아이콘 오브젝트를 그대로 사용한다. 씬 변경 사항은 (1) 아이콘 오브젝트 4개의 활성 상태 확인, (2) `_tabHighlights` → `_tabIcons` 리네이밍으로 끊어진 4개 GameObject 참조를 인스펙터에서 다시 연결하는 것이다 — `footer`의 앵커(Bottom+Stretch)는 이미 올바르게 설정돼 있으므로 추가로 손댈 것이 없다.

```
[Scene: MainMenu]
└── footer (HorizontalLayoutGroup, Bottom+Stretch 앵커로 부모 폭 자동 추적)
    ├── Tab_0 (Button, RectTransform.anchoredPosition.y 트윈 대상)
    │   └── Icon_0 (GameObject + Image, Is Active 켜짐 상태 유지 — 더 이상 SetActive로 토글하지 않음)
    ├── Tab_1 / Icon_1
    ├── Tab_2 / Icon_2
    └── Tab_3 / Icon_3
```

---

## 이번 범위에서 제외

- **탭 인덱스-페이지 매칭, 스와이프/플릭 판정, 페이지 폭 계산 자체** — `plan-mainmenuscene-tabslide.md` 범위 그대로 유지, 이번 문서는 건드리지 않는다
- **씬의 프로토타입 오브젝트 이름 정리(`Image (3)` 등 → `Tab_0` 등)** — `plan-mainmenuscene-tabslide.md`가 이미 남겨둔 미해결 체크리스트 항목이며 이 문서가 대신 처리하지 않는다
- **`_selectedYOffset`/`_iconScale`, 아이콘 색 상수값의 디자인 확정치 검증** — 요청받은 수치를 그대로 반영하며, 실제 비주얼 검수 후 값 조정은 에디터/코드 작업으로 별도 진행
- **`footer`의 앵커/레이아웃 설정 자체를 코드로 관리하는 것** — Bottom+Stretch 앵커 + `HorizontalLayoutGroup`만으로 충분하다고 판단(결정 4), 코드로 재구현하지 않는다

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 위치/스케일 트윈 도중 다른 탭 클릭 | `SetPage`가 매번 4개 탭 전부의 목표 상태를 다시 계산하고 기존 트윈 `Kill()` 후 재시작 — 중간에 눌러도 최종적으로 선택된 탭만 들어올림+확대+흰색 상태로 수렴. 색상은 트윈이 아니라 매번 즉시 대입되므로 중간에 눌러도 어긋날 여지가 없음 |
| 씬에 저장된 아이콘 오브젝트 일부가 비활성(`Is Active` 꺼짐) 상태로 남아있음 | `SetActive` 토글을 더 이상 쓰지 않으므로, 꺼진 채로 두면 해당 탭은 선택돼도 아이콘이 보이지 않음 — 구현 전 씬에서 4개 모두 활성 확인 필수 |
| 아이콘 오브젝트에 `Image` 컴포넌트가 없음 | `Awake()`의 `GetComponent<Image>()`가 `null` 반환 → 이후 `_iconImages[i].color` 대입 시 `NullReferenceException` — 씬의 모든 `_tabIcons` 오브젝트가 `Image` 컴포넌트를 갖고 있는지 사전 확인 필요 |
| `_tabHighlights` → `_tabIcons` 필드명 변경 후 씬에서 4개 참조를 다시 연결하지 않음 | 씬에 저장된 4개 GameObject 참조가 끊어져 `_tabIcons`가 빈 배열이 됨 → `Awake()`의 길이 검증에서 `Debug.LogError` 후 초기화 중단(예외는 아니지만 탭 기능 전체가 멈춤) — 코드 수정 후 반드시 인스펙터에서 재연결 필요 |
| (에디터 전용) Play 중 Game 뷰 리사이즈 시 `HorizontalLayoutGroup`이 자동으로 자식 Y 위치를 다시 정렬함 | 선택된 탭의 `_selectedYOffset`이 사라질 수 있음 — `OnRectTransformDimensionsChange`에서 `RecalculatePageWidths()` 이후 `UpdateTabSelectionVisual(CurrentPage)`를 재호출해 즉시 복구 |
| `_tabPositionTweens`/`_iconScaleTweens` 배열이 `Awake()`에서 초기화되지 않은 채 `SetPage`가 먼저 호출됨 | 구조상 `Awake()` 안에서 배열 초기화 직후 `SetPage(0)`을 호출하므로 발생하지 않음 — 순서를 바꾸지 않도록 주의 |
| 씬에서 특정 아이콘의 `localScale`이 `Vector3.one`이 아니게 배치돼 있음 | 캡처하지 않고 `Vector3.one`을 기준으로 가정하므로, 그 아이콘은 선택/비선택 전환 시 원래 스케일을 무시하고 `Vector3.one` 또는 `Vector3.one * _iconScale`로 강제 트윈됨 — 씬의 모든 아이콘이 스케일 1로 배치돼 있는지 사전 확인 필요 |
| 씬에서 특정 탭 버튼의 `anchoredPosition.y`가 0이 아니게 배치돼 있음 | 캡처하지 않고 `0f`을 기준으로 가정하므로, 비선택 시 그 버튼은 원래 위치가 아니라 강제로 Y=0으로 트윈됨(위치가 살짝 튈 수 있음) — 씬의 모든 탭 버튼이 Y=0에 배치돼 있는지 사전 확인 필요 |
| `footer`의 앵커가 나중에 고정 점 앵커(`AnchorMin == AnchorMax`)로 바뀜 | 탭 바가 더 이상 화면비에 반응하지 않고 고정 폭으로 남음 — `footer`의 앵커는 Bottom+Stretch로 유지돼야 한다 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | Tab_2 클릭 | 버튼2가 `anchoredPosition.y` 기준값 + `_selectedYOffset`만큼 위로, 아이콘2 `localScale`이 기준값의 1.1배로 트윈되고, 아이콘2 색은 즉시 흰색으로 전환. 나머지 3개는 기준 위치 + 기준 크기(100%)로 트윈되고 색은 즉시 진한 회색으로 전환 |
| 2 | Tab_0 선택 상태에서 Tab_3 클릭 | Tab_0의 버튼이 원래 Y 위치로 내려가고 아이콘이 100%로 축소되며 색은 즉시 회색으로, 동시에 Tab_3가 들어올려지며 110%로 확대되고 색은 즉시 흰색으로 전환 |
| 3 | 확대 트윈 진행 중 다른 탭 재클릭 | 기존 위치/스케일 트윈 `Kill()` 후 현재 위치/크기에서 새 목표로 트윈 재시작(점프 없이 자연스럽게 이어짐), 색은 매번 즉시 새 값으로 대입 |
| 4 | `Awake()` 직후(초기 `SetPage(0)` 호출) | Tab_0만 들어올려진 위치+110%로 트윈 재생되고 색은 즉시 흰색, 나머지 3개는 기준 위치+기준 크기로 트윈되고 색은 즉시 진한 회색 |
| 5 | 씬에서 아이콘 4개 오브젝트가 모두 `Is Active` 켜진 상태로 Play | 비선택 탭도 100% 크기·진한 회색으로 아이콘이 보임(완전히 숨겨지지 않음) — "위치+크기+색"으로만 선택을 구분하는 디자인 의도대로 동작 |
| 6 | 해상도 프리셋이 다른 두 기기(또는 에디터 Game 뷰 프리셋)에서 각각 Play | `footer`가 Bottom+Stretch 앵커로 화면 가로 폭에 맞게 자동 배치되고, 4개 탭 버튼이 `HorizontalLayoutGroup`에 의해 그 폭을 나눠 가짐(코드 개입 없음) |
| 7 | (에디터 전용) 어떤 탭이 선택된 상태에서 Play 중 Game 뷰를 드래그로 리사이즈 | 리사이즈로 인한 레이아웃 리빌드로 선택 탭의 Y 오프셋이 순간적으로 되돌아가더라도, 곧바로 재호출되는 `UpdateTabSelectionVisual(CurrentPage)`가 들어올림/스케일/색을 즉시 재적용해 최종적으로 선택 탭이 계속 들어올려진 상태로 보임 |
| 8 | 필드명 변경(`_tabHighlights` → `_tabIcons`) 후 씬에서 4개 GameObject를 인스펙터에 다시 연결 | `_tabIcons`에 4개 참조가 정상적으로 채워지고 `Awake()`의 길이 검증을 통과함 |

---

## 구현 시 주의사항

- **버튼은 `anchoredPosition`, 아이콘은 `localScale`을 트윈한다** — 버튼에 스케일을 쓰면 배경 이미지가 늘어나 보이므로 쓰지 않는다.
- **아이콘 기본 스케일(1)과 버튼 기본 Y 위치(0)는 씬에서 캡처하지 않고 상수로 가정한다** — 색상과 동일한 이유(고정 전제)로 `Awake()`에서 배열로 캡처하지 않는다. 씬의 아이콘/버튼이 실제로 이 전제(`localScale = Vector3.one`, `anchoredPosition.y = 0`)를 만족하는지는 구현 전 확인이 필요하다.
- **`_selectedIconColor`/`_unselectedIconColor`는 `[SerializeField]`가 아닌 `private static readonly Color` 상수로 선언한다** — 인스펙터에 노출하지 않는다.
- **`footer`의 `HorizontalLayoutGroup.m_ChildControlHeight`가 나중에 실수로 켜지면(1로 변경되면) 레이아웃이 버튼 크기를 되돌리려 할 수 있다** — 이 세팅이 0으로 유지되는지 확인 필수(지금은 크기가 아니라 위치만 바꾸므로 영향은 제한적이지만, 안전을 위해 유지).
- **`footer`의 앵커(Bottom+Stretch)를 그대로 유지한다 — 폭을 코드로 재계산하는 로직을 추가하지 않는다.** 스트레치 앵커에서 `sizeDelta.x`는 폭이 아니라 마진이므로, 여기에 폭 값을 대입하면 실제 폭이 의도치 않게 커지는 버그가 생긴다(결정 4 참고).
- **`_tabHighlights` → `_tabIcons` 리네이밍 후 씬에서 4개 GameObject 참조를 반드시 다시 연결한다** — Unity가 직렬화 필드를 이름으로 매칭하므로 리네이밍만으로는 기존 연결이 끊어진다.
- **트윈 배열(`_tabPositionTweens`/`_iconScaleTweens`)은 `_tabButtons.Length` 크기로 `Awake()`에서 반드시 초기화한다** — 초기화 누락 시 `UpdateTabSelectionVisual`에서 `NullReferenceException`.
- **아이콘 색은 트윈하지 않고 `_iconImages[i].color`에 즉시 대입한다** — 위치/크기와 달리 색 전환에 트윈을 쓰지 않기로 했으므로 `Tween`/`Kill()` 관리 대상에 포함하지 않는다.
- **기존 `_snapEase`를 재사용하고 새 이징 필드를 추가하지 않는다** — 탭 전환, 아이콘 위치/크기 전환의 타이밍을 통일한다.
- **씬에서 4개 아이콘 오브젝트의 `Is Active`를 켜두는 작업을 빠뜨리지 않는다** — 코드만 바꾸고 씬을 그대로 두면 꺼진 아이콘이 영영 보이지 않는다.

---

## 구현 후 체크리스트

- [x] 씬에서 4개 아이콘 오브젝트가 모두 `Is Active` 켜진 상태인지, `Image` 컴포넌트를 갖고 있는지 확인
- [x] `MainMenuTabSlideController.cs`: `_tabHighlights` → `_tabIcons` 리네이밍
- [x] `_iconScale`/`_selectedYOffset`/`_selectionTweenDuration` `[SerializeField]` 추가, `_selectedIconColor`/`_unselectedIconColor` `private static readonly Color` 상수 추가
- [ ] 씬에서 `_tabIcons`에 4개 GameObject 참조를 인스펙터에서 다시 연결
- [ ] 씬에서 4개 아이콘의 `localScale`이 `Vector3.one`인지, 4개 탭 버튼의 `anchoredPosition.y`가 `0`인지 확인(전제 조건)
- [x] `Awake()`에 `_iconImages`/트윈 배열 2종 캡처·초기화 로직 추가
- [x] `UpdateTabHighlights` 제거, `UpdateTabSelectionVisual(int)`로 교체(위치+크기는 트윈, 색상은 즉시 대입), `SetPage`에서 호출부 교체
- [x] `OnDestroy()`에 트윈 배열 2종 정리 로직 추가
- [ ] 테스트 시나리오 8개 검증(특히 #6~#7: 해상도별/에디터 리사이즈 시 탭 바가 Bottom+Stretch로 정상적으로 꽉 차는지, 선택 상태 복구 확인, #8: 필드명 변경 후 씬 참조 보존 확인)
- [ ] (추후) `_selectedYOffset`/`_iconScale`/아이콘 색 상수값 실제 비주얼 검수 후 디자인 피드백에 맞춰 조정
