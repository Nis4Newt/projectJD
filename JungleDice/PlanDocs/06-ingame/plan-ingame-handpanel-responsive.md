# InGame hand 패널 화면비 대응 계획

> 상위 문서: 없음 (독립적 UI 개선 요청 — 기존 6단계 로드맵에서 파생된 항목이 아님)
> 관련 문서: [핸드/필드 배치 계획](plan-ingame-handfield.md) (`hand`/`HandSlot`/`HorizontalLayoutGroup` 구조를 최초로 정의한 문서, 이번 문서는 그 구조를 그대로 두고 폭만 조정)
> 의존 관계: `JungleDice.InGame.InGameSceneManager`, `hand`의 `HorizontalLayoutGroup`(`childControlWidth: true`라 부모 폭이 줄면 내부 `HandSlot` 4개 폭도 비례 축소됨), Unity `CanvasScaler`(스케일 계산은 Unity가 자동 수행 — 코드에서 직접 참조하지 않음)
> 범위: `InGame.unity`의 `hand` 패널(`RectTransform.sizeDelta.x = 1590`) 폭을 InGame 진입 시와 이후 화면 크기가 실제로 바뀔 때마다(이벤트 기반) 재계산 — 화면 가로가 원본보다 좁으면 화면 가로에 맞춰 축소, 다시 넓어지면 원본 폭으로 복원. `HandSlot` 개별 폭 재계산 로직 신설, `hand` 외 다른 패널의 반응형 처리는 범위 밖.

---

## 배경

`InGame.unity`의 `hand` 패널은 `AnchorMin.x = AnchorMax.x = 0.5`(가로 중앙 고정)에 `sizeDelta.x = 1590`(고정폭)으로 배치돼 있다([`InGame.unity:4675-4694`](../../Assets/Scenes/InGame.unity)). `CanvasScaler`는 `Scale With Screen Size` + `Match Width Or Height = 1`(세로 기준)이라 세로는 항상 기준 해상도(2400)에 맞지만, 가로는 화면비에 따라 늘어나거나 줄어든다. 화면비가 기준(1080:2400)보다 좁은 기기(가로가 더 좁은 기기)에서는 `hand`가 캔버스 가로 폭을 넘어 잘리거나 화면 밖으로 삐져나올 수 있다.

`hand` 내부의 `GameObject (1)`은 `HorizontalLayoutGroup`(`childControlWidth: true`)을 가지고 있어([`InGame.unity:173-187`](../../Assets/Scenes/InGame.unity)), 부모(`hand`)의 폭이 줄어들면 4개 `HandSlot`의 폭도 자동으로 비례 축소된다 — 즉 `hand`의 `sizeDelta.x` 하나만 조정하면 내부 슬롯 배치는 기존 `HorizontalLayoutGroup`이 알아서 따라간다. 그래서 이번 문서는 슬롯 개별 폭을 건드리지 않고 `hand` 패널 하나의 폭만 화면에 맞춰 조정한다.

**최초 1회가 아니라 이벤트 기반으로 반응하는 이유**: 에디터 Game 뷰 리사이즈, 모바일 화면 회전, 폴더블 기기의 화면 크기 변경처럼 씬 진입 이후에도 화면 크기가 바뀌는 경우가 있다. 매 프레임 폴링(`Update()`에서 크기 비교)해도 비교 연산 자체는 비용이 거의 없지만, Unity가 이미 제공하는 `MonoBehaviour.OnRectTransformDimensionsChange()` 콜백을 쓰면 폴링 자체가 필요 없고 실제로 크기가 바뀔 때만 호출되므로 더 저렴하고 코드도 단순하다 — 그래서 이벤트 기반으로 설계한다.

---

## 설계 목표

