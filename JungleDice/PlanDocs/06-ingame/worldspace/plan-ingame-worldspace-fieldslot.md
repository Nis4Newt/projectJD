# FieldSlot World Space 전환 계획

> 상위 문서: [InGame 필드 World Space 전환 개요](plan-ingame-worldspace.md) (1단계)
> 의존 관계: `JungleDice.InGame.FieldSlot`, `JungleDice.InGame.WorldFriend`, `JungleDice.InGame.InGameSceneManager`, Unity `Physics2DRaycaster`/`Collider2D`
> 범위: `FieldSlot`이 붙는 씬 오브젝트를 uGUI(`RectTransform`/`Image`)에서 world space(`Transform`/`SpriteRenderer`/`Collider2D`)로 바꾸고, 드롭 판정이 계속 동작하도록 카메라에 `Physics2DRaycaster`를 추가한다. 여기에 더해 `FieldSlot`의 점유 판정을 트랜스폼 계층 탐색(`transform.childCount`/`GetComponentInChildren`)이 아니라 명시적 참조(`PlacedFriend`)로 바꾸고, `InGameSceneManager.GetFieldSlot`이 배열 순서가 아니라 `FieldSlot.Index`를 기준으로 항상 올바른 슬롯을 찾도록 한다.

---

## 배경

`FieldSlot.cs`는 원래부터 `transform`(RectTransform이 아니라 base `Transform`)과 `IDropHandler`만 쓰고 있어 RectTransform 전용 API에 의존하지 않는다. 따라서 GameObject 타입 전환(`RectTransform`/`Image` → `Transform`/`SpriteRenderer`) 자체는 **씬 작업만으로 끝나고 코드 변경이 필요 없다**.

드롭 판정은 uGUI `GraphicRaycaster`가 `Canvas` 하위만 인식하는 문제를 풀어야 한다 — 하지만 별도의 스크린→world 좌표 변환 코드를 새로 짤 필요는 없다. Unity의 `EventSystem`은 씬에 등록된 모든 `BaseRaycaster`(`GraphicRaycaster`, `Physics2DRaycaster` 등)의 히트 결과를 합쳐 거리순으로 정렬하고, `IDropHandler.OnDrop` 등 표준 이벤트를 어떤 raycaster가 찾아낸 대상이든 동일하게 호출한다. 즉 Main Camera에 `Physics2DRaycaster`를 추가하고 `FieldSlot`에 `Collider2D`를 달아주기만 하면, 드래그 중인 카드(여전히 UI)를 world space `FieldSlot` 위에 놓았을 때 기존 `FieldSlot.OnDrop`이 그대로 호출된다.

다만 점유 판정(`IsOccupied`)만은 코드 변경이 필요하다 — 기존 `transform.childCount > 0`/`GetComponentInChildren<WorldFriend>()` 방식은 슬롯 자식으로 카드가 아닌 다른 오브젝트가 들어가거나 `InGameSceneManager`가 엉뚱한 슬롯을 조회하면 조용히 어긋난다. `FieldSlot`이 자신에게 배치된 `WorldFriend`를 명시적 참조로 직접 들고 있도록 바꿔, 트랜스폼 계층을 탐색하지 않고도 항상 정확한 점유 상태를 보장한다.

---

## 설계 목표

- `FieldSlot`이 붙는 GameObject의 타입 전환은 코드 변경 없이 씬 작업만으로 끝낸다 — 이미 RectTransform 비의존
- 드롭 판정 경로 교체는 코드가 아니라 씬 설정(카메라 컴포넌트 + 슬롯 Collider2D)으로 해결
- `Index`는 그대로 유지하되, `IsOccupied`는 `PlacedFriend`(명시적 참조) 기반으로 바꿔 — [공격](../plan-ingame-attack.md)/[합체](../plan-ingame-merge.md)/[치트](../plan-ingame-cheat.md) 문서가 이미 이 인터페이스에 의존하고 있어 시그니처는 그대로 두고 내부 구현만 교체
- `InGameSceneManager.GetFieldSlot(rollValue)`가 씬의 `_fieldSlots` Inspector 등록 순서와 무관하게 항상 정확한 슬롯을 반환하도록 한다

---

## 핵심 설계 결정

### 1. `FieldSlot` — 점유 판정을 `PlacedFriend` 명시적 참조로

