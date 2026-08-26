# FriendCard 데이터/드래그 분리 구현 계획

> 상위 문서: [메인메뉴 친구 선택/교체 구현 계획](./plan-mainmenuscene-friendselect.md) (메인메뉴에서 `FriendCard`를 표시 전용으로 안전하게 재사용하려면 배틀 드래그 책임과 분리해야 한다는 필요에서 파생)
> 의존 관계: `JungleDice.InGame.InGameSceneManager`(배틀 드래그 진입점), `JungleDice.InGame.HandSlot`/`FieldSlot`, `JungleDice.Data.Table.CardTable`, `JungleDice.Core.Sprites.SpriteManager`, DOTween
> 범위: `Assets/Scripts/InGame/FriendCard.cs` 하나를 데이터 컴포넌트(`FriendCard`)와 배틀 전용 드래그/핸드슬롯 컴포넌트(`FriendCardBattleControl`)로 쪼개고, 그로 인해 바뀌는 InGame 호출부(`InGameSceneManager`, `FieldSlot`)를 수정한다. 메인메뉴 쪽에서 분리된 `FriendCard`를 실제로 소비하는 `FriendCardMainControl`/`FriendListItem`/`FriendSlot` 등은 이 문서의 범위 밖 — [friendselect.md](./plan-mainmenuscene-friendselect.md) 쪽에서 이어서 다룬다(아래 "구현 후 체크리스트" 참고).

---

## 배경 / 문제 인식

`FriendCard`(`Assets/Scripts/InGame/FriendCard.cs`)는 두 가지 서로 무관한 책임을 한 클래스에 담고 있다:

1. **데이터/표시**: `SetKey(int)` — `CardTable`에서 조회해 이미지/이름/설명/공격력/생명력 텍스트를 채운다.
2. **배틀 드래그/핸드슬롯**: `IBeginDragHandler`/`IDragHandler`/`IEndDragHandler`, `HomeSlot`/`MoveToSlot`/`AttachToSlot`(핸드 슬롯 부착), `NotifyPlaced`/`Discard`(필드 배치·풀핸드 파괴), 그리고 `OnBeginDrag`가 `InGameSceneManager.Instance.CanPlayFriend`/`ShowMergePreview`를 직접 호출.

이 결합 때문에 `FriendCard`를 메인메뉴처럼 드래그가 필요 없는 화면에 그대로 붙이면 `OnBeginDrag`가 참조하는 `InGameSceneManager.Instance`가 없어 크래시한다. 표시 전용 `Friend` 컴포넌트(이미지+공격력+생명력만, 이름/설명 없음)로 대체하는 방법도 있지만, `FriendCard`와 같은 정보량을 보여주려면 결국 `FriendCard`의 표시 로직을 다시 베끼는 셈이 된다.

2번 책임(드래그/핸드슬롯)만 별도 컴포넌트로 떼어내면 1번(데이터/표시)은 `InGameSceneManager`에 전혀 의존하지 않으므로, 메인메뉴에서도 `FriendCard` 하나로 이름/설명까지 포함한 카드 표시를 그대로 재사용할 수 있다.

---

## 설계 목표

- `FriendCard`는 `key → 이미지/이름/설명/공격력/생명력` 표시만 담당하고, `MonoBehaviour` 외 어떤 인터페이스도 구현하지 않는다 — 배틀/메인메뉴 어느 씬에서 `AddComponent`해도 안전해야 한다
- 드래그/핸드슬롯 책임은 새 컴포넌트 `FriendCardBattleControl`로 옮기되, 동작(드래그 좌표 계산, 핸드 슬롯 부착, 필드 배치/파괴)은 기존과 **한 줄도 다르지 않게** 유지한다 — 이번 문서는 순수 리팩터링이지 배틀 로직 변경이 아니다
- `FriendCardBattleControl`는 `FriendCard`를 `[RequireComponent]`로 강제하고 데이터가 필요할 때 `Data` 프로퍼티로 위임한다 — `Key`/`SetKey`를 중복 노출하지 않는다(단일 진실 공급원 유지)
- 호출부(`InGameSceneManager`, `FieldSlot`)가 드래그 동작이 필요한 곳에서는 `FriendCardBattleControl` 타입을, 데이터만 필요한 곳에서는 `FriendCard`(또는 `.Data`)를 쓰도록 최소한만 고친다

