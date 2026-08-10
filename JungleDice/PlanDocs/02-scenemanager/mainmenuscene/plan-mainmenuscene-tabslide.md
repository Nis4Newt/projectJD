# 메인메뉴 슬라이드/탭 구현 계획

> 상위 문서: [씬별 매니저 구현 계획](../plan-scenemanager.md) (`MainMenuSceneManager` 스켈레톤이 예견해 둔 MainMenu 씬 전용 UI 로직 중, 탭/슬라이드 네비게이션에서 파생)
> 의존 관계: 없음 (`MainMenuSceneManager`, `GameManager` 등 씬/전역 시스템을 참조하지 않는 독립 UI 컴포넌트)
> 범위: `ScrollRect`(Horizontal Only) 기반 4페이지 슬라이드 + 탭 4개 클릭/스와이프 네비게이션, 기기별 해상도/화면비에 대응하는 페이지 폭 계산까지. 각 페이지 내부 콘텐츠(상점, 친구 목록 등)와 실기기에서의 세션 중 실시간 화면비 변경 대응은 범위 밖(아래 "이번 범위에서 제외" 참고).

---

## 배경 / 문제 인식

`MainMenuSceneManager`는 빈 스켈레톤 상태라 MainMenu 씬에 실제 UI 로직이 없다. 씬에는 프로토타입으로 `Scroll View`(`ScrollRect`, `m_Horizontal: 1` / `m_Vertical: 0`, `MovementType: Elastic`)와 `Content`(자식 4개, 고정 크기 `4320x2400` = 페이지당 `1080` 폭 가정)가 배치돼 있으나(`MainMenu.unity`, 미커밋), 두 가지가 빠져 있다:

1. Unity 기본 `ScrollRect`는 자유 스크롤만 지원하고 "페이지 경계에 딱 붙는" 스냅 동작이 없다.
2. `CanvasScaler`가 `Constant Pixel Size`(참조 해상도 `800x600`)로 설정돼 있고, `Content`의 페이지 폭도 `1080` 고정값이라 기기마다 다른 화면비에서 페이지 경계와 스크롤 위치 계산이 어긋난다.

`ProjectSettings.asset`상 `defaultScreenOrientation: 4`(AutoRotation) + `allowedAutorotateTo*` 4방향 전부 허용 상태이나, 실제 게임 정책은 **단일 방향 고정**이다(프로젝트 생성 시 방치된 기본값). 따라서 세션 도중 화면비가 바뀌는 경우(기기 회전)는 실제로 발생하지 않는다 — 다만 기기마다 최초 실행 시의 화면비 자체는 제각각이므로 페이지 폭은 뷰포트 기준으로 계산해야 한다.

---

## 설계 목표

- `ScrollRect` 하나로 4페이지 좌우 슬라이드, 세로 스크롤은 사용하지 않음
- 탭 4개 중 하나를 클릭하면 해당 페이지로 부드럽게 이동
- 스와이프하면 가장 가까운 페이지로 스냅(페이지 중간에 걸쳐서 멈추지 않음)
- 탭 하이라이트와 현재 페이지가 어느 입력 경로로 이동해도 항상 일치
- 기기마다 다른 해상도/화면비에서도 최초 실행 시 페이지 폭이 뷰포트에 정확히 맞음
- 새 패턴을 발명하지 않고 기존 관례 재사용: 코루틴 기반 애니메이션(DOTween 등 외부 트윈 라이브러리 미도입 — `Packages/manifest.json` 확인 결과 없음), `Button.onClick`, `CompositeDisposable`

---

## 핵심 설계 결정

### 1. `ScrollRect`: Horizontal Only 유지, 스냅은 별도 스크립트로 구현

기본 `ScrollRect`엔 페이지 스냅 기능이 없으므로, 프로토타입 설정(`m_Horizontal: 1`, `m_Vertical: 0`, `MovementType: Elastic`)은 그대로 두고 `OnEndDrag` 시점에 목표 `anchoredPosition`을 코드로 계산해 코루틴으로 Lerp 이동시킨다.