- 씬 진입 시 최초 계산 + 이후 화면 크기가 실제로 바뀔 때마다 이벤트 기반(`OnRectTransformDimensionsChange`)으로 재계산 — 매 프레임 폴링하지 않는다
- 화면 가로(캔버스 단위 환산값)가 `hand`의 원본 폭보다 넓거나 같으면 원본 폭 그대로(또는 원본 폭으로 복원)
- 화면 가로가 원본보다 좁을 때만 `hand.sizeDelta.x`를 화면 가로 값으로 줄인다
- 원본 폭은 최초 1회 캡처해 별도로 저장 — 두 번째 리사이즈부터 "이미 줄어든 현재 값"이 아니라 항상 "원본"을 기준으로 비교해야 화면이 다시 넓어졌을 때 정확히 복원된다
- `hand` 하나의 `RectTransform`만 조작한다 — `HandSlot`/`HorizontalLayoutGroup` 쪽 코드는 건드리지 않는다(기존 `childControlWidth`에 위임)
- `GameType.Solo`/`Battle` 여부와 무관하게 항상 적용한다 — `hand` 패널 자체는 게임 모드와 무관한 UI 배치 문제이기 때문

---

## 핵심 설계 결정

### 1. "화면 가로 픽셀"은 `Screen.width`가 아니라 루트 Canvas `RectTransform.rect.width`로 비교한다

`hand`의 `sizeDelta.x = 1590`은 화면 실제 픽셀이 아니라 `CanvasScaler`가 적용된 **캔버스 단위**다. `Screen.width`(실제 픽셀)를 그대로 비교하면 단위가 달라 잘못된 비교가 된다. Screen Space - Overlay 캔버스에서 루트 `Canvas`의 `RectTransform.rect.width`는 이미 `CanvasScaler`가 스케일을 반영한 캔버스 단위 값이므로(`Match Width Or Height = 1`이면 `Screen.width / (Screen.height / 2400)`와 동일), `hand.sizeDelta.x`와 같은 단위 공간에서 바로 비교할 수 있다. 이렇게 하면 `CanvasScaler` 설정이 바뀌어도(예: 기준 해상도 변경) 코드를 손대지 않아도 항상 정합적이다.

```csharp
float screenWidthInCanvasUnits = ((RectTransform)_canvasTransform).rect.width;
```

`_canvasTransform`은 `InGameSceneManager`가 이미 `[SerializeField]`로 들고 있는 필드([`InGameSceneManager.cs:60`](../../Assets/Scripts/InGame/InGameSceneManager.cs))를 그대로 재사용한다.

### 2. 원본 폭은 최초 1회만 `_originalHandPanelWidth` 필드에 캡처한다

리사이즈가 `hand.sizeDelta.x`를 직접 덮어쓰므로, 두 번째 호출부터는 `sizeDelta.x`를 다시 읽으면 "이미 줄어든 값"을 원본으로 착각하게 된다. 그래서 `OnAwake()`에서 딱 한 번 `_originalHandPanelWidth = _handPanelRect.sizeDelta.x`로 저장해두고, 이후 모든 리사이즈 계산은 이 필드를 기준으로 한다. `1590`을 코드에 매직 넘버로 넣지 않으므로 나중에 에디터에서 원본 폭을 바꿔도 코드 수정이 필요 없다.

### 3. 이벤트 기반 감지 — `ScreenSizeChangeNotifier`를 Canvas 루트에 부착

`InGameSceneManager`는 Canvas와 다른 GameObject(`IngameSceneManager`)에 있어 `OnRectTransformDimensionsChange()`를 직접 받을 수 없다. Canvas 루트(`Canvas (1)`)에 이 콜백만 전달하는 작은 컴포넌트를 새로 붙인다.

```csharp
public class ScreenSizeChangeNotifier : MonoBehaviour
{
    public event Action OnScreenSizeChanged;

    private void OnRectTransformDimensionsChange() => OnScreenSizeChanged?.Invoke();
}
```

`hand`가 아니라 **Canvas 루트**에 붙이는 것이 중요하다 — `hand` 자신에 붙이면 우리가 `hand.sizeDelta`를 바꿀 때마다 자기 자신의 콜백이 다시 발동해 재귀 호출 위험이 생긴다. Canvas 루트의 크기는 화면 해상도에 의해서만 바뀌고 `hand`(그 자식)의 크기 변경으로는 바뀌지 않으므로, Canvas 루트에 붙이면 이런 되먹임 없이 "실제 화면 크기 변경"만 정확히 감지한다.

### 4. 리사이즈 로직은 원본과 현재 화면을 비교해 `Mathf.Min`으로 목표 폭을 정하고, 변경이 있을 때만 리빌드

