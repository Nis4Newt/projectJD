# InGame 핸드/필드 배치 계획

> 상위 문서: [InGame 로직 개요](plan-ingame.md) (3단계, [덱 구성 계획](plan-ingame-decksetup.md)·[턴 진행 계획](plan-ingame-turnsystem.md) 이후)
> 관련 문서: [Friend 컴포넌트 구현 계획](../05-prefab/plan-prefab.md) (필드용 `Friend` 프리팹 재사용), [덱 구성 계획](plan-ingame-decksetup.md) (`_userDeck` 소비), [턴 진행 계획](plan-ingame-turnsystem.md) (`EnterPhase(PlayFriend)`가 드로우, `OnActionButtonClicked`의 `PlayFriend→RollAttacker`가 핸드 정리 진입점)
> 의존 관계: `JungleDice.InGame.InGameSceneManager`, `JungleDice.InGame.Friend`, `JungleDice.Data.Table.CardTable`(`cardname`/`explain` 포함), `DG.Tweening`
> 범위: 유저 턴 `PlayFriend` 진입 시 `_userDeck`에서 핸드 고정 슬롯으로 드로우, 핸드 카드를 드래그해 유저 필드(4/5/6번)에 놓기, "roll attacker" 클릭 시 핸드 정리(compact). 컴퓨터 측 핸드/필드(1/2/3번), 합체 판정(`CardCondition`/`CardTarget`)·공격/피격·승패 판정은 범위 밖.

---

## 배경

`_userDeck`(유저 친구 3종을 10장씩 섞은 30장 리스트, [덱 구성 계획](plan-ingame-decksetup.md))과 `TurnPhase.PlayFriend`([턴 진행 계획](plan-ingame-turnsystem.md))는 지금까지 스텁이었다. 이번 문서가 처음으로 실제 소비 로직을 연결한다 — "카드를 뽑아 보여주고 필드에 놓는다"까지만 다루고, 합체/공격 판정은 범위 밖이다.

**드로우 순서**: 요청사항의 "친구카드는 플레이어의 friends 순서대로 등장"은 `UserData.Friends`를 순환하는 게 아니라 **이미 셔플된 `_userDeck`을 앞에서부터 순서대로 소비**한다는 뜻으로 확정한다(요청자 확인) — `RemoveAt(0)`으로 그대로 뽑기만 하면 된다.

**핸드 구조**: `HorizontalLayoutGroup` 자동 정렬이 아니라 고정 슬롯(`HandSlot`) 4개로 구성한다. 이유는 세 가지 요구사항 때문이다 — 드래그로 카드가 빠져도 나머지가 자동으로 당겨지면 안 되고, 드롭 실패 시 "마지막 자리"가 아니라 "원래 있던 자기 자리"로 돌아가야 하고, 정리(빈 자리 채우기)는 "roll attacker" 클릭 시점에만 일어나야 한다. `LayoutGroup`은 자식이 바뀌면 즉시 전체를 재배치하므로 이 셋과 근본적으로 맞지 않는다. `FieldSlot`이 이미 쓰는 패턴(점유 여부 = `transform.childCount > 0`)을 핸드에도 그대로 적용한 것이 `HandSlot`이다.

씬에 이미 갖춰진 인프라(`InGame.unity`, `Canvas (1)` 하위 — `IngameSceneManager`가 참조하는 캔버스):

- **`Friend` 프리팹/컴포넌트**: [plan-prefab.md](../05-prefab/plan-prefab.md)에서 완성. 필드 배치 시 `Instantiate` + `SetKey`만 호출.
- **`hand > GameObject (1)`의 기존 자식 4개**(`Image`/`Image (4)`/`Image (5)`/`Image (6)`, 각각 이미 다른 위치에 배치돼 있음): 그대로 `HandSlot` 4개로 쓴다. 안에 내장된 `FriendCard` 미리보기 인스턴스는 제거, 부모의 `HorizontalLayoutGroup`은 제거(또는 비활성화).
- **`Interface > deck`**: 카드가 생성되는 시각적 기준 위치(`deck (1)`/`deck (2)`는 장식 레이어).
- **`mybase > bases`**: 유저 필드 배경 3장(`Image (2)`/`Image (3)`/`Image (4)`) — `FieldSlot` 부착 대상, 4/5/6번.

`FriendCard.prefab`은 아트만 있고 컴포넌트가 없다 — 이번 문서가 `FriendCard.cs`를 처음 작성해 붙인다.