---

## 핵심 설계 결정

### 1. 분리 기준: "배틀 드래그/핸드슬롯" 전부를 `FriendCardBattleControl`로 — `CanvasGroup` 포함

`_canvasGroup`은 드래그 중 레이캐스트 차단(`blocksRaycasts`)과 `Discard`의 페이드아웃에만 쓰인다 — 둘 다 배틀 전용 동작이므로 `[RequireComponent(typeof(CanvasGroup))]`도 `FriendCard`가 아니라 `FriendCardBattleControl`로 옮긴다. `FriendCard`는 표시 필드(`Image`/`TextMeshProUGUI` 4종)만 남는다.

| 메서드/필드 | 이동 위치 | 사유 |
|---|---|---|
| `SetKey`, `Key` | `FriendCard` (유지) | 순수 데이터 조회 |
| `Initialize(dragLayer)`, `OnBeginDrag/OnDrag/OnEndDrag` | `FriendCardBattleControl` | 드래그 좌표 계산 + `InGameSceneManager` 호출 |
| `HomeSlot`, `MoveToSlot`, `AttachToSlot` | `FriendCardBattleControl` | 핸드 슬롯 개념 자체가 배틀 전용 |
| `NotifyPlaced`, `Discard` | `FriendCardBattleControl` | 필드 배치/풀핸드 파괴 — 배틀 상태 전이 |
| `CanvasGroup` 참조 | `FriendCardBattleControl` | 위 표 상단 설명 |

### 2. `FriendCardBattleControl`는 `Data` 프로퍼티로 `FriendCard`를 노출, `Key`를 따로 복제하지 않는다

```csharp
[RequireComponent(typeof(FriendCard))]
[RequireComponent(typeof(CanvasGroup))]
public class FriendCardBattleControl : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private CanvasGroup _canvasGroup;
    private FriendCard _friendCard;

    public FriendCard Data => _friendCard;

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        _friendCard = GetComponent<FriendCard>();
    }
    // ...
}
```

`FriendCardBattleControl.Key` 같은 위임 프로퍼티를 추가하는 방법도 검토했지만, 호출부가 `card.Data.Key`로 쓰든 `card.Key`로 쓰든 결국 같은 값이라 위임 프로퍼티는 "어느 쪽이 원본인지" 헷갈림만 늘린다(중복 API). 호출부 4곳뿐이라 `.Data.Key`로 명시하는 쪽이 더 명확하다.

### 3. InGame 프리팹은 두 컴포넌트를 그대로 같이 둔다(런타임 `AddComponent` 아님)

배틀의 손패 카드는 스폰되는 순간부터 항상 드래그 가능해야 하므로(선택적 기능이 아님), `FriendCard.prefab`에 `FriendCard`+`FriendCardBattleControl`를 에디터에서 함께 붙여둔다 — 매 스폰마다 `AddComponent<FriendCardBattleControl>()`를 호출할 이유가 없다. `InGameSceneManager`의 `_friendCardPrefab` 필드 타입을 `FriendCardBattleControl`로 바꾸면 `Instantiate` 결과 하나로 드래그 메서드(`Initialize`/`MoveToSlot`)와 데이터(`.Data.SetKey`) 둘 다 접근 가능하다.

(메인메뉴처럼 "기본은 표시만, 필요할 때만 드래그 컴포넌트를 얹는" 조합이 필요한 경우는 `plan-mainmenuscene-friendselect.md` 쪽에서 다룬다 — 그쪽은 씬에 고정 배치된 오브젝트에 `FriendCard`+메인메뉴 전용 이벤트 클래스를 함께 붙이는 방식이라 이 문서와 결이 다르다.)

---

