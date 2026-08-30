# 친구탭 장착 카드 표시 구현 계획

> 상위 문서: [메인메뉴 친구 선택/교체 구현 계획](./plan-mainmenuscene-friendselect.md) (`FriendTabController.PopulateList`/`FriendSlot`/`FriendPickPopup`의 표시 규칙 확장)
> 관련 문서: [유저 덱 3종 저장 구현 계획](../../04-userdata/plan-userdata-multideck.md) (`SelectDeck`/`SetFriends` 둘 다 이번 갱신의 트리거)
> 의존 관계: `JungleDice.Core.User.UserManager`/`UserData`(`Friends`), `JungleDice.Core.Event.EventBus`/`UserDataChanged`, `JungleDice.Data.Table.CardTable`
> 범위: (1) 친구탭 카드 목록에서 현재 선택된 덱(3장)에 포함된 카드를 숨기고, 덱 전환/카드 교체 시 즉시 갱신한다. (2) List 상태에서 슬롯(이미 장착된 카드)을 클릭하면 `FriendPickPopup`을 읽기 전용(선택 버튼 비활성)으로 띄운다. 숨김/표시 전환 애니메이션, "보유 카드" 개념 등은 범위 밖.

---

## 배경 / 문제 인식

`FriendTabController.PopulateList()`는 `CardTable` 전체(19종)를 그대로 나열하고, 이미 현재 덱에 들어있는 카드도 목록에서 계속 클릭 가능하다. 카드 교체 자체에는 중복 방지 로직이 없어(`plan-mainmenuscene-friendselect.md`의 "이번 범위에서 제외" 참고), 이미 쓰고 있는 카드를 다시 골라도 그대로 진행된다. 이미 장착된 카드는 목록에서 골라봤자 의미가 없으므로 숨기기로 한다.

다만 목록에서 숨기면 "이 슬롯에 지금 어떤 카드가 들어있는지" 상세(설명/능력치)를 확인할 방법이 사라진다 — 슬롯(`FriendSlot`) 자체는 이미지만 보여줄 뿐 `FriendCard`의 이름/설명까지는 노출하지 않는다. List 상태에서 슬롯을 클릭하면 목록 카드를 클릭했을 때와 동일한 `FriendPickPopup`으로 상세를 볼 수 있게 하되, "선택"은 의미가 없으므로(이미 장착된 카드를 같은 슬롯에 다시 넣는 것) 버튼을 비활성화한다.

---

## 설계 목표

- 현재 선택된 덱(`UserManager.Current.Friends`)에 포함된 카드는 목록에서 숨긴다 — 그리드가 빈 칸 없이 재배치되도록
- 덱 전환(`SelectDeck`)/카드 교체(`SetFriends`) 등 `UserDataChanged`가 발행되는 모든 경로에서 별도 분기 없이 동일하게 즉시 갱신
- 목록 아이템을 재생성하지 않고(`PopulateList`는 `Awake` 1회만 실행), 기존 인스턴스의 활성 상태만 토글
- List 상태에서 슬롯 클릭 시 `FriendPickPopup`으로 상세를 보여주되, 선택 버튼은 비활성화해 "교체 시작" 진입점과 명확히 구분한다 — 팝업 컴포넌트를 새로 만들지 않고 기존 것을 재사용

---

## 핵심 설계 결정

### 1. 생성된 `FriendListItem`을 캐시 — 갱신할 때 `CardTable`을 다시 순회하지 않는다

```csharp
private readonly List<FriendListItem> _listItems = new();

private void PopulateList()
{
    foreach (var data in CardTable.Instance.GetAll())
    {
        var item = Instantiate(_listItemPrefab, _gridContent);
        item.SetKey(data.key);
        item.Clicked += OnListItemClicked;
        _listItems.Add(item);
    }

    LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
}
```

### 2. `RefreshListVisibility` — `HashSet<int>` 멤버십 판정 후 `SetActive` 토글

```csharp
private void RefreshListVisibility()
{
    var friends = new HashSet<int>(UserManager.Current.Friends);
    foreach (var item in _listItems)
        item.gameObject.SetActive(!friends.Contains(item.Key));

    LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
}
```

`GridLayoutGroup`은 `activeInHierarchy`가 `false`인 자식을 레이아웃 계산에서 제외하므로, `SetActive(false)`만으로 빈 칸 없이 재배치된다 — 별도 정렬 로직이 필요 없다. 다만 그 리빌드가 프레임 끝까지 지연되므로, `PopulateList`의 초기 생성 직후와 동일한 이유로 `LayoutRebuilder.ForceRebuildLayoutImmediate(_content)`를 여기서도 호출한다.