`MainMenuTabSlideController`는 **`ScrollRect`와 같은 GameObject(`ScrollView`)에 붙인다.** Unity UI의 드래그 이벤트는 포인터 다운 지점에서 상위로 올라가며 핸들러가 있는 첫 GameObject에서만 처리되는데, `ScrollRect` 자신이 이미 `IEndDragHandler`를 구현하므로 컨트롤러를 `ScrollView`의 부모 오브젝트에 두면 `ScrollRect`가 먼저 가로채 컨트롤러의 `OnEndDrag`가 아예 호출되지 않는다. `[RequireComponent(typeof(ScrollRect))]`로 강제하고 `GetComponent<ScrollRect>()`로 가져와 오배치를 방지한다.

```csharp
[RequireComponent(typeof(ScrollRect))]
public class MainMenuTabSlideController : MonoBehaviour, IEndDragHandler
{
    [SerializeField] private RectTransform[] _pages;   // Content 자식, 순서 = 페이지 순서
    [SerializeField] private Button[] _tabButtons;      // _pages와 1:1 대응, 씬 다른 위치(Tabs)에 있어도 인스펙터 참조로 연결

    private ScrollRect _scrollRect; // Awake에서 GetComponent로 획득 (같은 GameObject)
}
```

### 2. 페이지 폭: Viewport 기준으로 `Awake()` 1회 계산

`Content` 크기(`4320x2400`)와 페이지 폭(`1080`) 같은 고정 픽셀 값 대신, 각 페이지(`RectTransform`)에 `LayoutElement`를 붙이고 `preferredWidth = _scrollRect.viewport.rect.width`를 `Awake()`에서 읽어 대입한다. `Content`는 `HorizontalLayoutGroup`(spacing 0, `Control Child Size: Width` + `Height` 모두 체크)으로 자식을 순서대로 배치한다 — `Width`만 체크하면 폭은 스크립트 값대로 맞춰지지만 높이는 그대로 남아 `Content`/`Viewport` 높이와 안 맞을 수 있으므로 `Height`도 반드시 함께 켠다. 페이지의 실제 높이 값은 `Content`가 `Viewport`와 상하로 앵커 스트레치돼 있으면 자동으로 뷰포트 높이를 따라가므로 별도로 입력할 필요가 없다.

`HorizontalLayoutGroup`은 자식 배치만 계산할 뿐 `Content` 자신의 RectTransform 크기는 늘려주지 않으므로, `Content`에 `Content Size Fitter`(`Horizontal Fit: Preferred Size`)를 추가해 전체 폭이 페이지 폭 합(`viewport 폭 × 4`)으로 자동으로 맞춰지게 한다 — 이게 없으면 `ScrollRect`가 스크롤 가능한 범위 자체를 잘못 계산한다.

방향 고정 정책상 세션 중 화면비가 바뀌지 않으므로 재계산은 최초 1회로 충분하다 — `Update` 등 매 프레임 폴링은 추가하지 않는다.

`CanvasScaler`(Scale With Screen Size)의 스케일 적용은 해당 프레임이 렌더링되기 직전(`Canvas.willRenderCanvases`)에 처리되는데, `Awake()`는 그보다 먼저 실행되므로 `Awake()` 시점에 `viewport.rect.width`를 그냥 읽으면 아직 스케일이 반영되기 전의 값일 수 있다. `Awake()` 맨 앞에서 `Canvas.ForceUpdateCanvases()`를 호출해 그 적용을 강제로 먼저 끝낸 뒤 `RecalculatePageWidths()`를 호출한다.

### 3. 탭 ↔ 스크롤 동기화: 단일 진실 소스는 "현재 페이지 인덱스"

탭 클릭과 스와이프 둘 다 같은 내부 상태(`CurrentPage`)를 갱신하는 단일 진입점 `SetPage(int index)`로 합류시킨다 — `Friend.SetKey(int key)`(`plan-prefab.md`)가 이미지/att/hp를 각각 다른 메서드로 쪼개지 않은 것과 같은 이유로, 입력 경로가 여러 개라도 상태 반영 진입점은 하나만 둬서 "탭은 A인데 스크롤은 B 페이지" 같은 불일치를 원천 차단한다.