---

## 설계 목표

- `_userDeck`을 실제로 소비하는 첫 지점 — 앞에서부터 `RemoveAt(0)`, 추가 가공 없음
- 핸드는 고정 슬롯 4개(`HandSlot`, 인덱스 0~3) — 유저 `PlayFriend` 진입마다 빈 슬롯 개수만큼만 보충
- 카드는 덱 위치 → 목표 슬롯 위치로 한 장씩 순차적으로 날아간다 — 코루틴+DOTween(`MainMenuTabSlideController`와 동일 관례)
- 드래그로 슬롯이 비어도 자동으로 채워지지 않는다 — 정리(compact)는 "roll attacker" 클릭 시점에만 일어난다
- 점유 판정은 각 컴포넌트가 자식 유무로 직접 안다 — `InGameSceneManager`에 중복 상태를 두지 않는다(`HandSlot.IsOccupied`/`FieldSlot.IsOccupied` 모두 `transform.childCount > 0`)
- 드롭 성공/실패 판정은 `InGameSceneManager.TryPlaceFriendCard` 한 곳에서 — `FieldSlot`은 위임만 한다
- 컴퓨터 측 핸드/필드는 범위 밖 — 유저 전용

---

## 핵심 설계 결정

### 1. `HandSlot` — `FieldSlot`과 동일한 점유 판정 패턴, 드롭 대상은 아님

```csharp
public class HandSlot : MonoBehaviour
{
    [SerializeField] private int _index; // hand 내 순서(왼쪽→오른쪽), 0~3

    public int Index => _index;
    public bool IsOccupied => transform.childCount > 0;
}
```

`IDropHandler`를 구현하지 않는다 — 되돌아가는 목적지일 뿐, 사용자가 직접 드롭하는 대상이 아니다.

### 2. `FriendCard` — 자기 슬롯(`HomeSlot`)을 기억하고, 트윈 이동 후 도착 시 재부모

```csharp
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

        transform.SetParent(_dragLayer, worldPositionStays: true); // 자기 슬롯 밖으로 — 슬롯은 빈 채로 남고, 다른 카드가 채우지 않는다
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
```

- `_homeSlot`은 드로우/정리 시 `MoveToSlot`이 갱신하고, 드래그 도중(`_dragLayer` 아래 있는 동안)엔 바뀌지 않는다 — 그래야 드롭 실패 시 되돌아갈 자리를 안다.
- `MoveToSlot`(트윈)/`AttachToSlot`(즉시)을 분리 — 드롭 실패 복귀는 애니메이션 없이 즉시 반영한다(스코프 최소화), 필요해지면 `OnEndDrag`도 `MoveToSlot`으로 교체 가능.
- `CanvasGroup.blocksRaycasts = false`가 없으면 카드 자신의 `Image`가 포인터 아래 `FieldSlot`보다 먼저 레이캐스트를 가로채 `OnDrop`이 호출되지 않는다.
- Unity는 같은 이벤트 안에서 `IDropHandler.OnDrop`을 `IEndDragHandler.OnEndDrag`보다 먼저 실행한다 — `TryPlaceFriendCard`가 `NotifyPlaced()`를 먼저 호출해두면 `OnEndDrag`가 플래그를 보고 복귀를 건너뛴다(`Destroy()`는 프레임 끝까지 지연되므로 파괴 여부만으론 판단 불가, 명시적 플래그 필수).
- `Canvas (1)`이 `Screen Space - Overlay`라 `eventData.pressEventCamera`가 `null`인 게 정상이다.

### 3. `FieldSlot` — `IDropHandler`, 판정은 `InGameSceneManager`에 위임

```csharp
public class FieldSlot : MonoBehaviour, IDropHandler
{
    [SerializeField] private int _index; // 전체 필드 6자리 중 절대 번호(플레이어는 4/5/6)

    public int Index => _index;
    public bool IsOccupied => transform.childCount > 0;

    public void OnDrop(PointerEventData eventData)
    {
        var card = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<FriendCard>() : null;
        if (card == null) return;

        InGameSceneManager.Instance.TryPlaceFriendCard(this, card);
    }
}
```

`InGameSceneManager`는 `FieldSlot` 배열을 따로 들지 않는다 — 각 슬롯이 `SceneSingleton<InGameSceneManager>.Instance`로 스스로 호출한다.

