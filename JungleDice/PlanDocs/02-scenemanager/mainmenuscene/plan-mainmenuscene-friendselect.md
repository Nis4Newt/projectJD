# 메인메뉴 친구 선택/교체 구현 계획

> 상위 문서: [메인메뉴 슬라이드/탭 구현 계획](./plan-mainmenuscene-tabslide.md) (`TabSlide`가 정의한 4페이지 중 "친구" 탭 페이지 콘텐츠로 파생)
> 관련 문서: [FriendCard 데이터/드래그 분리 구현 계획](./plan-mainmenuscene-friendcard-split.md) (`FriendCard`를 배틀 드래그와 분리해 이번 문서 전 카드 시각 요소가 재사용하는 대상), [UserData 구현 계획](../../04-userdata/plan-userdata.md) (`Friends`/`SetFriends`가 이번 기능의 영속 대상), [메인메뉴 유저 정보 HUD 연결 계획](./plan-mainmenuscene-userdata-hud.md) (`UserDataChanged` 구독 → 재바인딩 패턴 재사용), [테이블 리더 시스템 구현 계획](../../03-table/plan-table.md) (`CardTable.Instance` 조회 컨벤션)
> 의존 관계: `JungleDice.InGame.FriendCard`(데이터/표시 전용, 재사용 — 드래그 분리 후의 버전), `JungleDice.Data.Table.CardTable`, `JungleDice.Core.User.UserManager`/`UserData`, `JungleDice.Core.Event.EventBus`/`UserDataChanged`, DOTween(`Assets/Plugins/Demigiant/DOTween`, 드래그 실패 시 복귀 애니메이션)
> 범위: 친구 탭 페이지 안에서 — 전체 친구 카드 목록 노출, 현재 선택된 3개 슬롯 표시, 목록 카드 클릭 시 선택 팝업, 팝업의 "선택" 클릭 이후 드래그/직접 클릭으로 슬롯 교체, 배경 클릭으로 교체 취소까지 다룬다. 탭 자체의 슬라이드/전환(`MainMenuTabSlideController`)과 목록 정렬·필터, 팝업을 배경 클릭으로 닫는 기능은 범위 밖(아래 "이번 범위에서 제외" 참고).

---

## 배경 / 문제 인식

`UserData.Friends`(`IReadOnlyList<int>`, 항상 3개 유지)와 `UserData.SetFriends(IEnumerable<int>)`는 이미 구현돼 있지만(`plan-userdata.md`), 이를 실제로 조작하는 UI가 아직 없다. `CardTable`에는 친구 카드 19종이 등록돼 있고(`Assets/Tables/Source/CardTable.csv`), 이 중 3개를 골라 `UserData.Friends`에 반영하는 화면이 이번 문서의 대상이다.

카드를 key로 받아 이미지/이름/설명/att/hp를 그리는 `FriendCard`(`Assets/Scripts/InGame/FriendCard.cs`)는 [friendcard-split.md](./plan-mainmenuscene-friendcard-split.md)에 의해 데이터/표시 전용으로 분리돼 있다 — 드래그/핸드슬롯 책임은 `FriendCardBattleControl`로 빠졌고, `FriendCard` 자체는 `InGameSceneManager`에 전혀 의존하지 않는다. 이번 문서는 그 `FriendCard`를 그대로 재사용하고, 메인메뉴 전용 드래그/슬롯 결합만 새로 만든다.

---

## 설계 목표

- 친구 탭 진입 시 `CardTable`의 모든 행을 그리드로 노출하고, 현재 `UserData.Friends`(3개)를 상단 슬롯에 표시한다
- 목록 카드 클릭 → 팝업(선택) → 드래그 또는 슬롯 직접 클릭으로 교체까지, 상태 전환이 항상 하나의 진입점(`FriendTabController`)을 거치게 해 "팝업은 닫혔는데 교체 UI는 안 뜸" 같은 불일치를 막는다
- 실제 데이터 변경은 기존 `UserData.SetFriends`/`UserDataChanged` 배선을 그대로 타게 해, 슬롯 갱신 로직을 HUD와 동일한 패턴(구독 → 재바인딩)으로 통일한다
- 배틀 전용 컴포넌트(`FriendCardBattleControl`/`InGameSceneManager`/`HandSlot`)에 새 의존을 만들지 않는다 — 표시는 분리된 `FriendCard`(데이터 전용)를 재사용하고, 드래그/슬롯/팝업은 이 씬 전용으로 새로 만든다
- 새 패턴 발명 최소화: 클릭은 전부 `Button.onClick`(프로젝트 전역 관례), 애니메이션은 DOTween, 구독 해제는 `CompositeDisposable`