```csharp
public class FieldSlot : MonoBehaviour, IDropHandler
{
    [SerializeField] private int _index;

    public int Index => _index;
    public WorldFriend PlacedFriend { get; private set; }
    public bool IsOccupied => PlacedFriend != null;

    public void PlaceFriend(WorldFriend friend) => PlacedFriend = friend;
    public void RemoveFriend() => PlacedFriend = null;

    public void OnDrop(PointerEventData eventData)
    {
        var card = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<FriendCardBattleControl>() : null;
        if (card == null) return;
        InGameSceneManager.Instance.TryPlaceFriendCard(this, card);
    }
}
```

`InGameSceneManager`가 `WorldFriend`를 생성하는 모든 지점(`TryPlaceFriendCard`, `ExecuteComputerAction`, `CheatSetSlot`, `SpawnFriendDirectly`)은 `SpawnWorldFriend(FieldSlot slot)` 헬퍼 하나만 거친다 — 이 헬퍼가 `Instantiate` 직후 `slot.PlaceFriend(friend)`까지 함께 처리해, 호출부마다 배치 호출을 반복하거나 빠뜨릴 여지가 없다([WorldFriend 신설 계획](plan-ingame-worldspace-worldfriend.md) 참고). 파괴하는 지점(`CheatClearSlot`, `ApplyClausesToFriend`의 사망 처리, `TryHandleDeath`)은 `Destroy` 직후 `slot.RemoveFriend()`를 호출한다 — `IsOccupied`와 실제 자식 유무가 트랜스폼 계층 탐색 없이 항상 일치한다.

### 2. `InGameSceneManager.GetFieldSlot` — `Index` 기준으로 정렬해두고 배열로 접근

```csharp
protected override void OnAwake()
{
    ...
    _fieldSlots = _fieldSlots.OrderBy(slot => slot.Index).ToArray(); // Inspector 등록 순서와 무관하게 정렬
    ...
}

private FieldSlot GetFieldSlot(int rollValue) => _fieldSlots[rollValue - 1];
```

`_fieldSlots` 배열의 Inspector 등록 순서가 실제 `FieldSlot.Index`(1~6)와 어긋나면 `GetFieldSlot`이 엉뚱한 슬롯을 반환한다. 매번 `Index`를 비교해 찾는 대신, `OnAwake`에서 한 번만 `Index` 기준으로 정렬해두고 이후엔 배열 인덱스로 바로 접근한다.

### 3. 드롭 판정 경로: `Physics2DRaycaster` + `Collider2D`

- Main Camera(world space를 렌더링하는 Orthographic 카메라)에 `Physics2DRaycaster` 컴포넌트를 추가한다.
- `FieldSlot`이 붙은 각 GameObject에 `BoxCollider2D`(카드 스프라이트 크기에 맞춤)를 추가한다.
- `Is Trigger`는 켜두는 것을 권장 — 레이캐스트 자체는 트리거 여부와 무관하게 동작하지만, 다른 2D 물리 상호작용과 혼선을 피한다.

### 4. GameObject 구조 전환(필드 6칸 전부)

기존: `RectTransform` + `CanvasRenderer` + `Image`
전환 후: `Transform` + `SpriteRenderer`(기존 슬롯 배경 스프라이트 재사용) + `BoxCollider2D` + `FieldSlot`

컴퓨터(1~3번)/유저(4~6번) 6칸 모두 동일하게 전환한다 — `GetFieldSlot`/`_fieldSlots` 배열은 6칸을 구분 없이 다루므로 일부만 전환하면 후속 로직이 깨진다.

---

## 클래스 구조

```
FieldSlot : MonoBehaviour, IDropHandler
├── Index : int { get; }
├── PlacedFriend : WorldFriend { get; private set; }   ← 신규
├── IsOccupied : bool { get; }                          ← PlacedFriend != null로 변경
├── PlaceFriend(WorldFriend)                            ← 신규
├── RemoveFriend()                                      ← 신규
└── OnDrop(PointerEventData)                            ← 변경 없음

InGameSceneManager (기존 파일 수정)
├── OnAwake에서 _fieldSlots를 Index 기준 정렬              ← 신규 한 줄
├── GetFieldSlot(rollValue)                             ← 배열 인덱스 접근, 변경 없음(정렬이 선행됨)
└── WorldFriend를 생성/파괴하는 모든 지점에서 PlaceFriend/RemoveFriend 호출  ← 신규
```

---

## 파일 구성