```csharp
public void SetPage(int index)
{
    index = Mathf.Clamp(index, 0, _pages.Length - 1);
    CurrentPage = index;

    UpdateTabHighlights(index);

    if (_snapRoutine != null) StopCoroutine(_snapRoutine);
    _snapRoutine = StartCoroutine(SnapToPageRoutine(index));
}

private void OnTabButtonClicked(int index) => SetPage(index);

public void OnEndDrag(PointerEventData eventData)
{
    float flickThreshold = _scrollRect.viewport.rect.width * _flickVelocityRatio;
    int target = Mathf.Abs(_scrollRect.velocity.x) >= flickThreshold
        ? CurrentPage + (_scrollRect.velocity.x < 0 ? 1 : -1) // 빠른 플릭: 드래그 거리와 무관하게 한 페이지 이동
        : CalculateNearestPageIndex();

    SetPage(target);
}
```

- 탭 클릭 → `SetPage(index)` 직접 호출
- 스와이프 종료(`OnEndDrag`) → 기본은 현재 `Content.anchoredPosition`에서 가장 가까운 페이지 인덱스를 계산해 `SetPage(index)` 호출
- **빠른 플릭(flick) 예외**: `ScrollRect.velocity.x`(드래그 속도, `ScrollRect`가 관성 계산용으로 이미 들고 있는 값)의 절댓값이 `viewport 폭 × _flickVelocityRatio`를 넘으면, 드래그 거리가 페이지 폭의 절반이 안 되더라도 `CurrentPage ± 1`로 한 페이지 이동시킨다 — 짧고 빠르게 튕기듯 스와이프해도 다음/이전 페이지로 넘어가는 모바일 UX 관례. 임계값을 뷰포트 폭의 배수로 잡아 해상도와 무관하게 일관되게 동작한다(고정 픽셀/초 값으로 두지 않음).
- 두 경로 모두 같은 메서드를 통과하므로 하이라이트 갱신과 스크롤 이동이 항상 함께 일어남

`SnapToPageRoutine`은 매 프레임 `_scrollRect.velocity = Vector2.zero`로 `ScrollRect` 자체의 관성(inertia) 스크롤을 무력화한다 — 그렇지 않으면 스와이프 종료 직후 `ScrollRect`가 자체 관성으로 계속 움직이려는 것과 우리 코루틴의 `anchoredPosition` 대입이 매 프레임 충돌해, 스냅이 끝난 뒤에도 페이지가 한 위치에 딱 고정되지 않고 미세하게 계속 움직이는 증상이 생긴다.

### 4. EventBus 미사용 — 컴포넌트 내부에 닫힌 상호작용

탭-페이지 동기화는 이 프리팹 하나 안에서만 일어나는 상호작용이고, 다른 씬 매니저나 전역 시스템이 현재 탭을 알아야 할 필요가 현재 없다. `EventBus`를 발행하지 않고 `MonoBehaviour` 내부 상태로 닫는다. 다른 시스템이 필요해지면 `plan-eventbus.md` 패턴 그대로 `public record MainMenuTabChanged(int Index);`를 추가한다.

### 5. 스와이프 판정은 `GameEvents.cs`의 기존 `SwipeEvent`를 재사용하지 않는다

`GameEvents.cs`에 `public record SwipeEvent(Vector2 Direction, float Speed);`가 정의돼 있지만 발행 주체가 없다(범용 입력 이벤트로 예약된 것으로 보임). 탭 슬라이드는 `IEndDragHandler.OnEndDrag`의 드래그 델타만으로 판단 가능하므로 로컬로 처리하고, 다른 시스템이 이 스와이프를 알아야 할 이유가 생기면 그때 연결한다.

### 6. 에디터 전용: Play 중 Game 뷰 리사이즈 대응 (`#if UNITY_EDITOR`)

실기기는 방향 고정이라 세션 중 리사이즈가 없지만, 에디터의 Game 뷰는 Play 중에도 자유롭게 리사이즈되므로 여러 해상도를 빠르게 확인할 수 있도록 에디터 전용 경로를 추가한다. 실기기 정책엔 없는 경로이므로 빌드에는 포함되지 않게 `#if UNITY_EDITOR`로 감싼다.

