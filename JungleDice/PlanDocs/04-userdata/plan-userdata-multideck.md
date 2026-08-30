# 유저 덱 3종 저장 구현 계획

> 상위 문서: [UserData 구현 계획](./plan-userdata.md) (`Friends`/`SetFriends`가 이번 문서의 확장 대상)
> 관련 문서: [메인메뉴 친구 선택/교체 구현 계획](../02-scenemanager/mainmenuscene/plan-mainmenuscene-friendselect.md) (`FriendTabController`에 덱 선택 버튼 3개 추가), [모험탭 덱 미리보기 구현 계획](../02-scenemanager/mainmenuscene/plan-mainmenuscene-adventuredeck.md) (`Friends` 소비 주체 중 하나 — 변경 영향 없음을 이번 문서에서 확인)
> 의존 관계: `JungleDice.Core.Event.EventBus`/`UserDataChanged`(기존 발행 경로 재사용), `JungleDice.MainMenu.FriendTabController`(덱 선택 버튼 추가 대상)
> 범위: `UserData`가 친구 덱 하나(`_friends`)만 저장하던 것을 3개 조합(`_decks`)으로 확장하고, 현재 선택된 덱 인덱스(`_currentDeckIndex`)를 추가한다. `FriendTabController`에 덱 선택 버튼 3개를 연결해 `SelectDeck(int)`을 호출하게 한다. 버튼 배치·강조 표시 등 실제 씬/시각 작업은 범위 밖.

---

## 배경 / 문제 인식

현재 `UserData._friends`는 3장짜리 조합 하나만 저장한다. 유저가 상황별로 여러 덱 조합을 만들어두고 골라 쓰는 기획을 넣으려면, 지금 구조(`List<int>` 하나)로는 "조합을 저장"할 수만 있고 "여러 조합 중 선택"이 불가능하다.

---

## 설계 목표

- 기존 `Friends`/`SetFriends` 소비 주체(`FriendTabController`, `InGameSceneManager`, `MainMenuSceneManager`)는 코드 변경 없이 그대로 동작해야 한다 — "지금 선택된 덱"이라는 개념이 추가될 뿐, 그 값을 읽는 방식(`Friends`)은 그대로다.
- 덱 개수는 3개로 고정한다 — 친구 슬롯이 항상 3개인 것과 같은 고정 개수 관례를 재사용한다.
- 덱 전환도 기존 `UserDataChanged` 이벤트 경로를 그대로 태운다 — 새 이벤트를 만들지 않는다.
- `FriendTabController`는 버튼 클릭 시 `UserManager.Current.SelectDeck(index)` 호출 하나만 하면 되고, 슬롯 갱신을 직접 챙기지 않는다(기존 구독이 알아서 처리).

---

## 핵심 설계 결정

### 1. `_friends: List<int>` → `_decks: List<int[]>`(3개 고정) + `_currentDeckIndex`, `Friends`는 선택된 덱으로 위임

별도 `Deck` 래퍼 클래스도 검토했으나, 지금은 키 리스트 하나 외에 덱에 딸린 상태(이름 등)가 없어 래핑할 이유가 없다(YAGNI). 덱 내부 리스트 타입은 `List<int>`가 아니라 `int[]`를 쓴다 — 덱 하나는 "3장 통째로 교체"만 일어나고 개별 항목을 늘리거나 줄이는 연산이 없으므로, `Add`/`Remove`가 가능한 `List<int>`보다 크기가 고정된 `int[]`가 "항상 3장"이라는 불변식을 더 잘 드러낸다.

```csharp
[SerializeField] private List<int[]> _decks = new()
{
    new[] { 1004, 1016, 1019 },
    new[] { 1004, 1016, 1019 },
    new[] { 1004, 1016, 1019 },
};
[SerializeField] private int _currentDeckIndex;

public IReadOnlyList<int> Friends => _decks[_currentDeckIndex];
public int CurrentDeckIndex => _currentDeckIndex;
public int DeckCount => _decks.Count;
```

기존 `Friends`의 타입(`IReadOnlyList<int>`)과 "항상 3개"라는 불변식은 그대로 유지된다 — 호출부는 몇 번째 덱인지 몰라도 되고, `Friends`는 항상 "지금 쓸 3장"이다(`int[]`도 `IReadOnlyList<int>`를 구현하므로 프로퍼티 타입 변경 없음).

### 2. `SetFriends`는 여전히 "현재 선택된 덱"을 수정 — 시그니처/호출부 변경 없음

`FriendTabController.RequestReplace`가 `UserManager.Current.SetFriends(friends)`를 호출하는 지점은 그대로 둔다. 내부 구현은 `_friends`를 직접 고치던 것과 달리, `_decks[_currentDeckIndex]` 슬롯 자체를 새 배열로 통째로 교체한다(`int[]`는 `Clear`/`AddRange`가 없으므로 자연스럽게 이 방식이 됨).

```csharp
public void SetFriends(IEnumerable<int> cardIds)
{
    _decks[_currentDeckIndex] = cardIds.ToArray();
    EventBus.Publish(new UserDataChanged());
}
```