---

## 핵심 설계 결정

### 1. 카드 시각 요소는 전부 분리된 `FriendCard`(데이터 전용) 컴포넌트를 직접 재사용한다

| 후보 | 기각/채택 사유 |
|------|----------------|
| `FriendCard`(드래그 포함, 분리 전) 재사용 | 기각 — `OnBeginDrag`가 `InGameSceneManager.Instance`를 직접 호출해 메인메뉴에서 크래시(`friendcard-split.md`가 데이터/드래그를 분리해 해소) |
| `Friend`(InGame, 표시 전용: 이미지+att+hp) 재사용 | 기각 — `FriendCard`(이미지+이름+설명+att+hp)보다 정보량이 적고, 표시 컴포넌트를 두 개 유지할 이유가 없음 |
| **`FriendCard`(분리 후, 데이터 전용) 재사용** | **채택** — `SetKey(int)` 하나로 이미지/이름/설명/att/hp만 그리고 배틀 상태/드래그와 완전히 무관하다. 목록 아이템/슬롯/팝업/교체 카드 네 곳 모두 이 컴포넌트를 두고, 그 위에 각자 필요한 상호작용(클릭 전용 vs 드래그)만 얹는다 |

목록(`FriendListItem`)과 팝업은 `FriendCard`를 자식 오브젝트로 붙여 표시만 위임한다. 슬롯(`FriendSlot`)은 `FriendListItem`을 통째로 재사용해 같은 조합을 한 번 더 정의하지 않는다(핵심 설계 결정 3 참고). 교체 카드(`ReplaceCard`)는 같은 오브젝트에 `FriendCard`(표시)+`FriendCardMainControl`(드래그)를 나란히 붙여 쓴다 — 두 컴포넌트를 조합하는 패턴은 `friendcard-split.md`가 InGame 쪽 `FriendCard`+`FriendCardBattleControl` 조합에서 이미 확립한 것과 동일하다.

### 2. 상태는 `FriendTabController` 하나가 소유, 진입점은 `EnterReplaceMode`/`ExitReplaceMode`

`MainMenuTabSlideController.SetPage`가 탭 클릭/스와이프 두 경로를 하나로 합류시킨 것과 같은 이유로, "교체 상태로 들어가는 경로(팝업의 선택 버튼)"와 "나가는 경로(드롭 성공/슬롯 클릭/배경 클릭)"를 각각 하나의 메서드로만 진입하게 한다.

```csharp
private enum FriendTabState { List, Replace }

private FriendTabState _state = FriendTabState.List;

private void EnterReplaceMode(int key)
{
    _state = FriendTabState.Replace;
    _friendCardList.SetActive(false);
    _replaceUI.SetActive(true);
    _replaceCard.Setup(key);
}

public void ExitReplaceMode()
{
    _state = FriendTabState.List;
    _replaceUI.SetActive(false);
    _friendCardList.SetActive(true);
}
```

드롭 성공(`RequestReplace`)/배경 클릭(`OnPanelBackgroundClicked`) 두 경로 모두 데이터 반영 여부와 무관하게 조건 없이 `ExitReplaceMode`로 합류한다.

### 3. 슬롯 교체는 `RequestReplace` 하나로, `UserData.SetFriends`를 거쳐 이벤트로 되돌아온다

드래그 드롭(`FriendSlot.OnDrop`)과 직접 클릭(`FriendSlot`이 구독하는 `_item.Clicked`) 둘 다 같은 진입점을 호출한다. 슬롯을 직접 갱신하지 않고 `UserData.SetFriends`만 호출하는 이유는 `plan-mainmenuscene-userdata-hud.md`와 동일 — "값이 실제로 바뀌는 지점"과 "갱신 신호"를 분리하지 않기 위해서다.