## 클래스 구조

```
FriendCard : MonoBehaviour                        (기존 파일 축소, InGame/)
├── Key : int { get; private set; }
├── SetKey(int key)                                 ← 이미지+이름+설명+att+hp 동시 갱신 (변경 없음)
├── _cardImage : Image                              ← [SerializeField]
├── _nameText / _descText / _attText / _hpText      ← [SerializeField]
└── (드래그/CanvasGroup/HomeSlot 전부 제거)

FriendCardBattleControl : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler   (신규, InGame/)
├── [RequireComponent(typeof(FriendCard))]
├── [RequireComponent(typeof(CanvasGroup))]
├── Data : FriendCard { get; }                      ← Awake에서 GetComponent 캐싱
├── HomeSlot : HandSlot { get; }
├── Initialize(Transform dragLayer)
├── MoveToSlot(HandSlot slot, float duration)        ← 기존 FriendCard.MoveToSlot 그대로
├── AttachToSlot(HandSlot slot)                       ← 기존 FriendCard.AttachToSlot 그대로
├── OnBeginDrag/OnDrag/OnEndDrag                      ← 기존 FriendCard 구현 그대로, Key 참조만 Data.Key로 변경
├── NotifyPlaced()
└── Discard(float duration)
```

---

## 파일 구성

```
Assets/
├── Prefabs/
│   └── FriendCard.prefab                ← 기존 파일 수정, FriendCardBattleControl 컴포넌트 추가 부착 (Unity 에디터 작업)
└── Scripts/
    └── InGame/
        ├── FriendCard.cs                ← 기존 파일 축소 (데이터만)
        ├── FriendCardBattleControl.cs            ← 신규
        ├── InGameSceneManager.cs        ← 기존 파일 수정 (호출부)
        └── FieldSlot.cs                 ← 기존 파일 수정 (OnDrop)
```

---

## 상세 구현 명세

### `FriendCard.cs` (축소)

```csharp
using JungleDice.Core.Sprites;
using JungleDice.Data.Table;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JungleDice.InGame
{
    public class FriendCard : MonoBehaviour
    {
        [SerializeField] private Image _cardImage;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _descText;
        [SerializeField] private TextMeshProUGUI _attText;
        [SerializeField] private TextMeshProUGUI _hpText;

        public int Key { get; private set; }

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
    }
}
```

### `FriendCardBattleControl.cs` (신규 — 기존 `FriendCard`의 드래그/핸드슬롯 코드를 그대로 이전)

```csharp
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace JungleDice.InGame
{
    [RequireComponent(typeof(FriendCard))]
    [RequireComponent(typeof(CanvasGroup))]
    public class FriendCardBattleControl : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private CanvasGroup _canvasGroup;
        private FriendCard _friendCard;
        private Transform _dragLayer;
        private HandSlot _homeSlot;
        private bool _wasPlaced;

        public FriendCard Data => _friendCard;
        public HandSlot HomeSlot => _homeSlot;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _friendCard = GetComponent<FriendCard>();
        }

        public void Initialize(Transform dragLayer) => _dragLayer = dragLayer;

        public void MoveToSlot(HandSlot slot, float duration)
        {
            transform.DOMove(slot.transform.position, duration)
                .SetEase(Ease.OutQuint)
                .OnComplete(() => AttachToSlot(slot));
        }

        public void AttachToSlot(HandSlot slot)
        {
            _homeSlot = slot;
            transform.SetParent(slot.transform, worldPositionStays: false);
            ((RectTransform)transform).anchoredPosition = Vector2.zero;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!InGameSceneManager.Instance.CanPlayFriend)
            {
                eventData.pointerDrag = null;
                return;
            }

            _canvasGroup.blocksRaycasts = false;
            transform.SetParent(_dragLayer, worldPositionStays: true);
            transform.SetAsLastSibling();

            InGameSceneManager.Instance.ShowMergePreview(_friendCard.Key);
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
            InGameSceneManager.Instance.HideMergePreview();

            if (_wasPlaced) return;

            AttachToSlot(_homeSlot);
        }

        public void NotifyPlaced() => _wasPlaced = true;

        public void Discard(float duration)
        {
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.DOFade(0f, duration).OnComplete(() => Destroy(gameObject));
        }
    }
}
```