"친구 슬롯 교체"는 어떤 덱을 보고 있든 항상 그 덱을 수정한다 — "지금 선택 중인 덱을 편집한다"는 자연스러운 동작과 일치한다. 기존 배열을 그 자리에서 고치지 않고 새 배열로 바꿔치기하므로, `Friends`를 먼저 읽어 들고 있던 코드가 있어도 그 스냅샷이 뒤늦게 바뀌는 일이 없다(참조 교체 vs 제자리 수정의 차이).

### 3. 덱 전환은 `SelectDeck(int index)` 신규 — 범위 밖 인덱스는 무시, 기존 `UserDataChanged` 재사용

```csharp
public void SelectDeck(int index)
{
    if (index < 0 || index >= _decks.Count) return;
    _currentDeckIndex = index;
    EventBus.Publish(new UserDataChanged());
}
```

`TrySpendShell`처럼 `bool` 반환도 검토했으나, 실패를 처리할 UI가 이번 범위에 없고 인덱스는 항상 버튼 개수(3)로 고정돼 실제로 범위를 벗어날 경로가 없다 — `void`로 충분.

`UserDataChanged`를 재사용하는 이유: `FriendTabController.RefreshSlots`/`MainMenuSceneManager.RefreshDeckCards`/`MainMenuHudView`가 이미 이 이벤트 하나만 구독한다. 덱 전환도 "`Friends`가 바뀌었다"는 점에서 카드 교체와 본질적으로 같은 사건이므로, 새 이벤트(`DeckSelected` 등)를 추가하면 구독처마다 두 이벤트를 다 들어야 하는 중복만 늘어난다.

### 4. `FriendTabController`: 덱 선택 버튼 3개는 인덱스 캡처 후 `SelectDeck` 호출만

```csharp
[SerializeField] private Button[] _deckButtons; // 3개, UserData 덱 인덱스와 1:1

private void Awake()
{
    // ... 기존 초기화 ...
    for (int i = 0; i < _deckButtons.Length; i++)
    {
        int index = i; // 클로저 캡처 방지 — MainMenuTabSlideController.Awake와 동일 관례
        _deckButtons[i].onClick.AddListener(() => UserManager.Current.SelectDeck(index));
    }
}
```

`RefreshSlots()`는 손댈 필요가 없다 — `UserDataChanged` 구독 하나로 카드 교체든 덱 전환이든 항상 같은 경로로 슬롯이 갱신된다. 어떤 버튼이 현재 선택된 덱인지 강조 표시하는 로직은 이번 범위에서 제외한다(아래 "이번 범위에서 제외" 참고).

---

## 클래스 구조

```
UserData                                          (기존 파일 수정, Core/User/)
├── _decks : List<int[]>                          ← 기존 _friends 대체, 3개 고정, 각 3장
├── _currentDeckIndex : int                        ← 신규, 0~2
├── Friends : IReadOnlyList<int> { get }           ← _decks[_currentDeckIndex]로 위임(시그니처 변경 없음)
├── CurrentDeckIndex : int { get }                 ← 신규
├── DeckCount : int { get }                        ← 신규
├── SetFriends(IEnumerable<int>)                   ← 기존 시그니처 유지, 내부만 현재 덱 교체로 변경
└── SelectDeck(int index)                          ← 신규, 범위 밖 무시 + UserDataChanged 발행

FriendTabController                               (기존 파일 수정, MainMenu/)
├── _deckButtons : Button[3]                       ← [SerializeField] 신규
└── Awake()                                        ← 기존 초기화에 덱 버튼 onClick 연결 추가
```

---

## 파일 구성

```
Assets/Scripts/
├── Core/User/UserData.cs                — 기존 파일 수정
└── MainMenu/FriendTabController.cs      — 기존 파일 수정
```

---

## 상세 구현 명세

코드 변경이 필요한 곳은 `FriendTabController`(버튼 추가) 하나뿐이다. 나머지 호출부는 영향이 없음을 아래에서 확인한다.

| 호출부 | 사용 방식 | 영향 |
|---|---|---|
| `InGameSceneManager` | `DeckBuilder.Build(UserManager.Current.Friends)` | 없음 — `Friends`가 항상 "지금 선택된 덱"을 반환 |
| `MainMenuSceneManager.RefreshDeckCards` | `UserManager.Current.Friends` 순회 | 없음 — 동일 |
| `FriendTabController.RefreshSlots` | `UserManager.Current.Friends` 순회 | 없음 — 동일 |
| `FriendTabController.RequestReplace` | `UserManager.Current.SetFriends(friends)` | 없음 — 여전히 "현재 덱"을 교체 |

---

## 이번 범위에서 제외