클릭 감지는 `FriendSlot`이 직접 처리하지 않는다 — 그리드 아이템과 동일한 `FriendListItem`(`_item`)의 `Clicked` 이벤트를 구독한다. 그리드 목록과 슬롯 둘 다 "카드 하나 표시 + 클릭 이벤트"라는 같은 모양이므로, `FriendCard`+`Button`+클릭 이벤트 조합을 슬롯 쪽에 따로 다시 정의하지 않는다.

```csharp
private void RequestReplace(int slotIndex, int key)
{
    if (_state != FriendTabState.Replace) return; // 슬롯 클릭은 List 상태에서도 들어올 수 있는 입력이라 가드 필요

    var friends = new List<int>(UserManager.Current.Friends);
    friends[slotIndex] = key;
    UserManager.Current.SetFriends(friends); // 내부에서 EventBus.Publish(new UserDataChanged())

    ExitReplaceMode();
}
```

`FriendTabController`는 `MainMenuHudView`와 동일하게 `Awake()`에서 `EventBus.Subscribe<UserDataChanged>(_ => RefreshSlots())`로 구독한다 — 이 화면이 직접 `SetFriends`를 호출한 경우든, 다른 시스템이 나중에 호출한 경우든 슬롯은 항상 같은 경로로 갱신된다(`Awake()` 전체 구성은 아래 "클래스 구조" 참고).

```csharp
private void RefreshSlots()
{
    var friends = UserManager.Current.Friends; // 항상 3개(UserData 기본값이자 SetFriends 호출부의 불변 조건)
    for (int i = 0; i < _slots.Length; i++)
        _slots[i].SetKey(friends[i]);
}
```

### 4. 선택 팝업(`FriendPickPopup`)은 `UIManager.Load`가 아니라 씬에 고정 배치된 자식

`UIManager.Show<T>`/`HideTop`(팝업 스택)이 존재하지만 실제로 쓰이는 곳이 없다 — `OptionPanel`조차 `UIManager.Load`로 인스턴스만 얻고 `Show()`/`SetActive`는 자체 메서드로 직접 연다(`MainMenuSceneManager.cs` 확인). 이 팝업은 여러 씬에서 재사용되는 것도 아니므로(친구 탭 안에서만 뜸), `Resources.Load` 기반 싱글톤 로딩을 끌어들일 이유 없이 `[SerializeField]`로 친구 탭 페이지의 자식 오브젝트를 직접 참조한다(요청자가 "이미 만들어져 있고"라 한 대상). 위치는 항상 고정이고 클릭한 카드 위치로 옮겨가지 않는다 — 단순 on/off(`SetActive`)로 충분하다.

```csharp
public class FriendPickPopup : MonoBehaviour
{
    [SerializeField] private FriendCard _friendCard;
    [SerializeField] private Button _selectButton;
    [SerializeField] private Button _closeButton;

    private Action _onSelect;

    // 씬에 기본 비활성 상태로 배치된 오브젝트를 전제한다(에디터 설정) — 여기서 SetActive(false)를 호출하면
    // Awake가 최초 Show()의 SetActive(true) 도중 지연 실행되어 그 활성화를 즉시 되돌려버린다.
    // 초기화 시점의 비활성 보장은 FriendTabController.Awake()가 대신 명시적으로 수행한다(핵심 설계 결정 3, RefreshSlots 인근).
    private void Awake()
    {
        _selectButton.onClick.AddListener(OnSelectButtonClicked);
        _closeButton.onClick.AddListener(OnCloseButtonClicked);
    }

    public void Show(int key, Action onSelect)
    {
        _friendCard.SetKey(key);
        _onSelect = onSelect;
        gameObject.SetActive(true);
    }

    private void OnSelectButtonClicked()
    {
        gameObject.SetActive(false);
        _onSelect?.Invoke();
    }

    // 선택 없이 팝업만 닫는다 — onSelect를 호출하지 않으므로 Replace 상태로 전환되지 않는다
    private void OnCloseButtonClicked() => gameObject.SetActive(false);
}
```

`_onSelect`는 매 클릭마다 `FriendTabController`가 새로 넘겨준다 — 팝업은 자기가 어떤 카드였는지만 알고, "선택되면 무엇을 할지"는 몰라도 되게 분리한다. `_closeButton`은 `_selectButton`과 달리 `_onSelect`를 호출하지 않고 그냥 닫기만 한다 — 배경 클릭으로 닫는 기능(아래 "이번 범위에서 제외" 참고)과는 별개로, 선택 없이 명시적으로 취소하는 경로다.