`HashSet`을 쓰는 이유: `Friends`는 3개뿐이라 `List.Contains`와 성능 차이는 없지만, 19종 전체를 순회하는 루프 안에서 "포함 여부 판정"이라는 의도를 코드로 더 명확히 드러낸다.

### 3. 갱신 트리거는 기존 `UserDataChanged` 구독에 합류 — 새 이벤트 추가하지 않음

```csharp
private void Awake()
{
    PopulateList();
    RefreshSlots();
    RefreshListVisibility();

    // ... 기존 초기화 ...

    _subs.Add(EventBus.Subscribe<UserDataChanged>(_ =>
    {
        RefreshSlots();
        RefreshListVisibility();
    }));

    // ... 덱 버튼 연결 ...
}
```

덱 전환(`SelectDeck`)과 카드 교체(`SetFriends`) 모두 동일하게 `UserDataChanged`를 발행하므로(`plan-userdata-multideck.md`), 어느 경로로 `Friends`가 바뀌었는지 구분하지 않고 하나의 구독에서 슬롯 갱신과 목록 가시성 갱신을 함께 수행한다.

### 4. List 상태의 슬롯 클릭 — `FriendPickPopup`을 읽기 전용으로 재사용, 새 컴포넌트 없음

`FriendPickPopup.Show`에 `selectable` 플래그를 추가해, 선택 버튼의 `interactable`만 끈다(회색 처리는 `Button`의 기본 `ColorTint` 전환이 `interactable == false`일 때 자동으로 처리 — 별도 색상 코드 불필요).

```csharp
public void Show(int key, Action onSelect, bool selectable = true)
{
    _friendCard.SetKey(key);
    _onSelect = onSelect;
    _selectButton.interactable = selectable;
    gameObject.SetActive(true);
}
```

`FriendTabController.OnSlotClicked`는 상태에 따라 분기한다 — List 상태면 읽기 전용 팝업, Replace 상태면 기존 교체 흐름 그대로:

```csharp
public void OnSlotClicked(int slotIndex)
{
    if (_state == FriendTabState.List)
    {
        _pickPopup.Show(_slots[slotIndex].Key, null, selectable: false);
        return;
    }

    RequestReplace(slotIndex, _replaceCard.Data.Key);
}
```

슬롯의 현재 key를 읽기 위해 `FriendSlot`에 위임 프로퍼티를 추가한다(`FriendListItem.Key`와 동일한 패턴):

```csharp
public int Key => _item.Key;
```

기존 목록 카드 클릭 경로(`OnListItemClicked` → `_pickPopup.Show(item.Key, () => EnterReplaceMode(item.Key))`)는 `selectable` 기본값(`true`)을 그대로 타므로 변경이 필요 없다.

---

## 클래스 구조

```
FriendTabController                      (기존 파일 수정, MainMenu/)
├── _listItems : List<FriendListItem>     ← 신규, PopulateList가 생성한 아이템 캐시
├── PopulateList()                        ← 생성 시 _listItems에 추가하도록 수정
├── RefreshListVisibility()               ← 신규, 현재 덱 포함 여부로 SetActive 토글
├── Awake()                               ← RefreshListVisibility() 최초 호출 + UserDataChanged 구독에 합류
└── OnSlotClicked(int slotIndex)          ← List 상태 분기 추가(읽기 전용 팝업), Replace 분기는 기존 RequestReplace 유지

FriendSlot                                (기존 파일 수정, MainMenu/)
└── Key : int { get } => _item.Key         ← 신규, 위임 프로퍼티(FriendListItem.Key와 동일 패턴)

FriendPickPopup                           (기존 파일 수정, MainMenu/)
└── Show(int key, Action onSelect, bool selectable = true)   ← selectable 매개변수 추가, _selectButton.interactable 반영
```

---

## 파일 구성