- **덱 선택 버튼의 강조 표시(현재 선택 덱 하이라이트)**: `CurrentDeckIndex`를 이미 공개해 뒀으니 필요해지면 바로 읽어 쓰면 되지만, 시각 디자인이 아직 정해지지 않았다.
- **덱 이름/별명 저장**: 지금은 인덱스(0~2)로만 구분한다 — 이름 붙이기 요구가 없다(YAGNI).
- **덱 개수를 3개보다 유동적으로 늘리는 것**: 친구 슬롯 3개와 마찬가지로 고정 개수를 전제로 설계했다. 가변화 요구가 생기면 재검토.
- **실제 씬 배치(`_deckButtons` 인스펙터 연결)**: Unity 에디터 작업, "구현 후 체크리스트"에 남긴다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `SelectDeck`에 범위 밖 인덱스(예: -1, 3) 전달 | 조기 반환 — `_currentDeckIndex` 변경/이벤트 발행 없음 |
| 이미 선택된 덱을 다시 `SelectDeck`으로 선택 | 값 동일 여부를 검사하지 않고 그대로 `UserDataChanged` 발행 — `SetFriends`의 동일 값 교체 허용 관례(`plan-mainmenuscene-friendselect.md`)와 동일 |
| `SetFriends` 호출 시점의 `_currentDeckIndex` | 호출 시점 기준 "현재 덱"을 수정한다 — 덱 전환과 카드 교체를 연달아 해도 마지막 `SelectDeck` 결과가 적용된 덱이 수정 대상 |
| 앱 최초 실행(`CreateDefault`) | 3개 덱 모두 기존 기본값(`1004,1016,1019`)으로 동일하게 시작, 이후 각 덱을 독립적으로 편집 가능 |
| `_decks` 항목 개수가 3이 아니게 되는 경우 | 발생 경로 없음 — 생성자에서 3개 고정, `SetFriends`는 덱 내부 항목만 교체하고 덱 개수 자체는 바꾸지 않음 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 앱 시작 후 `Friends` 최초 접근 | 기본 덱 0번(`1004,1016,1019`) 반환 |
| 2 | `SelectDeck(1)` 후 `Friends` 접근 | 덱 1번 내용 반환, `UserDataChanged` 1회 발행 |
| 3 | 덱 1번 선택 상태에서 `SetFriends(new[]{2001,2002,2003})` 호출 후 `SelectDeck(0)`, 다시 `SelectDeck(1)` | 덱 0번은 그대로(`1004,1016,1019`), 덱 1번은 방금 수정한 값 유지 |
| 4 | `SelectDeck(5)`(범위 밖) 호출 | `CurrentDeckIndex` 변화 없음, 이벤트 미발행 |
| 5 | `FriendTabController`의 덱 버튼 2번 클릭 | `UserDataChanged` 구독으로 `RefreshSlots`가 자동 호출돼 슬롯 3개가 덱 2번 카드로 즉시 갱신 |

---

## 구현 시 주의사항

- **`Friends`는 `_decks[_currentDeckIndex]`(`int[]`)를 그대로 반환한다** — 호출부가 배열 요소를 직접 대입하면 내부 상태가 즉시 오염된다. 다만 `SetFriends`가 배열을 제자리 수정하지 않고 통째로 교체하므로, 예전 `Friends`를 들고 있던 참조는 최신 값과 자동으로 얽히지 않는다(스냅샷처럼 동작). 그래도 `FriendTabController.RequestReplace`처럼 `new List<int>(Friends)`로 복사해 쓰는 관례는 계속 지켜야 한다.
- **`SetFriends`/`SelectDeck` 둘 다 `UserDataChanged`를 발행한다** — 두 호출을 한 프레임에 연달아 하면 구독자가 두 번 재계산되지만, 현재 UI 흐름상 그런 경로가 없어 문제 없다(생기면 그때 배칭 검토).
- **`_deckButtons` 인덱스는 반드시 로컬 변수로 캡처한다** — `MainMenuTabSlideController.Awake`와 동일하게 루프 변수를 직접 캡처하면 클로저 버그가 생긴다.
- **아직 SaveSystem이 없어 세션 종료 시 초기화된다**(`plan-userdata.md`의 기존 제약과 동일) — 나중에 `JsonUtility` 기반 저장을 붙일 때 중첩 컬렉션(`List<int[]>`)은 그대로 직렬화되지 않으므로(Unity `JsonUtility`는 중첩 컬렉션을 지원하지 않음) 그 시점에 DTO 변환이 필요할 수 있다.

---

## 구현 후 체크리스트

- [x] `UserData.cs`: `_friends` → `_decks`/`_currentDeckIndex`로 교체, `Friends`/`SetFriends` 위임 로직 수정, `SelectDeck`/`CurrentDeckIndex`/`DeckCount` 추가
- [x] `FriendTabController.cs`: `_deckButtons` 필드 추가, `Awake()`에 onClick 연결
- [ ] 씬: 친구탭에 덱 선택 버튼 3개 배치, `_deckButtons` 인스펙터 연결 — Unity 에디터 작업(이번 범위 밖)
- [ ] 테스트 시나리오 5개 검증
- [ ] (추후) 선택된 덱 버튼 강조 표시 UI
- [ ] (추후) SaveSystem 연동 시 `_decks` 직렬화 방식 재검토(JsonUtility 중첩 컬렉션 제약)