### 5. 드래그 카드는 `FriendCardMainControl` — `FriendCard`와 같은 오브젝트에 나란히 붙여 표시는 위임, 드래그/슬롯 결합만 담당

[friendcard-split.md](./plan-mainmenuscene-friendcard-split.md)가 InGame 쪽에서 확립한 패턴(표시는 `FriendCard`, 드래그는 `[RequireComponent(typeof(FriendCard))]`를 건 별도 컴포넌트)을 메인메뉴에도 그대로 적용한다. `FriendCardMainControl`는 `Key`를 따로 들고 있지 않고 같은 GameObject의 `FriendCard`를 `Data`로 참조만 한다.

`OnDrag`의 `RectTransformUtility.ScreenPointToLocalPointInRectangle` 계산은 `FriendCardBattleControl.OnDrag`(InGame)의 좌표 변환 로직을 그대로 재사용한다. 드롭 결과 처리는 "원래 자리로 되돌아가거나(실패), 슬롯에 흡수되어 사라지거나(성공, 이후 `ExitReplaceMode`가 `_replaceUI` 자체를 비활성화)" 둘 중 하나뿐이라 `HomeSlot`/`MoveToSlot`(배틀 hand 슬롯 개념)은 필요 없다.

```csharp
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
```

`FriendCard`와 `FriendCardMainControl`는 씬에 고정 배치된 `ReplaceCard` 오브젝트(아래 "Unity 씬/오브젝트 구성" 참고) 하나에 함께 붙는다 — 요청자가 지정한 "`FriendCard`와 메인메뉴용 이벤트 클래스를 나란히 붙여 쓴다"는 조합 방식 그대로다.

### 6. 배경 클릭 취소는 패널 배경의 `Button`으로, 별도 오버레이를 만들지 않는다

"슬롯 아닌 배경 클릭 시 취소"를 위해 전체를 덮는 투명 오버레이를 새로 추가하는 방법도 검토했지만, 슬롯/교체카드가 배경보다 하이어라키상 나중(위)에 그려지는 한 배경 자체의 `Button`으로 충분하다 — Unity uGUI는 겹친 그래픽 중 레이캐스트 최상단(하이어라키상 뒤쪽 형제)만 이벤트를 받으므로, 슬롯/카드를 클릭하면 그쪽에서 소비되고 나머지 빈 영역 클릭만 배경 `Button`까지 도달한다. 새 오브젝트를 추가하지 않고 이미 있는 마스크 배경 `Image`에 `Button` 컴포넌트만 얹는다.

```csharp
private void OnPanelBackgroundClicked()
{
    if (_state != FriendTabState.Replace) return; // List 상태에서는 아무 의미 없는 클릭
    ExitReplaceMode();
}
```

---

## 클래스 구조