```csharp
private void ResizeHandPanelToScreen()
{
    float screenWidthInCanvasUnits = ((RectTransform)_canvasTransform).rect.width;
    float targetWidth = Mathf.Min(_originalHandPanelWidth, screenWidthInCanvasUnits);

    if (Mathf.Approximately(_handPanelRect.sizeDelta.x, targetWidth)) return; // 변경 없음 — 불필요한 리빌드 방지

    _handPanelRect.sizeDelta = new Vector2(targetWidth, _handPanelRect.sizeDelta.y);
    LayoutRebuilder.ForceRebuildLayoutImmediate(_handPanelRect); // HandSlot 위치를 즉시 재계산
}
```

`Mathf.Min`이라 화면이 원본보다 넓으면 `targetWidth == _originalHandPanelWidth`로 자동 복원되고, 좁으면 화면 폭으로 축소된다 — 방향 분기(if/else)를 따로 두지 않는다. `Mathf.Approximately` 가드는 `OnRectTransformDimensionsChange`가 크기 변화 없이도 호출될 수 있는 경우(예: 컴포넌트 활성화 시점)에 대비한 방어다.

`LayoutRebuilder.ForceRebuildLayoutImmediate`가 필요한 이유: `OnAwake() → StartMatch() → EnterPhase(PlayFriend) → DrawHandCards() → StartCoroutine(DrawHandCardsRoutine)`은 같은 프레임 안에서 동기적으로 실행되며, 코루틴은 첫 `yield`(`WaitForSeconds`) 전까지 즉시 실행된다([`InGameSceneManager.cs:129-135, 202-213`](../../Assets/Scripts/InGame/InGameSceneManager.cs)). 즉 최초 리사이즈 직후 첫 카드의 `MoveToSlot` 목표 위치(`slot.transform.position`)가 같은 프레임에 읽히므로, 강제로 즉시 리빌드하지 않으면 리사이즈 이전 위치로 날아간다.

### 5. `OnAwake()`에서 원본 캡처 → 최초 리사이즈 → 이벤트 구독

```csharp
protected override void OnAwake()
{
    _originalHandPanelWidth = _handPanelRect.sizeDelta.x; // 최초 1회만 캡처
    ResizeHandPanelToScreen();
    _screenSizeChangeNotifier.OnScreenSizeChanged += ResizeHandPanelToScreen;

    _subs.Add(EventBus.Subscribe<GameStateChanged>(OnGameStateChanged));

    if (GameSession.CurrentGameType != GameType.Solo) return; // Battle 모드는 범위 밖
    ...
```

`hand` 패널 크기 조정은 게임 모드와 무관한 화면 배치 문제이므로, Solo 전용 로직(`SetupDecks()` 등)보다 먼저, 그리고 그 로직의 조기 리턴과 무관하게 항상 실행되게 한다. 이벤트 구독 해제는 별도로 하지 않는다 — `InGameSceneManager`(`IngameSceneManager` GameObject)와 `ScreenSizeChangeNotifier`(Canvas 루트)는 둘 다 InGame 씬 생명주기에 묶여 있어 씬 전환 시 함께 파괴되고, 기존 코드도 `_actionButton.onClick.AddListener` 등 씬 내부 리스너를 명시적으로 해제하지 않는 관례를 따른다.

---

## 클래스 구조

```
ScreenSizeChangeNotifier : MonoBehaviour              (신규, InGame/)
└── OnScreenSizeChanged : event Action                ← OnRectTransformDimensionsChange()에서 발행

InGameSceneManager (기존 파일 수정, InGame/)
├── _handPanelRect : RectTransform [SerializeField]              ← 신규, "hand" 오브젝트의 RectTransform
├── _screenSizeChangeNotifier : ScreenSizeChangeNotifier [SerializeField]  ← 신규, Canvas 루트에 부착된 인스턴스
├── _originalHandPanelWidth : float                    ← 신규, private, OnAwake()에서 최초 1회 캡처
├── ResizeHandPanelToScreen()                          ← 신규, private, 원본/현재 화면 비교 후 Mathf.Min으로 리사이즈
└── OnAwake()                                          ← 맨 앞에 원본 캡처 + ResizeHandPanelToScreen() 호출 + 이벤트 구독 3줄 추가
```

---

## 파일 구성