```csharp
public void TryPlaceFriendCard(FieldSlot slot, FriendCard card)
{
    if (slot.IsOccupied) return; // 이미 점유 — 아무 것도 안 하면 OnEndDrag가 알아서 복귀 처리

    var friend = Instantiate(_friendPrefab, slot.transform);
    friend.SetKey(card.Key);

    card.NotifyPlaced();
    Destroy(card.gameObject);
}
```

### 4. 덱 → 핸드 드로우: 빈 슬롯을 찾아 그 위치로 트윈

```csharp
[SerializeField] private FriendCard _friendCardPrefab;
[SerializeField] private Friend _friendPrefab;
[SerializeField] private Transform _deckOrigin;
[SerializeField] private HandSlot[] _handSlots; // hand의 고정 슬롯 4개, 인덱스 0~3(왼쪽→오른쪽)
[SerializeField] private Transform _dragLayer;
[SerializeField] private float _drawInterval = 0.15f;
[SerializeField] private float _drawDuration = 0.3f;

private void DrawHandCards()
{
    var emptySlots = new List<HandSlot>();
    foreach (var slot in _handSlots)
        if (!slot.IsOccupied) emptySlots.Add(slot);

    int needed = Mathf.Min(emptySlots.Count, _userDeck.Count);
    if (needed <= 0) return;

    StartCoroutine(DrawHandCardsRoutine(emptySlots, needed));
}

private IEnumerator DrawHandCardsRoutine(List<HandSlot> emptySlots, int count)
{
    for (int i = 0; i < count; i++)
    {
        int key = _userDeck[0];
        _userDeck.RemoveAt(0); // 이미 셔플된 순서 그대로 앞에서부터 소비

        SpawnFriendCard(key, emptySlots[i]);

        yield return new WaitForSeconds(_drawInterval);
    }
}

private void SpawnFriendCard(int key, HandSlot slot)
{
    var card = Instantiate(_friendCardPrefab, _dragLayer);
    card.transform.position = _deckOrigin.position; // "덱 오브젝트의 위치에서 생성"
    card.SetKey(key);
    card.Initialize(_dragLayer);

    card.MoveToSlot(slot, _drawDuration); // 빈 슬롯 위치로 이동 후 도착하면 그 슬롯의 자식으로
}
```

빈 슬롯 목록은 드로우 시작 전 한 번만 계산해 각 카드에 하나씩 미리 배정한다. 목표 위치는 "hand 전체 중심"이 아니라 빈 슬롯 각각의 정확한 위치 — 카드마다 다른 슬롯으로 날아간다. `DrawHandCards()`는 `EnterPhase(PlayFriend)`에서 유저 턴일 때만 호출(6번).

### 5. 핸드 정리(compact): "roll attacker" 클릭 시점에만, 앞 슬롯부터 채우기

```csharp
[SerializeField] private float _compactDuration = 0.25f;

private void CompactHand()
{
    var cards = new List<FriendCard>();
    foreach (var slot in _handSlots)
    {
        if (slot.IsOccupied)
            cards.Add(slot.GetComponentInChildren<FriendCard>());
    }

    for (int i = 0; i < cards.Count; i++)
    {
        var targetSlot = _handSlots[i];
        var card = cards[i];

        if (card.HomeSlot == targetSlot) continue; // 이미 제자리

        card.MoveToSlot(targetSlot, _compactDuration);
    }
}
```

카드가 있는 슬롯들을 순서대로 모아 인덱스 0번부터 다시 채운다 — 예: [카드A, 빈칸, 카드B, 카드C] → [카드A, 카드B, 카드C, 빈칸]. 이미 제자리인 카드는 건드리지 않는다(불필요한 트윈 방지). 정리 도중 드래그하는 극단적 케이스는 별도로 막지 않는다(YAGNI).

### 6. 진입 지점: 드로우는 `PlayFriend` 진입 시, 정리는 "roll attacker" 클릭 시

기존 코드에 각각 한 줄만 추가한다.

```csharp
case TurnPhase.PlayFriend:
    Debug.Log($"[InGame] {_currentOwner} 턴 - 친구카드 플레이");
    if (_currentOwner == TurnOwner.User) DrawHandCards();
    _actionButtonText.text = "roll attacker";
    _actionButton.interactable = _currentOwner == TurnOwner.User;
    break;
```

```csharp
case TurnPhase.PlayFriend:
    CompactHand(); // hand를 앞으로 당겨 정리한 뒤 다음 단계로
    EnterPhase(TurnPhase.RollAttacker);
    break;
```