```
CardTable (기존 파일 수정, Data/Table/)
└── GetAll() : IReadOnlyList<CardTableData>   ← 신규, Rows 그대로 노출(목록 전체 순회용)

FriendTabController : MonoBehaviour           (신규, MainMenu/)
├── _gridContent : Transform                  ← [SerializeField], GridLayoutGroup 부모
├── _listItemPrefab : FriendListItem          ← [SerializeField]
├── _friendCardList : GameObject              ← [SerializeField], 그리드(카드 목록)만 감싼 오브젝트. Replace 상태에서 SetActive(false) — ScrollView 전체가 아니라 이 목록만 숨긴다(슬롯은 ScrollView 안에 있어도 계속 보여야 함)
├── _slots : FriendSlot[3]                    ← [SerializeField]
├── _pickPopup : FriendPickPopup              ← [SerializeField]
├── _replaceUI : GameObject                   ← [SerializeField], 기본 비활성
├── _replaceCard : FriendCardMainControl      ← [SerializeField]
├── _panelBackgroundButton : Button           ← [SerializeField]
├── _subs : CompositeDisposable
├── _state : FriendTabState                   ← private, List/Replace
├── Awake()                                   ← PopulateList + RefreshSlots + _pickPopup/_replaceUI 초기 비활성화 + UserDataChanged 구독 + 배경 버튼 연결
├── PopulateList()                            ← private, CardTable.Instance.GetAll() 순회하며 FriendListItem 생성
├── RefreshSlots()                            ← private, UserManager.Current.Friends → 슬롯 3개 SetKey
├── OnListItemClicked(FriendListItem item)    ← private, _pickPopup.Show(item.Key, () => EnterReplaceMode(item.Key))
├── EnterReplaceMode(int key)                 ← private
├── ExitReplaceMode()                         ← public(FriendSlot/배경 버튼에서 호출)
├── RequestReplace(int slotIndex, int key)    ← private, Replace 상태 가드 + SetFriends + ExitReplaceMode
├── OnSlotClicked(int slotIndex)              ← public, FriendSlot._item.Clicked → RequestReplace(slotIndex, _replaceCard.Data.Key)
├── OnSlotDropped(int slotIndex, int key)     ← public, FriendSlot.OnDrop → RequestReplace(slotIndex, key)
├── OnPanelBackgroundClicked()                ← private
└── OnDestroy()                               ← _subs.Dispose()

FriendListItem : MonoBehaviour                (신규, MainMenu/)
├── _friendCard : FriendCard                  ← [SerializeField], 자식
├── _button : Button                          ← [SerializeField]
├── Key : int => _friendCard.Key              ← 위임 프로퍼티(별도 상태 없음)
├── event Action<FriendListItem> Clicked
├── SetKey(int key)                            ← _friendCard.SetKey
└── Awake()                                    ← _button.onClick.AddListener(() => Clicked?.Invoke(this))

FriendSlot : MonoBehaviour, IDropHandler       (신규, MainMenu/)
├── _index : int                              ← [SerializeField], 0~2, UserData.Friends 인덱스와 1:1
├── _item : FriendListItem                    ← [SerializeField], FriendCard+Button 조합을 통째로 재사용(별도 필드로 다시 들고 있지 않음)
├── _controller : FriendTabController         ← [SerializeField] (인스펙터 직결, 씬 내 유일 인스턴스라 순환 참조 우려 없음)
├── SetKey(int key)                            ← _item.SetKey
├── Awake()                                    ← _item.Clicked += _ => _controller.OnSlotClicked(_index)
└── OnDrop(PointerEventData eventData)         ← eventData.pointerDrag의 FriendCardMainControl 획득 → NotifyDropped() + _controller.OnSlotDropped(_index, card.Data.Key)

FriendPickPopup : MonoBehaviour               (신규, MainMenu/, 씬에 고정 배치된 자식 — "이미 만들어져 있는 선택 panel")
├── _friendCard : FriendCard                  ← [SerializeField]
├── _selectButton : Button                    ← [SerializeField]
├── _closeButton : Button                     ← [SerializeField]
├── Awake()                                    ← _selectButton/_closeButton onClick 연결(초기 비활성화는 FriendTabController.Awake()가 담당)
├── Show(int key, Action onSelect)
├── OnSelectButtonClicked()                    ← private, SetActive(false) + onSelect 호출
└── OnCloseButtonClicked()                     ← private, SetActive(false)만(onSelect 미호출)

FriendCardMainControl : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler   (신규, MainMenu/, FriendCard와 같은 오브젝트에 부착)
├── [RequireComponent(typeof(FriendCard))]
├── [RequireComponent(typeof(CanvasGroup))]
├── _dragLayer : Transform                    ← [SerializeField]
├── Data : FriendCard { get; }                ← Awake에서 GetComponent 캐싱, Key는 Data.Key로 조회(복제 없음)
├── Setup(int key)                             ← Data.SetKey(key)
├── NotifyDropped()
└── (드래그 3종 핸들러 — 위 "핵심 설계 결정 5" 참고)
```

---

## 파일 구성

```
Assets/
├── Prefabs/
│   └── FriendListItem.prefab            ← 신규, FriendCard 자식 + Button + FriendListItem.cs (그리드 목록과 3개 슬롯이 이 프리팹 하나를 공유)
└── Scripts/
    ├── Data/
    │   └── Table/
    │       └── CardTable.cs             ← 기존 파일 수정 (GetAll() 추가)
    └── MainMenu/
        ├── FriendTabController.cs       ← 신규
        ├── FriendListItem.cs            ← 신규
        ├── FriendSlot.cs                ← 신규
        ├── FriendPickPopup.cs           ← 신규
        └── FriendCardMainControl.cs     ← 신규
```