```
Assets/Scripts/InGame/
├── FieldSlot.cs             ← 기존 파일 수정 (PlacedFriend/PlaceFriend/RemoveFriend 추가)
└── InGameSceneManager.cs    ← 기존 파일 수정 (OnAwake 정렬 한 줄 + PlaceFriend/RemoveFriend 호출)
```

씬 리소스(`InGame.unity`)와 카메라 설정도 함께 바뀐다.

---

## Unity 씬/오브젝트 구성

```
[Scene: InGame.unity]
├── Main Camera
│   └── Physics2DRaycaster 컴포넌트 추가
├── my_slot1 / my_slot2 / my_slot3  (유저 필드, Index 4/5/6)
│   └── 각각: RectTransform/CanvasRenderer/Image 제거 → Transform + SpriteRenderer + BoxCollider2D 추가
└── oppo_slot1 / oppo_slot2 / oppo_slot3  (컴퓨터 필드, Index 1/2/3) — 동일하게 전환
```

world 좌표는 기존 화면상 배치를 참고해 카메라 orthographic size(5)와 화면비를 기준으로 재배치한다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `Collider2D` 크기가 스프라이트보다 작음 | 드롭 판정 사각지대 발생 — 스프라이트 bounds에 맞춰 크기 조정 |
| 필드 슬롯끼리 `Collider2D`가 겹침 | `Physics2DRaycaster`가 카메라에 가장 가까운 하나를 선택 — 슬롯 간 z 위치를 동일하게 맞추고 겹치지 않게 배치 |
| UI 패널(Canvas)이 필드 위를 화면상 덮음 | `GraphicRaycaster`가 먼저 히트해 `Physics2DRaycaster` 결과보다 우선될 수 있음 — 필드 위를 덮는 UI 레이아웃이 없는지 확인 |
| Main Camera에 `Physics2DRaycaster`가 없음 | `OnDrop`이 전혀 호출되지 않아 카드가 항상 원래 핸드 슬롯으로 복귀 — 설치 여부를 가장 먼저 확인 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 빈 필드 슬롯(world space) 위에 핸드 카드를 드래그해 놓기 | `FieldSlot.OnDrop` 호출, `InGameSceneManager.TryPlaceFriendCard` 진입 |
| 2 | Main Camera에 `Physics2DRaycaster`를 잠시 제거하고 동일 동작 | `OnDrop` 호출 안 됨 — 카드가 원래 핸드 슬롯으로 복귀(사전 확인용 네거티브 테스트) |
| 3 | 컴퓨터 슬롯(1~3번)에도 `Collider2D`가 정상 부착돼 있는지 | 씬 뷰에서 6칸 모두 Collider2D 존재 확인(유저가 직접 드롭하진 않지만 `BuildObservation`/`GetFieldFriends` 등 후속 로직은 6칸 전부에 의존) |

---

## 구현 시 주의사항

- `WorldFriend`를 생성/파괴하는 지점을 새로 추가할 때는 반드시 `PlaceFriend`/`RemoveFriend`를 함께 호출한다 — 빠뜨리면 `IsOccupied`가 실제 상태와 어긋난다.
- 6칸 중 일부만 전환하면 안 된다 — `InGameSceneManager`의 `GetFieldSlot`/`GetFieldFriends` 등은 컴퓨터/유저 구분 없이 인덱스로 접근한다.
- `_fieldSlots` 배열은 `OnAwake`에서 한 번만 정렬한다 — 이후 씬에서 슬롯을 추가/교체하면 다시 정렬이 필요하다.

---

## 구현 후 체크리스트

- [x] Main Camera에 `Physics2DRaycaster` 추가
- [x] 필드 슬롯 6개(`my_slot1/2/3`, `oppo_slot1/2/3`)에 `BoxCollider2D` 추가(`Is Trigger` 체크)
- [x] `FieldSlot.PlacedFriend`/`PlaceFriend`/`RemoveFriend` 추가, `InGameSceneManager`의 생성/파괴 지점에 반영
- [x] `InGameSceneManager.OnAwake`에서 `_fieldSlots`를 `Index` 기준 정렬
- [x] world 좌표 배치가 기존 화면 레이아웃과 시각적으로 대응하는지 확인
- [x] 테스트 시나리오 검증
- [x] 다음 문서로 이동: [plan-ingame-worldspace-worldfriend.md](plan-ingame-worldspace-worldfriend.md)