`CompactHand()`는 `EnterPhase(RollAttacker)` 호출 전에 실행 — "정리 후 다음 단계로"라는 순서를 코드 순서로도 그대로 드러낸다. 컴퓨터 턴은 이 경로를 타지 않는다(`OnActionButtonClicked`는 유저 턴에만 호출됨).

---

## 클래스 구조

```
HandSlot : MonoBehaviour                          (신규, InGame/)
├── Index : int { get; }                     ← [SerializeField] _index, 0~3
└── IsOccupied : bool { get; }                ← transform.childCount > 0

FriendCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler   (신규, InGame/)
├── Key : int { get; private set; }
├── HomeSlot : HandSlot { get; }              ← 현재 속한 핸드 슬롯
├── SetKey(int key)                        ← 이미지+이름+설명+공격력+체력 동시 갱신
├── Initialize(Transform dragLayer)
├── MoveToSlot(HandSlot, float duration)      ← 트윈 이동 후 도착 시 AttachToSlot
├── AttachToSlot(HandSlot)                    ← 즉시 재부모 + HomeSlot 갱신
├── OnBeginDrag / OnDrag / OnEndDrag
├── NotifyPlaced()                          ← 필드 배치 성공 시 InGameSceneManager가 호출
├── _canvasGroup : CanvasGroup               ← Awake에서 캐시, 드래그 중 blocksRaycasts 토글
└── _cardImage/_nameText/_descText/_attText/_hpText : [SerializeField]

FieldSlot : MonoBehaviour, IDropHandler       (신규, InGame/)
├── Index : int { get; }                     ← [SerializeField] _index, 4/5/6(플레이어)
├── IsOccupied : bool { get; }                ← transform.childCount > 0
└── OnDrop(PointerEventData)                 ← InGameSceneManager.TryPlaceFriendCard에 위임

InGameSceneManager (기존 파일 수정, InGame/)
├── _friendCardPrefab : FriendCard [SerializeField]   ← 신규
├── _friendPrefab : Friend [SerializeField]           ← 신규
├── _deckOrigin : Transform [SerializeField]          ← 신규, "deck" 오브젝트
├── _handSlots : HandSlot[] [SerializeField]          ← 신규, 4개(0~3)
├── _dragLayer : Transform [SerializeField]           ← 신규, "Canvas (1)" 루트
├── _drawInterval : float = 0.15f [SerializeField]    ← 신규
├── _drawDuration : float = 0.3f [SerializeField]     ← 신규
├── _compactDuration : float = 0.25f [SerializeField] ← 신규
├── DrawHandCards()                                    ← 신규, private
├── DrawHandCardsRoutine(List<HandSlot>, int) : IEnumerator  ← 신규, private
├── SpawnFriendCard(int key, HandSlot)                  ← 신규, private
├── CompactHand()                                       ← 신규, private
├── TryPlaceFriendCard(FieldSlot, FriendCard)           ← 신규, public (FieldSlot이 호출)
├── EnterPhase(TurnPhase.PlayFriend) 분기                ← 기존 case에 한 줄 추가
└── OnActionButtonClicked() PlayFriend 분기               ← 기존 case에 한 줄 추가
```

---

## 파일 구성

```
Assets/Scripts/
└── InGame/
    ├── HandSlot.cs               ← 신규
    ├── FriendCard.cs              ← 신규
    ├── FieldSlot.cs               ← 신규
    └── InGameSceneManager.cs      ← 기존 파일 수정 (드로우/정리/배치 로직 + 신규 필드 추가)
```

`FriendCard.prefab`(아트만 있음)에 `FriendCard.cs`를 부착하는 작업은 Unity 에디터에서 진행. `Friend.prefab`은 이미 `Friend.cs`가 붙어 있어 수정 없음.

---

## Unity 씬/오브젝트 구성