`MainMenu/` 아래 배치 — `MainMenuTabSlideController`/`MainMenuHudView`와 동일하게 이 씬에만 등장하는 구체적인 UI 로직이기 때문(`plan-prefab.md`가 확립한 원칙 재사용). `FriendCard`/`FriendCardBattleControl`는 `InGame/`에 그대로 둔다(배틀 전용 `FriendCardBattleControl`와 분리됐을 뿐 `FriendCard` 자체의 소속은 바뀌지 않음).

---

## Unity 씬/오브젝트 구성

```
[Scene: MainMenu]
└── Canvas
    └── TabSlide/ScrollView/Content/Page_Friend (tabslide.md의 페이지 중 하나, 실제 이름은 씬 정리 시 확정)
        └── FriendPanel (FriendTabController.cs, Image(마스크 배경)+Button ← _panelBackgroundButton)
            ├── ScrollView (ScrollRect Vertical Only, Mask)  ← 덱 슬롯 + 친구카드 목록을 함께 담는 스크롤 영역, 이 오브젝트 자체는 Replace 상태에서도 계속 켜져 있음
            │   └── Viewport
            │       └── Content
            │           ├── Slots (HorizontalLayoutGroup, 항상 노출 — Replace 상태에서도 꺼지지 않음)
            │           │   ├── Slot_0 ~ Slot_2 (FriendSlot.cs, FriendListItem.prefab 인스턴스를 자식으로 배치 — 그리드와 동일한 프리팹 재사용)
            │           └── FriendCardList (GridLayoutGroup)      ← _friendCardList이자 _gridContent, FriendListItem 인스턴스가 런타임에 채움, Replace 상태에서만 SetActive(false)
            ├── PickPopup (FriendPickPopup.cs, 기본 비활성, FriendCard 자식 + SelectButton + CloseButton)  ← 요청자가 이미 배치해 둔 "선택 panel"
            └── ReplaceUI (기본 비활성)                            ← _replaceUI
                ├── GuideText (안내 문구, TextMeshProUGUI)
                └── ReplaceCard (FriendCard + FriendCardMainControl + CanvasGroup, 같은 오브젝트에 나란히 부착 — Friend 자식으로 감싸지 않음)
```

`_dragLayer`는 `ReplaceCard`가 드래그 중 다른 UI(슬롯 포함) 위로 그려지도록 `FriendPanel` 또는 그 상위 `Canvas`를 지정한다 — `InGameSceneManager`가 `_dragLayer`를 별도 최상위 오브젝트로 두는 것과 같은 이유.

---

## 이번 범위에서 제외