```
Assets/Scripts/
└── InGame/
    ├── ScreenSizeChangeNotifier.cs   ← 신규
    └── InGameSceneManager.cs         ← 기존 파일 수정 (필드 3개 + 메서드 1개 추가, OnAwake() 세 줄 추가)
```

`HandSlot.cs` 등 다른 기존 컴포넌트는 건드리지 않는다.

---

## Unity 씬/오브젝트 구성

```
[Scene: InGame.unity, Canvas (1) 하위]
├── Interface > hand                       ← _handPanelRect로 연결 (sizeDelta.x = 1590인 그 RectTransform 자체)
└── Canvas (1) (루트)                       ← ScreenSizeChangeNotifier.cs 신규 부착

[IngameSceneManager GameObject]
└── InGameSceneManager.cs
    ├── _handPanelRect            ← 위 hand 트랜스폼 (Inspector에서 신규 연결)
    └── _screenSizeChangeNotifier ← Canvas (1)에 부착한 ScreenSizeChangeNotifier (Inspector에서 신규 연결)
```

`hand` 내부의 `GameObject (1)`이나 `HandSlot` 4개는 인스펙터 연결이 필요 없다 — `HorizontalLayoutGroup`이 자동으로 따라간다.

---

## 이번 범위에서 제외

- `HandSlot` 개별 폭 재계산 로직 — `HorizontalLayoutGroup`의 `childControlWidth`가 이미 처리하므로 별도 코드 불필요
- `hand` 외 다른 패널(필드, 옵션 패널 등)의 반응형 처리 — 이번 요청은 hand 패널 하나로 한정됨. `ScreenSizeChangeNotifier`는 재사용 가능하지만, 다른 패널에 연결하는 건 실제 요구가 생겼을 때 별도 문서로 다룬다
- 화면이 매우 좁아 `HandSlot` 카드 간 겹침이 심해지는 시각적 보정(예: `_spacing` 동적 조정) — 패널 폭 조정만 다루고, 카드 겹침 정도 자체는 `HorizontalLayoutGroup`의 기존 동작(고정 `spacing: -40`)에 맡긴다

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 화면 가로(캔버스 단위)가 원본(1590)보다 넓거나 같음 | `Mathf.Min`이 원본을 선택 → `sizeDelta.x`가 원본과 같으면 `Mathf.Approximately` 가드로 조기 리턴 |
| 화면 가로가 원본보다 좁음 | `sizeDelta.x`를 화면 가로 값으로 축소, `HandSlot` 4개는 `HorizontalLayoutGroup`이 비례 축소 |
| 화면 가로가 정확히 원본과 같음 (경계값) | `Mathf.Min` 결과가 원본과 같아 변경 없음 |
| 축소된 상태에서 화면이 다시 원본보다 넓어짐 | `Mathf.Min`이 다시 원본을 선택 → `sizeDelta.x`가 원본으로 복원 |
| `OnRectTransformDimensionsChange`가 크기 변화 없이 호출됨(컴포넌트 활성화 등) | `Mathf.Approximately` 가드로 리빌드 생략 |
| `_handPanelRect`/`_screenSizeChangeNotifier` 인스펙터 연결 누락 | `NullReferenceException` — 기존 관례와 동일하게 방어 코드 없이 즉시 드러냄 |
| `GameType.Battle`로 진입 | `ResizeHandPanelToScreen()`은 `Solo` 체크보다 먼저 실행되므로 Battle 모드에서도 패널 크기는 조정됨(Battle의 hand 로직 자체는 여전히 범위 밖) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 기준 해상도(1080x2400)로 InGame 진입 | 캔버스 단위 화면 가로 ≈ 1080 < 1590 → `hand.sizeDelta.x`가 1080으로 축소, `HandSlot` 4개 폭이 비례 축소 |
| 2 | 화면비가 더 넓은 기기(예: 태블릿 비율)로 진입 | 캔버스 단위 화면 가로 > 1590 → `sizeDelta.x` 그대로 1590 유지 |
| 3 | 시나리오 1 상태에서 유저 선공 첫 `PlayFriend` 진입 | `DrawHandCardsRoutine`의 첫 카드가 리사이즈된(축소된) `HandSlot[0]` 위치로 정확히 이동(리사이즈 이전 위치로 날아가지 않음) |
| 4 | `GameType.Battle`로 InGame 진입 | `hand.sizeDelta.x`가 화면 가로에 맞게 조정됨(이후 Battle 전용 hand 로직은 여전히 미구현이라 별도 검증 불필요) |
| 5 | 에디터 Game 뷰에서 InGame 진입 후 창을 좁게 드래그 | `OnScreenSizeChanged` 이벤트가 발동해 `hand.sizeDelta.x`가 즉시 축소(폴링 없이 반응) |
| 6 | 시나리오 5 상태에서 창을 다시 원래 크기 이상으로 넓힘 | `hand.sizeDelta.x`가 원본(1590)으로 정확히 복원 |
| 7 | 화면 크기 변화 없이 `OnRectTransformDimensionsChange`가 여러 번 호출되는 상황(연속 프레임 관찰) | `Mathf.Approximately` 가드로 `LayoutRebuilder` 재호출 없음(불필요한 리빌드로 인한 스터터 없는지 확인) |