```
[Assets/Prefabs/FriendCard.prefab]
└── FriendCard(루트)
    ├── FriendCard.cs 부착 (신규)          ← _cardImage="friend", _nameText="name_txt",
    │                                         _descText="desc_txt", _attText="att_txt", _hpText="hp_txt" 연결
    └── CanvasGroup 부착 (신규, [RequireComponent]가 자동 추가)

[Scene: InGame.unity, Canvas (1) 하위]
├── Interface > hand > GameObject (1)
│   ├── HorizontalLayoutGroup 제거(또는 비활성화) — 슬롯은 고정 위치여야 함
│   └── 기존 자식 4개(Image/Image (4)/Image (5)/Image (6))를 그대로 HandSlot 4개로
│       ├── 각각 HandSlot 부착, _index = 0/1/2/3 (화면상 좌→우 순서 확인)
│       └── 내장된 FriendCard 미리보기 인스턴스 제거 — 슬롯은 처음엔 비어 있어야 함
├── Interface > deck                       ← _deckOrigin
├── mybase > bases > Image (2)/(3)/(4)     ← 각각 FieldSlot 부착, Index = 4/5/6
│                                             (좌→우 실제 화면 배치 확인 후 배정)
└── Canvas (1) (루트)                      ← _dragLayer(드래그 중 최상단 렌더링)

[IngameSceneManager GameObject]
└── InGameSceneManager.cs
    ├── _friendCardPrefab ← Assets/Prefabs/FriendCard.prefab
    ├── _friendPrefab     ← Assets/Prefabs/Friend.prefab
    ├── _deckOrigin       ← 위 deck 트랜스폼
    ├── _handSlots        ← [Image, Image (4), Image (5), Image (6)]의 HandSlot (인덱스 0~3 순서대로)
    └── _dragLayer        ← Canvas (1) 트랜스폼
```

`FieldSlot` 3개는 `IngameSceneManager`에 연결하지 않는다(핵심 설계 결정 3번) — 반면 `_handSlots`는 배열 순서 자체가 "채우는 순서"라 반드시 연결해야 한다. `EventSystem`/`GraphicRaycaster`는 이미 씬에 존재.

---

## 이번 범위에서 제외

- 컴퓨터 측 핸드 연출/필드(1/2/3번), `_computerDeck` 소비
- 합체 판정(`CardCondition`/`CardTarget`), 공격/피격, 승패 판정 — 필드에 `Friend`가 놓이는 것까지만
- 핸드 4장 꽉 찬 상태에서 필드에 안 놓고 턴 넘어갈 때의 "카드 버리기"/페널티 — 그냥 드로우가 0장이 될 뿐
- 필드에 놓인 `Friend`를 다시 드래그하는 기능 — `Friend`는 드래그 인터페이스 없음
- 덱 소진 시 처리(패배 조건 등) — `needed`가 0이 되어 드로우가 멈출 뿐
- 드롭 실패 복귀의 이동 애니메이션 — 지금은 즉시 스냅(`AttachToSlot`), 필요해지면 `MoveToSlot`으로 교체 가능
- 정리(compact) 트윈 도중의 드래그 등 겹치는 상호작용 특별 처리(YAGNI)

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 핸드 슬롯 4개 모두 점유 | `emptySlots.Count == 0` → `needed <= 0`으로 조기 리턴, 드로우 없음 |
| `_userDeck` 잔여량이 `needed`보다 적음 | `Mathf.Min`으로 남은 만큼만 드로우 — 예외 없음, 일부 슬롯 빈 채 유지 |
| 필드 슬롯이 아닌 곳에 드롭 / 이미 점유된 필드 슬롯에 드롭 / 집었다가 그 자리에서 뗌 | 세 경우 모두 `_wasPlaced`가 `false`로 남아 `OnEndDrag`가 `AttachToSlot(_homeSlot)`으로 원래 자리 복귀 |
| 핸드 카드를 그대로 둔 채 "roll attacker" 클릭 | `CompactHand()`가 빈 슬롯을 채우도록 나머지를 앞으로 당김(이미 앞이 꽉 차 있으면 이동 없음) |
| 드래그 중인 카드가 있는 채로 "roll attacker" 클릭 | 드래그 중인 카드는 어느 슬롯에도 속하지 않은 상태라 `CompactHand()`의 슬롯 스냅샷에서 자연히 제외됨 |
| 드로우 코루틴 도중 씬 전환 | `MonoBehaviour` 파괴로 코루틴 자동 중단 |
| 신규 필드(`_friendCardPrefab` 등) 인스펙터 연결 누락 | `NullReferenceException` — 기존 관례와 동일하게 방어 코드 없이 즉시 드러냄 |
| `FieldSlot.OnDrop`의 `pointerDrag`가 `FriendCard`가 아님 | `GetComponent<FriendCard>()`가 `null` → 조기 리턴(방어용, 현재는 발생 안 함) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | Solo 진입, 유저 선공 첫 `PlayFriend` | `_userDeck.Count` 30→26, `_handSlots[0..3]`에 카드가 순차 도착, key가 드로우 전 `_userDeck` 앞 4개와 인덱스 순서대로 일치 |
| 2 | 슬롯 0번 카드를 4번 필드 슬롯으로 드래그 | 그 슬롯 아래 `Friend` 생성, 원래 `FriendCard` 파괴, `_handSlots[0].IsOccupied == false` |
| 3 | 이미 점유된 필드 슬롯 / 빈 화면에 드롭 | 카드가 자기 원래 핸드 슬롯으로 복귀 |
| 4 | 시나리오 2 상태(슬롯 0만 빈칸)에서 "roll attacker" 클릭 | 슬롯 1/2/3의 카드가 각각 0/1/2로 트윈 이동, 슬롯 3이 빈 자리가 됨 |
| 5 | 핸드 3장을 필드에 놓고 1장은 남긴 채 다음 유저 `PlayFriend` 진입 | 빈 슬롯 3개만큼만 드로우, 기존 1장은 유지 |
| 6 | `_userDeck.Count == 2`에서 `PlayFriend` 진입(핸드 0장) | 2장만 드로우, 이후 `Count == 0`, 예외 없음 |
| 7 | 드래그 중 카드가 다른 UI 위를 지나갈 때 | `blocksRaycasts=false`라 아래 `FieldSlot`이 정상적으로 `OnDrop` 후보가 됨 |