```csharp
#if UNITY_EDITOR
private void OnRectTransformDimensionsChange()
{
    if (!isActiveAndEnabled || !Application.isPlaying) return; // 씬 편집/비활성 상태 호출 방어
    RecalculatePageWidths();
    if (_snapRoutine != null) { StopCoroutine(_snapRoutine); _snapRoutine = null; }
    _scrollRect.content.anchoredPosition = CalculateTargetPosition(CurrentPage); // Lerp 없이 즉시 반영
}
#endif
```

- `CalculateTargetPosition(int index)`는 `SnapToPageRoutine`의 Lerp 목표 계산과 같은 로직을 공유하는 `private` 헬퍼로 뽑아, 코루틴 경로와 에디터 즉시대입 경로의 계산이 어긋나지 않게 한다.
- `Application.isPlaying` 가드는 이중 방어용 — `[ExecuteAlways]` 없는 일반 `MonoBehaviour`는 Play 중이 아니면 이 콜백 자체가 오지 않지만, 방어적으로 남긴다.
- 빌드에서는 이 블록 자체가 컴파일되지 않으므로 프로덕션 코드 경로(`Awake()` 1회 계산)에 영향을 주지 않는다.

---

## 이번 범위에서 제외

- **실기기(빌드)에서의 세션 중 실시간 화면비/해상도 변경 대응**: 화면 회전 정책이 단일 방향 고정이라 빌드에서 실행 중 화면비가 바뀌는 시나리오가 없다. 추후 정책이 자동 회전 지원으로 바뀌면 에디터 전용 로직(#6)을 `#if UNITY_EDITOR` 밖으로 꺼내 정식 도입 검토.
- **Player Settings의 `Default Orientation` 고정 작업 자체**: 씬/빌드 설정 작업이라 범위 밖(구현 후 체크리스트에만 항목으로 남김).
- **`CanvasScaler` 참조 해상도 확정**: 프로젝트 디자인 기준 해상도 확정은 씬 작업 시 별도 결정.

---

## 클래스 구조

```
MainMenuTabSlideController : MonoBehaviour, IEndDragHandler   (신규, MainMenu/, [RequireComponent(typeof(ScrollRect))])
├── _pages : RectTransform[4]           ← [SerializeField], Content 자식(페이지 순서)
├── _tabButtons : Button[4]             ← [SerializeField], _pages와 1:1 인덱스 대응
├── _tabHighlights : GameObject[4]      ← [SerializeField], 선택된 탭 표시용
├── _snapDuration : float = 0.25f       ← [SerializeField]
├── _flickVelocityRatio : float = 3f    ← [SerializeField], 초당 뷰포트 폭의 배수(플릭 판정 임계값)
├── _scrollRect : ScrollRect            ← private, Awake에서 GetComponent(같은 GameObject)
├── CurrentPage : int { get; private set; }
├── Awake()                              ← private, 배열 길이 검증 + 탭 onClick 연결 + Canvas.ForceUpdateCanvases() + 페이지 폭 최초 1회 계산
├── SetPage(int index)                  ← 공개 진입점 유일. 하이라이트 갱신 + 스냅 이동 트리거
├── OnEndDrag(PointerEventData)         ← IEndDragHandler, 플릭 속도 초과 시 CurrentPage±1, 아니면 가장 가까운 페이지 계산 후 SetPage 호출
├── RecalculatePageWidths()             ← private, LayoutElement.preferredWidth = viewport.rect.width
├── CalculateNearestPageIndex()         ← private, Content.anchoredPosition 기준 반올림
├── CalculateTargetPosition(int index)  ← private, 페이지 인덱스 → 목표 anchoredPosition (Snap/에디터 즉시대입 공용)
├── SnapToPageRoutine(int index)        ← private 코루틴, CalculateTargetPosition으로 Lerp
└── OnRectTransformDimensionsChange()   ← private, #if UNITY_EDITOR 전용. 에디터 Play 중 리사이즈 시 즉시 재배치
```

---

## 파일 구성

```
Assets/
└── Scripts/
    └── MainMenu/
        └── MainMenuTabSlideController.cs   ← 신규
```

`MainMenuSceneManager.cs`와 마찬가지로 `MainMenu/` 아래 배치 — 이 씬에만 등장하는 구체적인 UI 컴포넌트이기 때문(`plan-prefab.md`가 `Friend.cs`를 `Core/`가 아닌 `InGame/`에 둔 것과 동일 원칙).

---

## Unity 씬/오브젝트 구성

```
[Scene: MainMenu]
└── Canvas (CanvasScaler: Scale With Screen Size로 전환됨)
    └── TabSlide (GameObject, 순수 레이아웃 컨테이너)
        ├── Tabs (RectTransform, HorizontalLayoutGroup)
        │   ├── Tab_0 (Button) ~ Tab_3 (Button)
        │   └── (각 Tab 하위에 선택 표시용 Highlight 오브젝트)
        └── ScrollView (ScrollRect, Horizontal Only, MainMenuTabSlideController.cs ← 반드시 이 오브젝트)
            └── Viewport
                └── Content (HorizontalLayoutGroup spacing 0 + Content Size Fitter: Horizontal Fit = Preferred Size)
                    ├── Page_0 (RectTransform + LayoutElement) ~ Page_3
```

`MainMenuTabSlideController`는 `TabSlide`가 아니라 `ScrollRect`가 붙어있는 `ScrollView` 오브젝트에 배치한다(위 "핵심 설계 결정 1" 참고). `_tabButtons`/`_tabHighlights`는 인스펙터에서 `Tabs` 하위 오브젝트를 드래그해 연결하면 되므로 계층 구조상 떨어져 있어도 문제없다.

현재 씬에 프로토타입으로 흩어져 있는 `Scroll View` / `Content` / `t (1)`~`t (4)` / `Image (1)`~`(7)` 등은 실험용 배치이며, 실제 구현 시 위 구조로 정리한다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 스와이프 거리가 짧아 페이지 경계를 못 넘음 (플릭 속도 미만) | `CalculateNearestPageIndex()`가 원래 페이지를 반환 → 결과적으로 원위치 스냅 |
| 드래그 거리는 짧지만 빠르게 플릭함 (속도가 임계값 이상) | 거리 계산을 건너뛰고 `CurrentPage ± 1`로 강제 이동 — `SetPage`의 `Mathf.Clamp`로 범위 밖 방지 |
| 마지막 페이지에서 다음 방향으로 플릭 (더 이상 넘어갈 페이지 없음) | `CurrentPage + 1`이 범위를 벗어나지만 `SetPage`가 clamp하여 그대로 마지막 페이지 유지, 예외 없음 |
| 스냅 애니메이션 도중 다른 탭 클릭/새 스와이프 시작 | `SetPage` 진입 시 기존 `_snapRoutine` 즉시 `StopCoroutine` 후 새 목표로 재시작 |
| 첫/마지막 페이지에서 바깥쪽으로 계속 드래그 | `MovementType: Elastic`이 저항감 처리, `OnEndDrag`에서 `Mathf.Clamp(0, pages.Length-1)`로 범위 내 스냅 |
| `CanvasScaler`가 `Constant Pixel Size`로 방치된 채 이 기능만 적용 | 페이지 폭 계산은 `Viewport.rect.width` 기준이라 탭/슬라이드는 동작하지만, 다른 UI 요소는 해상도별로 계속 어긋남 — `Scale With Screen Size` 전환 선행 필요 |
| Player Settings의 `Default Orientation`이 `Auto Rotation`으로 남아있음 | 방향 고정이 전제인 설계라 실제로 회전이 허용되면 페이지 폭이 회전 후 어긋남 — 구현 전 방향 고정 확인 필수 |
| `_pages`/`_tabButtons`/`_tabHighlights` 길이가 서로 불일치 | 인덱스 매칭이 깨지므로 `Awake()`에서 세 배열 길이가 다르면 `Debug.LogError` 후 초기화 중단 |
| `MainMenuTabSlideController`가 `ScrollView`가 아닌 다른(부모) 오브젝트에 배치됨 | `ScrollRect`가 드래그 이벤트를 먼저 가로채 `OnEndDrag`가 호출되지 않음 — 반드시 `ScrollRect`와 같은 GameObject에 배치(`[RequireComponent(typeof(ScrollRect))]`로 강제) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | Tab_2 클릭 | `CurrentPage == 2`, `Content` 애니메이션으로 Page_2 중앙 정렬, Tab_2 하이라이트만 활성 |
| 2 | Page_0에서 오른쪽으로 절반 이상 스와이프 후 손 뗌 | `CalculateNearestPageIndex()`가 1 반환 → `SetPage(1)` 호출, Tab_1 하이라이트로 전환 |
| 3 | Page_0에서 살짝만 드래그(페이지 폭의 절반 미만) 후 손 뗌 | 원래 페이지(0)로 스냅 복귀 |
| 3-1 | Page_0에서 페이지 폭의 10%만 드래그했지만 임계값 이상 빠르게 플릭 후 손 뗌 | `CalculateNearestPageIndex()`는 0을 반환할 거리지만, 플릭 속도가 임계값을 넘어 `SetPage(1)`이 호출됨 |
| 4 | Tab_3 클릭 애니메이션 도중 Tab_0 클릭 | 기존 스냅 코루틴 중단, Page_0로 재시작, 최종적으로 `CurrentPage == 0` |
| 5 | 해상도 프리셋이 다른 두 기기(또는 에디터 Game 뷰 프리셋)에서 각각 Play | 두 경우 모두 `Awake()` 시점 `Viewport.rect.width` 기준으로 페이지 폭이 뷰포트에 정확히 맞음 |
| 6 | Page_3(마지막)에서 오른쪽으로 계속 드래그 | Elastic 저항 후 손을 떼면 `CurrentPage`가 3에서 벗어나지 않음(clamp) |
| 7 | (에디터 전용) Play 중 Page_2를 보고 있는 상태에서 Game 뷰를 드래그로 리사이즈 | 각 페이지 `LayoutElement.preferredWidth` 즉시 갱신, `Content`가 새 폭 기준 Page_2 위치로 순간 재배치, `CurrentPage`는 2 그대로 유지 |
| 8 | 빌드(Development Build 등)에 `#if UNITY_EDITOR` 블록이 포함되지 않는지 확인 | 빌드에서 창 크기를 조절해도 재배치 로직이 동작하지 않음 — `resizableWindow: 0` 설정과 함께 실기기 동작이 `Awake()` 1회 계산 그대로임을 확인 |

---

## 구현 시 주의사항

- **Player Settings의 `Default Orientation`을 실제 정책(단일 방향)으로 고정 필수**: 현재 `Auto Rotation` + 4방향 전체 허용으로 방치돼 있음. 방향 고정이 선행되지 않으면 회전 시 페이지 폭이 어긋난다.
- **`CanvasScaler`를 `UI Scale Mode: Scale With Screen Size`로 전환 필수**: 현재 `Constant Pixel Size`(800x600) 방치 상태로는 해상도 대응이 안 됨.
- **페이지 폭은 하드코딩 픽셀 값(현재 프로토타입의 `4320`/`1080`)으로 두지 않는다** — `LayoutElement.preferredWidth`를 `Viewport.rect.width`에서 `Awake()` 시점에 읽어온다.
- **`Content`에 `Content Size Fitter`(Horizontal Fit: Preferred Size) 추가를 빠뜨리지 않는다** — `HorizontalLayoutGroup`만으로는 `Content` 자신의 폭이 자동으로 늘어나지 않아 `ScrollRect`의 스크롤 범위가 잘못 계산된다.
- **`HorizontalLayoutGroup`의 `Control Child Size`는 `Width`/`Height` 둘 다 체크한다** — `Width`만 켜면 폭은 맞아도 페이지 높이가 `Content`/`Viewport` 높이와 어긋날 수 있다.
- **`LayoutElement.preferredWidth`를 바꾼 뒤에는 `LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollRect.content)`로 즉시 리빌드를 강제한다** — 그냥 두면 Unity가 프레임 끝 무렵에야 반영하므로, 바로 이어서 `CalculateTargetPosition` 등이 새 크기를 전제로 계산하는 코드와 타이밍이 어긋날 수 있다.
- **`Awake()` 맨 앞에서 `Canvas.ForceUpdateCanvases()`를 호출한다** — `CanvasScaler`의 스케일 적용이 `Awake()`보다 늦게 처리되므로, 이게 없으면 최초 실행 시 `viewport.rect.width`를 잘못된(스케일 적용 전) 값으로 읽어 첫 페이지 크기/위치가 어긋난다. 이후 탭 클릭·리사이즈로 재계산될 때만 우연히 맞아 보이는 식으로 증상이 가려질 수 있어 발견하기 쉽지 않다.
- **에디터 전용 리사이즈 대응(`OnRectTransformDimensionsChange`)은 반드시 `#if UNITY_EDITOR`로 감싼다** — 스냅 목표 위치 계산은 `CalculateTargetPosition(int)` 하나로 공유해 코루틴 경로와 에디터 즉시대입 경로의 계산이 어긋나지 않게 한다.
- **DOTween 등 외부 트윈 라이브러리 없음** — 스냅 애니메이션은 `LoginTapToContinueUI`의 `BlinkRoutine`처럼 코루틴 기반 `Lerp`로 구현(프로젝트 전역 관례).
- **`MainMenuTabSlideController`는 반드시 `ScrollRect`와 같은 GameObject(`ScrollView`)에 붙인다** — 부모 오브젝트에 두면 `ScrollRect`가 드래그 이벤트를 먼저 가로채 `OnEndDrag`가 호출되지 않는다(`[RequireComponent(typeof(ScrollRect))]`로 컴파일 타임에 어느 정도 방지되지만, 배치 자체는 여전히 직접 확인 필요).
- **탭 인덱스와 `Content` 자식 순서(페이지 순서)가 항상 일치해야 한다** — 배열 인덱스로만 매칭하므로 `_pages`/`_tabButtons` 배열 순서가 실제 씬 계층 순서와 어긋나지 않게 주의.
- **`EventBus`를 새로 끌어들이지 않는다** — 이 컴포넌트는 자기 안에서 닫힌 상호작용이며, 필요해지기 전까지 이벤트를 발행하지 않는다.

---

## 구현 후 체크리스트

- [x] Player Settings에서 `Default Orientation`을 Portrait로 고정, `allowedAutorotateTo*`를 Portrait만 남기고 정리(`ProjectSettings.asset`)
- [x] `Canvas`의 `CanvasScaler`를 `Scale With Screen Size`로 전환, 참조 해상도 1080x1920 / Match: Height(1) 적용(`MainMenu.unity`)
- [x] `MainMenuTabSlideController.cs` 작성 (`Assets/Scripts/MainMenu/`)
- [x] `Content`에 `HorizontalLayoutGroup`(`Control Child Size: Width`+`Height` 체크) + `Content Size Fitter`(Horizontal: Preferred Size), 각 페이지에 `LayoutElement` 구성 완료
- [ ] 씬의 프로토타입 오브젝트 이름 정리(`t (1)`~`t (4)` → `Page_0`~`Page_3` 등, 현재 기능은 정상 동작하나 이름은 프로토타입 그대로) — 에디터 작업 필요
- [x] `MainMenuTabSlideController`를 `ScrollView`(ScrollRect가 붙은 오브젝트)에 부착, `_pages`/`_tabButtons`/`_tabHighlights` 인스펙터 연결
- [ ] 4개 페이지는 우선 빈 placeholder로 배치(실제 콘텐츠는 범위 밖)
- [ ] 테스트 시나리오 8개 검증(특히 #5, #7: 해상도별 Play 확인 / 에디터 Play 중 리사이즈 확인, #8: 빌드에 에디터 전용 코드 미포함 확인)
- [ ] (추후) 다른 시스템이 현재 탭 인덱스를 알아야 하면 `MainMenuTabChanged` 이벤트 추가
- [ ] (추후) 화면 회전 정책이 자동 회전 지원으로 바뀌면, 이미 만들어 둔 에디터 전용 리사이즈 로직을 `#if UNITY_EDITOR` 밖으로 꺼내 실기기 대응으로 승격 검토