- **선택 팝업을 배경 클릭으로 닫는 기능**: `_closeButton`(전용 닫기 버튼)으로 선택 없이 닫는 경로는 생겼지만, 배경/다른 위치를 클릭해서 닫는 것은 여전히 범위 밖이다. 다른 카드를 다시 클릭하면 같은 팝업 인스턴스가 새 key/위치로 재구성될 뿐이라 UX상 막히지 않는다.
- **목록 정렬/필터(보유 여부, 능력별 등)**: `CardTable` 전체 행을 그대로 노출한다. "보유 카드"라는 개념 자체가 현재 데이터 모델에 없음(`UserData`엔 `Friends` 3개만 있고 소유 카드 풀이 없음).
- **슬롯이 비어있는 상태 처리**: `UserData.Friends`가 항상 3개를 유지한다는 전제(기본값도 3개, `SetFriends` 호출부도 이 문서뿐) 위에서 설계했다. 추후 슬롯 수가 가변적으로 바뀌면 이 문서를 다시 검토해야 한다.
- **현재 슬롯에 들어있는 카드를 목록에서 강조 표시**: 명세에 없음.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 팝업이 열린 채로 다른 목록 카드 클릭 | 팝업은 씬에 고정된 단일 인스턴스라 `Show()`가 새 key/위치로 즉시 재구성 — 자연스럽게 이동 |
| List 상태에서 슬롯을 클릭(교체 카드가 없는 상태) | `RequestReplace`가 `_state != Replace`에서 조기 반환 — 아무 일도 일어나지 않음 |
| 드래그 중 슬롯이 아닌 곳(목록/배경)에 드롭 | `FriendCardMainControl.OnEndDrag`에서 `_wasDropped == false` → 원위치로 `DOAnchorPos` 복귀, Replace 상태 유지 |
| Replace 상태에서 패널 배경 클릭 | `ExitReplaceMode()` — 목록 재표시, `UserData` 변경 없음 |
| 이미 슬롯에 들어있는 카드와 같은 key로 "교체" | 값 동일 여부를 검사하지 않고 그대로 `SetFriends` 호출 — `UserDataChanged`는 발행되지만 실질 변화 없음(허용, 별도 방지 로직 없음) |
| Replace 상태 도중 다른 시스템이 `UserManager.Current.SetFriends`를 호출(예: 서버 동기화) | `UserDataChanged` 구독으로 슬롯은 즉시 갱신되지만 Replace 상태 자체는 유지 — 사용자가 배경을 눌러 취소하거나 드롭을 완료해야 함 |
| 목록이 비활성화된(`FriendCardList` `SetActive(false)`) Replace 상태에서 목록 카드 클릭 시도 | 비활성 오브젝트는 레이캐스트 대상이 아니므로 애초에 입력이 발생하지 않음(같은 `ScrollView` 안의 `Slots`는 계속 활성 상태라 슬롯 클릭/드롭은 영향받지 않음) |
| `FriendListItem`/`FriendSlot` 인스펙터 참조 누락 | `FriendCard.cs`/`MainMenuHudView.cs`와 동일한 기존 관례 — 방어 코드 없이 `NullReferenceException`으로 즉시 드러남 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 친구 탭 최초 진입 | `_gridContent`에 `CardTable` 전체 행 수만큼 `FriendListItem` 생성, 3개 슬롯이 `UserData.Friends` 값으로 채워짐 |
| 2 | 목록의 카드 하나 클릭 | `PickPopup`이 고정 위치에서 해당 key로 활성화(위치 이동 없음) |
| 3 | `PickPopup`의 "선택" 버튼 클릭 | 팝업 비활성화, `FriendCardList`(목록)만 비활성화되고 `Slots`는 계속 보임, `ReplaceUI` 활성화되고 `ReplaceCard`가 선택한 key로 표시 |
| 4 | `ReplaceCard`를 `Slot_1`으로 드래그해 드롭 | `UserData.Friends[1]`이 선택 key로 교체, `UserDataChanged` 발행, 슬롯 3개 갱신, `ReplaceUI` 비활성화 + 목록 재활성화 |
| 5 | Replace 상태에서 `Slot_2`를 드래그 없이 클릭 | `UserData.Friends[2]`가 `ReplaceCard.Data.Key`로 즉시 교체 — 결과는 시나리오 4와 동일하게 종료 |
| 6 | `ReplaceCard`를 드래그하다 슬롯이 아닌 배경에 드롭 | 카드가 원래 위치로 애니메이션 복귀, Replace 상태 유지(교체 안 됨) |
| 7 | Replace 상태에서 슬롯/카드가 아닌 패널 배경 클릭 | `ReplaceUI` 비활성화, 목록 재활성화, `UserData` 변경 없음 |
| 8 | 시나리오 4 직후 다시 목록의 다른 카드 클릭 → 다른 슬롯으로 교체 | 두 번째 교체도 동일하게 동작, 최종 `UserData.Friends` 3개 모두 각각의 마지막 선택값과 일치 |
| 9 | `PickPopup`이 열린 상태에서 "선택"이 아니라 "닫기" 버튼 클릭 | 팝업만 비활성화, `EnterReplaceMode` 미호출(`FriendCardList`/`ReplaceUI` 상태 그대로), `UserData` 변경 없음 |

---

## 구현 시 주의사항