### 호출부 수정 — `InGameSceneManager.cs`

`_friendCardPrefab` 타입과 드래그가 필요한 지역 변수 타입을 `FriendCard` → `FriendCardBattleControl`로, 데이터 접근은 `.Data.Key`/`.Data.SetKey(...)`로 바꾼다. 로직 흐름 자체는 변경 없음:

```csharp
[SerializeField] private FriendCardBattleControl _friendCardPrefab; // 기존: FriendCard

private FriendCardBattleControl SpawnCardAtDeck(int key)             // 기존: FriendCard 반환
{
    var card = Instantiate(_friendCardPrefab, _dragLayer);
    card.transform.position = _deckOrigin.position;
    card.Data.SetKey(key);                                   // 기존: card.SetKey(key)
    return card;
}

private void CompactHand()
{
    var cards = new List<FriendCardBattleControl>();                  // 기존: List<FriendCard>
    foreach (var slot in _handSlots)
        if (slot.IsOccupied)
            cards.Add(slot.GetComponentInChildren<FriendCardBattleControl>()); // 기존: FriendCard
    // 이하 동일 (card.HomeSlot / card.MoveToSlot 그대로 — FriendCardBattleControl가 그대로 제공)
}

public void TryPlaceFriendCard(FieldSlot slot, FriendCardBattleControl card)  // 기존: FriendCard card
{
    if (slot.IsOccupied)
    {
        var existing = slot.GetComponentInChildren<Friend>();
        if (!CanMerge(existing, card.Data.Key)) return;        // 기존: card.Key
        MergeCardIntoSlot(existing, card.Data.Key, slot.Index); // 기존: card.Key
        card.NotifyPlaced();
        Destroy(card.gameObject);
        return;
    }

    var friend = Instantiate(_friendPrefab, slot.transform);
    friend.SetKey(card.Data.Key);                               // 기존: card.Key
    card.NotifyPlaced();
    Destroy(card.gameObject);
}
```

`SpawnFriendCard`, `DrawAndDiscardOne`(`.Discard(_drawDuration)` 호출)은 반환/매개변수 타입만 `FriendCardBattleControl`로 따라가고 본문은 그대로다.

### 호출부 수정 — `FieldSlot.cs`

```csharp
public void OnDrop(PointerEventData eventData)
{
    var card = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<FriendCardBattleControl>() : null; // 기존: FriendCard
    if (card == null) return;

    InGameSceneManager.Instance.TryPlaceFriendCard(this, card);
}
```

---

## Unity 씬/오브젝트 구성