```
Assets/Scripts/MainMenu/
├── FriendTabController.cs   — 기존 파일 수정
├── FriendSlot.cs            — 기존 파일 수정
└── FriendPickPopup.cs       — 기존 파일 수정
```

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 같은 카드가 덱 안에 2개 이상 들어있는 경우(현재 중복 방지 로직 없음) | 목록 숨김은 `HashSet` 멤버십 판정이라 개수와 무관하게 1번만 숨김 처리됨 |
| Replace 모드 진입 중(`_friendCardList` 전체가 `SetActive(false)`로 숨겨짐) | 목록 자체가 안 보이므로 개별 아이템 가시성 갱신은 화면에 영향 없음 — 다음에 목록이 다시 보일 때는 이미 최신 상태 |
| 덱 전환으로 이전 덱에만 있던 카드가 새 덱엔 없는 경우 | 그 카드는 목록에 다시 나타남(`HashSet`이 매번 새 `Friends` 기준으로 재계산되므로 자동 처리) |
| 앱 최초 실행 시 기본 덱(1004,1016,1019) | `Awake`에서 `PopulateList` 직후 `RefreshListVisibility` 호출로 최초 목록에도 바로 반영 |
| List 상태에서 슬롯 클릭 → 팝업이 이미 열려있는 상태(목록 카드 클릭으로) | `PickPopup`은 씬에 고정된 단일 인스턴스라 `Show()`가 새 key/`selectable` 값으로 즉시 재구성됨(`plan-mainmenuscene-friendselect.md`의 기존 엣지 케이스와 동일 패턴) |
| 읽기 전용으로 뜬 팝업에서 "닫기" 클릭 | 기존 `OnCloseButtonClicked` 그대로 — `onSelect` 호출 없이 닫힘(애초에 `null`이라 호출해도 무해하지만 버튼이 비활성이라 클릭 자체가 안 됨) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 친구탭 최초 진입 | 목록에서 기본 덱 3장(1004,1016,1019)이 보이지 않고 나머지 16종만 표시 |
| 2 | 목록에서 카드 A 선택 → 슬롯 교체 완료 | 카드 A가 목록에서 사라지고, 교체되어 빠진 카드가 목록에 다시 나타남 |
| 3 | 덱 버튼으로 다른 덱(다른 3장 구성)으로 전환 | 이전 덱 카드들은 다시 보이고, 새 덱의 3장이 숨겨짐 |
| 4 | 같은 카드를 두 슬롯에 중복으로 넣은 상태(엣지 케이스) | 목록에서 해당 카드 1장만 숨김(중복 개수와 무관) |
| 5 | List 상태에서 슬롯 하나 클릭 | `FriendPickPopup`이 해당 슬롯의 카드 key로 열리고, 선택 버튼이 회색으로 비활성화됨 |
| 6 | 시나리오 5 상태에서 선택 버튼 클릭 시도 | `interactable == false`라 클릭 이벤트 자체가 발생하지 않음 — `EnterReplaceMode` 미호출 |
| 7 | 시나리오 5 상태에서 닫기 버튼 클릭 | 팝업만 닫히고 `_state`/`UserData` 변경 없음 |
| 8 | Replace 상태에서 슬롯 클릭(기존 동작) | 여전히 `RequestReplace` 경로로 진행 — List 상태 분기가 Replace 동작에 영향 없음 |

---

## 구현 시 주의사항

- **`SetActive(false)`는 인스턴스를 끄는 것이지 파괴가 아니다** — `_listItems` 캐시는 계속 유효하며 재생성하지 않는다.
- **`RefreshListVisibility`는 `_gridContent`가 아니라 `_content`(상위 ScrollView Content)에 리빌드를 호출한다** — `GridLayoutGroup` 자신의 높이 변화가 상위 `VerticalLayoutGroup`+`ContentSizeFitter`에 전파돼야 스크롤 범위가 맞는다.
- **`HashSet`은 매 호출마다 새로 생성한다** — `Friends`가 3개뿐이라 캐싱 이점이 없고, 캐싱하면 무효화 시점을 따로 관리해야 해 오히려 복잡해진다.
- **`FriendPickPopup.Show`의 `selectable` 기본값은 반드시 `true`로 둔다** — 기존 호출부(`OnListItemClicked`)가 매개변수를 추가하지 않고도 그대로 컴파일/동작해야 한다.
- **읽기 전용 팝업은 `onSelect`로 `null`을 넘긴다** — 버튼이 비활성이라 실제로 호출될 일은 없지만, `selectable: false`인데 콜백을 넘기는 건 의도를 헷갈리게 하므로 명시적으로 `null`을 전달한다.
- **`OnSlotClicked`의 List 분기는 `RequestReplace`를 거치지 않는다** — 데이터 변경이 전혀 없는 조회 전용 동작이므로 `UserData`/`UserDataChanged`와 무관하다.

---

## 구현 후 체크리스트

- [x] `FriendTabController.cs`: `_listItems` 캐시 추가, `PopulateList`에서 캐시 채우기
- [x] `RefreshListVisibility()` 작성, `Awake()` 최초 호출 + `UserDataChanged` 구독에 연결
- [x] `FriendSlot.cs`: `Key` 위임 프로퍼티 추가
- [x] `FriendPickPopup.cs`: `Show`에 `selectable` 매개변수 추가
- [x] `FriendTabController.OnSlotClicked`: List 상태 분기(읽기 전용 팝업) 추가
- [ ] 테스트 시나리오 8개 검증 — Unity 에디터 플레이 모드 확인 필요