---

## 구현 시 주의사항

- `FieldSlot`/`FriendCard`는 스스로 배치를 승인하지 않는다 — 반드시 `TryPlaceFriendCard`를 거친다.
- `NotifyPlaced()`는 `Destroy()` 이전에 호출한다 — `Destroy`는 프레임 끝까지 지연되므로 플래그 없이는 판단 불가.
- `CanvasGroup.blocksRaycasts` 토글을 빠뜨리면 드롭이 영영 발생하지 않는다.
- `hand`의 `HorizontalLayoutGroup`을 반드시 제거/비활성화한다 — 남아있으면 슬롯 위치가 매 프레임 재배치되어 "고정 슬롯" 전제가 깨진다.
- `hand` 슬롯 안의 기존 `FriendCard` 미리보기 인스턴스를 반드시 제거한다 — 안 지우면 시작부터 모든 슬롯이 점유 상태로 잡힌다.
- `_handSlots` 배열 순서 = 빈 슬롯을 채우는 순서다 — 화면 좌→우와 어긋나면 애니메이션이 슬롯을 건너뛰는 것처럼 부자연스러워 보인다.
- `CompactHand()`는 `EnterPhase(RollAttacker)` 호출 전에 실행한다.
- DOTween `useSafeMode` 설정을 그대로 신뢰 — 트윈 도중 카드가 파괴되는 경로가 이번 범위엔 없지만, 프로젝트 전역 설정이 이미 안전 모드다.

---

## 구현 후 체크리스트

- [ ] `HandSlot.cs`/`FriendCard.cs`/`FieldSlot.cs` 작성 (`Assets/Scripts/InGame/`)
- [ ] `InGameSceneManager.cs`: 신규 필드 7종 + `DrawHandCards`/`DrawHandCardsRoutine`/`SpawnFriendCard`/`CompactHand`/`TryPlaceFriendCard` 추가, `EnterPhase(PlayFriend)`와 `OnActionButtonClicked`에 각 한 줄 추가
- [ ] `FriendCard.prefab`에 스크립트 부착 + 필드 5개 인스펙터 연결 (Unity 에디터 작업)
- [ ] `hand > GameObject (1)`의 `HorizontalLayoutGroup` 제거, 기존 자식 4개에 `HandSlot` 부착(`_index` 0~3) + 내장 미리보기 인스턴스 제거 (Unity 에디터 작업)
- [ ] `mybase > bases`의 3장에 `FieldSlot` 부착(`_index` 4/5/6) (Unity 에디터 작업)
- [ ] `IngameSceneManager`에 신규 필드 인스펙터 연결 (Unity 에디터 작업)
- [ ] 테스트 시나리오 7개 검증 (특히 #3, #4: 원래 자리 복귀·정리 애니메이션 육안 확인)
- [ ] (추후) 컴퓨터 핸드/필드(1/2/3번) 별도 계획 문서
- [ ] (추후) 합체 판정/공격·피격/승패 판정을 다루는 후속 계획 문서