`Assets/Prefabs/FriendCard.prefab` 루트에 `FriendCardBattleControl` 컴포넌트를 추가로 부착한다(`[RequireComponent(typeof(FriendCard))]`라 `FriendCard`는 이미 있으므로 자동으로 요구조건은 만족, `CanvasGroup`도 기존에 이미 있던 컴포넌트라 새로 생기지 않음 — `FriendCardBattleControl`만 추가하면 됨). 필드 값(`_dragLayer` 등)은 `Initialize`로 런타임에 주입되므로 인스펙터에서 채울 값 없음.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `FriendCard.prefab`에 `FriendCardBattleControl` 부착을 누락 | `_friendCardPrefab` 필드 타입이 `FriendCardBattleControl`이므로 프리팹에 컴포넌트가 없으면 `Instantiate` 결과가 `null` — 인스펙터 필드 자체가 애초에 연결 불가(할당 시점에 타입 불일치로 드러남), 방어 코드 불필요 |
| 드래그 없이 `FriendCard`만 붙은 오브젝트에서 드래그 이벤트 발생 | 발생하지 않음 — `IBeginDragHandler` 등을 `FriendCard`가 구현하지 않으므로 uGUI가애초에 드래그 이벤트를 보내지 않음 |
| `FriendCardBattleControl.Awake`가 `FriendCard`를 못 찾음 | `[RequireComponent(typeof(FriendCard))]`가 있어 Unity가 컴포넌트 추가 시점에 강제로 같이 붙이므로 발생 불가 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 컴파일 후 배틀 씬 진입, 덱에서 카드 드로우 | 기존과 동일하게 카드가 덱 위치에서 생성돼 빈 핸드 슬롯으로 이동 |
| 2 | 손패 카드를 드래그해 필드 슬롯에 드롭(병합 가능 조합) | 기존과 동일하게 병합 처리 후 `FriendCardBattleControl` 오브젝트 파괴, `NotifyPlaced` 경로 정상 동작 |
| 3 | 손패 카드를 드래그하다 빈 곳에 드롭(배치 실패) | `OnEndDrag`가 `AttachToSlot(_homeSlot)`으로 원래 슬롯 복귀 — 기존과 동일 |
| 4 | 핸드 풀 상태에서 드로우 | `SpawnCardAtDeck(key).Discard(_drawDuration)` 호출 경로 유지, 페이드 후 파괴 |
| 5 | `CanPlayFriend == false`인 상태에서 드래그 시도 | `OnBeginDrag`에서 `eventData.pointerDrag = null`로 드래그 자체 취소 — 기존과 동일 |

---

## 구현 시 주의사항

- **로직을 바꾸지 않는다** — 이번 문서는 순수하게 클래스를 쪼개는 리팩터링이다. `MoveToSlot`/`AttachToSlot`/`OnBeginDrag` 등의 본문은 기존 `FriendCard.cs`에서 그대로 복사하고, `Key` 참조만 `_friendCard.Key`/`card.Data.Key`로 바꾼다.
- **`FriendCard`에는 더 이상 `CanvasGroup`이 필요 없다** — `[RequireComponent(typeof(CanvasGroup))]`를 `FriendCard`에서 제거하고 `FriendCardBattleControl`로 옮긴다. 프리팹에는 이미 `CanvasGroup`이 있으므로 컴포넌트 제거/재생성 이슈는 없다.
- **`Data.Key`로 통일하고 위임 프로퍼티를 추가하지 않는다** — 호출부 4곳(`SpawnCardAtDeck`, `TryPlaceFriendCard` 2곳, `FieldSlot.OnDrop`은 `.Data` 접근 없음)만 고치면 되므로 중복 API보다 명시적 접근이 낫다(위 "핵심 설계 결정 2" 참고).
- **`InGameSceneManager`/`FieldSlot`에서 `FriendCard` 타입으로 남아있던 지역 변수는 전부 검색해서 `FriendCardBattleControl`로 바꾼다** — 이 문서의 "호출부 수정"에 나열된 지점이 전부다(`ComputerAI.cs`/`Core/UI/*`는 주석에만 `FriendCard`가 등장, 실제 코드 참조 없음 — 확인 완료).

---

## 구현 후 체크리스트

- [x] `FriendCard.cs` 축소 (데이터 필드/`SetKey`/`Key`만 남김, `CanvasGroup` 요구조건 제거)
- [x] `FriendCardBattleControl.cs` 작성 (`Assets/Scripts/InGame/`)
- [x] `InGameSceneManager.cs`: `_friendCardPrefab` 타입, `SpawnCardAtDeck`/`CompactHand`/`TryPlaceFriendCard` 수정
- [x] `FieldSlot.cs`: `OnDrop`의 `GetComponent<FriendCard>()` → `GetComponent<FriendCardBattleControl>()`
- [x] `Assets/Prefabs/FriendCard.prefab`에 `FriendCardBattleControl` 컴포넌트 부착 — Unity 에디터 작업
- [ ] 테스트 시나리오 5개 검증(배틀 씬 플레이 모드)
- [ ] 다음 문서로 이동: [plan-mainmenuscene-friendselect.md](./plan-mainmenuscene-friendselect.md)