---

## 구현 시 주의사항

- `LayoutRebuilder.ForceRebuildLayoutImmediate(_handPanelRect)`를 빠뜨리면 리사이즈 첫 프레임에 `DrawHandCards()`가 읽는 `slot.transform.position`이 이전 레이아웃 값이라 첫 카드가 잘못된 위치로 날아간다.
- `_handPanelRect`는 `hand`(`sizeDelta.x=1590`) 오브젝트 자체를 가리켜야 한다 — 내부 `GameObject (1)`을 잘못 연결하면 안 된다.
- `_originalHandPanelWidth`는 `OnAwake()`에서 **딱 한 번만** 캡처한다 — `ResizeHandPanelToScreen()` 내부에서 매번 `sizeDelta.x`를 다시 읽으면 이미 축소된 값을 원본으로 착각해 화면이 넓어져도 복원되지 않는다.
- `ScreenSizeChangeNotifier`는 반드시 **Canvas 루트**에 붙인다 — `hand`에 붙이면 우리가 `sizeDelta`를 바꿀 때 자기 콜백이 재귀적으로 다시 발동할 수 있다.
- `ResizeHandPanelToScreen()`은 `OnAwake()` 맨 앞, `GameSession.CurrentGameType` 체크보다 먼저 호출한다 — Solo 전용 조기 리턴과 무관하게 항상 실행되어야 한다.
- `Screen.width`를 직접 쓰지 않는다 — `hand.sizeDelta.x`와 단위가 다르다(캔버스 단위 vs 실제 픽셀). 반드시 루트 Canvas의 `RectTransform.rect.width`로 비교한다.
- `Mathf.Approximately` 가드를 빠뜨리면 크기 변화 없는 `OnRectTransformDimensionsChange` 호출에도 매번 `LayoutRebuilder.ForceRebuildLayoutImmediate`가 돌아 불필요한 비용이 생긴다.

---

## 구현 후 체크리스트

- [x] `ScreenSizeChangeNotifier.cs` 작성 (`Assets/Scripts/InGame/`)
- [x] `InGameSceneManager.cs`: `_handPanelRect`/`_screenSizeChangeNotifier` `[SerializeField]` + `_originalHandPanelWidth` 필드 추가
- [x] `ResizeHandPanelToScreen()` private 메서드 추가, `OnAwake()`에 원본 캡처 + 최초 호출 + 이벤트 구독 추가
- [ ] `InGame.unity`: `Canvas (1)`에 `ScreenSizeChangeNotifier` 부착, `IngameSceneManager` 인스펙터에 `hand`의 `RectTransform`과 위 notifier 연결 (Unity 에디터 작업 — 아직 미완료, 아래 참고)
- [ ] 에디터 Game 뷰에서 여러 해상도(기준 해상도, 더 넓은 화면비, 더 좁은 화면비)로 테스트, 창 크기를 드래그하며 실시간 반응 확인
- [ ] 테스트 시나리오 7개 검증 (특히 #3: 리사이즈 직후 첫 카드 드로우 위치, #6: 축소 후 복원)
- [ ] (추후) `hand` 외 다른 패널에도 반응형이 필요해지면 `ScreenSizeChangeNotifier` 재사용해 별도 문서로 확장