- **`FriendCard`는 [friendcard-split.md](./plan-mainmenuscene-friendcard-split.md) 적용 이후 버전(데이터 전용, 드래그 없음)이어야 한다** — 분리 전 버전은 `OnBeginDrag`가 `InGameSceneManager.Instance`를 참조해 메인메뉴에서 크래시한다.
- **`RequestReplace`는 반드시 `_state == Replace`일 때만 동작하도록 가드한다** — 슬롯의 `_item.Clicked`는 List 상태에서도 씬에 존재하는 한 이론상 발생 가능(비활성화되지 않으므로)하니, 상태 가드가 없으면 List 상태에서 슬롯을 눌렀을 때 `_replaceCard.Data.Key`(초기값 0 또는 이전 값)로 잘못 교체될 수 있다.
- **슬롯 배열 순서(`_slots[0..2]`)와 `UserData.Friends` 인덱스가 항상 일치해야 한다** — `MainMenuTabSlideController`의 `_pages`/`_tabButtons` 인덱스 매칭 관례와 동일.
- **`UIManager.Show`/`HideTop`(팝업 스택)을 새로 끌어들이지 않는다** — 현재 코드베이스에서 실제로 쓰이는 곳이 없고(`OptionPanel`도 직접 `SetActive`), 이 팝업도 씬에 고정 배치된 단일 인스턴스로 충분하다.
- **드래그 실패 시 복귀는 DOTween(`DOAnchorPos`)을 쓴다** — 코루틴 Lerp를 새로 만들지 않는다(`plan-mainmenuscene-tabslide-dotween.md`로 이미 DOTween이 프로젝트 표준 애니메이션 수단으로 확정됨).
- **`FriendCardMainControl`의 원위치(`_originAnchoredPosition`/`_originParent`)는 `Awake` 1회만 캐싱한다** — `Setup`마다 다시 읽지 않는다. 매번 다시 읽으면 드래그 중간에 파괴되지 않고 남아있는 상태에서 위치가 흐트러질 여지가 생긴다.
- **`FriendCardMainControl`는 `Key`를 복제하지 않고 `Data.Key`로 `FriendCard`에 위임한다** — `Setup(key)`도 내부적으로 `Data.SetKey(key)`만 호출(위 "핵심 설계 결정 5" 참고).
- **`CardTable.GetAll()`은 `Rows`(내부 `List<TData>`)를 그대로 반환하는 얕은 노출이다** — 호출부(`FriendTabController.PopulateList`)는 이 리스트를 변형하지 않는다(정렬/필터 없음, 위 "제외" 항목과 연결).

---

## 구현 후 체크리스트

- [x] (선행) [plan-mainmenuscene-friendcard-split.md](./plan-mainmenuscene-friendcard-split.md) 체크리스트 완료 — `FriendCard`(데이터 전용) 확보
- [x] `CardTable.cs`: `GetAll() : IReadOnlyList<CardTableData>` 추가
- [x] `FriendListItem.cs` 작성 (`Assets/Scripts/MainMenu/`)
- [x] `FriendSlot.cs` 작성
- [x] `FriendPickPopup.cs` 작성 (`Awake`에서 `SetActive(false)` 호출 안 함 — 씬에 기본 비활성으로 배치된 오브젝트 전제, 최초 `Show()` 시 지연 실행되는 `Awake`가 활성화를 되돌리는 문제 방지. 초기 비활성화는 `FriendTabController.Awake()`가 대신 명시적으로 보장, 선택 없이 닫는 `_closeButton`/`OnCloseButtonClicked` 추가)
- [x] `FriendCardMainControl.cs` 작성
- [x] `FriendTabController.cs` 작성 (`Awake()`에서 `_pickPopup.gameObject.SetActive(false)`/`_replaceUI.SetActive(false)` 명시 호출 추가)
- [ ] `FriendListItem.prefab` 제작(`FriendCard` 자식 + `Button` + `FriendListItem.cs`) — Unity 에디터 작업
- [ ] 씬: 친구 탭 페이지에 `ScrollView`(`Slots`+`FriendCardList` 포함)/`PickPopup`(`SelectButton`+`CloseButton` 인스펙터 연결 포함)/`ReplaceUI` 배치, 스크립트 인스펙터 연결(`_slots`/`_friendCardList`/`_gridContent`/`_pickPopup`/`_replaceCard`/`_panelBackgroundButton` 등) — Unity 에디터 작업, `ReplaceCard`는 `FriendCard`+`FriendCardMainControl`+`CanvasGroup`을 한 오브젝트에 부착
- [ ] 테스트 시나리오 9개 검증
- [ ] (추후) 팝업 배경 클릭으로 닫기 등 부가 UX 필요 시 `FriendPickPopup`에 배경 클릭 콜백 추가
- [ ] (추후) "보유 카드" 개념이 생기면 `PopulateList`에서 `CardTable.GetAll()` 대신 소유 목록으로 필터링
