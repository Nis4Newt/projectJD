# InGame 덱 구성 계획

> 상위 문서: [InGame 로직 개요](plan-ingame.md) (1단계)
> 관련 문서: [UserData 구현 계획](../04-userdata/plan-userdata.md) (`_friends`가 덱 구성 출처, 이번 문서에서 `icon`/`nextStage` 필드 추가), [테이블 리더 시스템 구현 계획](../03-table/plan-table.md) (`StageTable.GetFriends(key)`로 컴퓨터 친구 목록 조회), [씬별 매니저 구현 계획](../02-scenemanager/plan-scenemanager.md) (`InGameSceneManager.OnAwake()`에서 실행)
> 의존 관계: `JungleDice.Core.User.UserData`/`UserManager`, `JungleDice.Data.Table.StageTable`, `JungleDice.Core.GameSession`(`GameType.Solo` 여부 확인)
> 범위: `UserData`에 `icon`(string)/`nextStage`(int) 필드 추가, InGame 씬 진입 시 유저·컴퓨터 각각 30매 덱을 생성해 셔플하고 로그로 출력하는 것까지. 생성된 덱을 실제 턴 진행에서 소비하는 로직은 범위 밖([턴 진행 계획](plan-ingame-turnsystem.md)과도 아직 연결되지 않음).

---

## 배경

`UserData.Friends`(`IReadOnlyList<int>`, `CardTable`의 key 목록)는 유저가 선택한 친구 카드 3장을 담고 있다(`plan-userdata.md`). Solo 모드 InGame에서는 이 3장을 각 10장씩 늘려 30장 덱을 만들고 섞어야 한다. 컴퓨터(상대)도 동일한 규칙으로 덱을 만들되, 친구 목록의 출처만 다르다 — `StageTable`(`key|icon|hp|friend1|friend2|friend3`)의 한 행에서 가져온다.

문제는 "몇 번째 스테이지 데이터를 컴퓨터가 쓸지"를 결정할 값이 아직 `UserData`에 없다는 것 — 이번 문서가 `nextStage` 필드를 추가해 이 값을 유저 진행도로 관리한다. `icon`은 `StageTableData.icon`(스테이지 아이콘)과 대응되는 유저 프로필 아이콘 필드로, 이번 요구사항에 직접 쓰이진 않지만 함께 요청된 필드라 같이 추가한다(HUD 등 실제 표시 소비처는 후속 작업).

---

## 설계 목표

- `UserData` 필드는 기존 관례(모두 `private` + 전용 setter 메서드 + `EventBus.Publish(new UserDataChanged())`)를 그대로 따른다
- 유저 덱과 컴퓨터 덱은 "친구 3장을 10장씩 늘려 셔플"이라는 동일한 규칙을 공유하므로 하나의 헬퍼로 통일하고, 실제 소스(유저의 `_friends` vs 스테이지의 `friends`)만 다르게 넘긴다
- InGame 씬 진입 즉시(Solo 모드에 한해) 덱을 만들고 로그로 확인할 수 있어야 한다 — 아직 UI에 카드가 배치되지 않으므로 콘솔 로그가 유일한 확인 수단
- `GameType.Battle`(대전) 진입 시에는 이 로직이 실행되지 않는다 — Solo 전용 범위를 코드에서도 명확히 가드

---

## 핵심 설계 결정

### 덱 생성 헬퍼: `DeckBuilder` 정적 클래스, `InGame/`에 신규

유저/컴퓨터 양쪽 다 "친구 key 목록 → 각 10장씩 → 셔플된 30장 리스트"라는 동일한 절차를 거치므로, 이 로직을 `InGameSceneManager` 안에 두 번 쓰지 않고 별도 정적 헬퍼로 뽑아낸다(`SpriteManager`/`TableValueParser`처럼 상태 없는 순수 유틸 정적 클래스 관례 재사용).

```csharp
namespace JungleDice.InGame
{
    public static class DeckBuilder
    {
        private const int CopiesPerFriend = 10;

        public static List<int> Build(IReadOnlyList<int> friendKeys)
        {
            var deck = new List<int>(friendKeys.Count * CopiesPerFriend);
            foreach (var key in friendKeys)
            {
                for (int i = 0; i < CopiesPerFriend; i++)
                    deck.Add(key);
            }
            Shuffle(deck);
            return deck;
        }

        private static void Shuffle(List<int> deck)
        {
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
        }
    }
}
```

- Fisher–Yates 셔플 채택 — 별도 패키지 없이 `UnityEngine.Random`만으로 균등 분포 셔플이 되는 표준 알고리즘, 새 알고리즘을 고안할 이유 없음
- `friendKeys.Count`가 3이 아니어도(예: 추후 친구 수 규칙이 바뀌어도) 그대로 동작 — `CopiesPerFriend`만 상수로 고정, 친구 수는 하드코딩하지 않음
- 입력 유효성(빈 리스트, null 등)은 검사하지 않는다 — 호출부(`InGameSceneManager`)가 항상 `UserData.Friends`/`StageTable.GetFriends`에서 온 값을 넘기고, 두 출처 모두 이미 각자의 계층에서 데이터 부재를 `LogError`로 알리므로 여기서 중복 방어하지 않음

### 컴퓨터 친구 목록 출처: `StageTable.GetFriends(UserData.NextStage)`

`StageTable`(`03-table`)에 이미 `GetFriends(int key)`가 있다(`StageTableData.friends`, `friend1`/`friend2`/`friend3`를 `OnLoaded()`에서 배열로 묶어둔 것). 어떤 스테이지인지는 `UserData.NextStage`(이번 문서에서 추가)로 결정한다 — "다음에 도전할 스테이지"라는 이름 그대로, Solo 모드 진입 시점의 진행도를 이 값이 대표한다.

### 실행 시점: `InGameSceneManager.OnAwake()`, `GameSession.CurrentGameType == Solo`일 때만

```csharp
protected override void OnAwake()
{
    _subs.Add(EventBus.Subscribe<GameStateChanged>(OnGameStateChanged));

    if (GameSession.CurrentGameType != GameType.Solo) return; // Battle 모드는 범위 밖

    SetupDecks();
}

private void SetupDecks()
{
    var stageFriends = StageTable.Instance.GetFriends(UserManager.Current.NextStage);

    _userDeck = DeckBuilder.Build(UserManager.Current.Friends);
    _computerDeck = DeckBuilder.Build(stageFriends);

    Debug.Log($"[InGame] 유저 덱: {string.Join(", ", _userDeck)}");
    Debug.Log($"[InGame] 컴퓨터 덱: {string.Join(", ", _computerDeck)}");
}
```

- `StageTable.Instance`가 `null`이거나 `GetFriends`가 없는 key로 `null`을 반환하는 경우, `DeckBuilder.Build(null)`은 `NullReferenceException`을 던진다 — 이는 "테이블 asset 자체가 없음"/"`nextStage` 값이 잘못됨" 같은 설정 오류이므로, `Friend.cs`/`CardTable` 등과 동일하게 방어 코드 없이 즉시 드러나게 둔다(엣지 케이스 표 참고)
- `_userDeck`/`_computerDeck`은 `InGameSceneManager`의 private 필드로 보관 — 지금은 로그 출력에만 쓰이지만, [턴 진행 계획](plan-ingame-turnsystem.md) 이후의 후속 문서에서 실제 소비 로직이 이 필드를 사용하게 될 예정

### `UserData` 확장: `icon`(string), `nextStage`(int)

기존 필드와 동일한 패턴 — `private` 필드 + 읽기 프로퍼티 + 전용 setter, setter는 `UserDataChanged` 발행:

```csharp
[SerializeField] private string _icon = "";
[SerializeField] private int _nextStage;

public string Icon => _icon;
public int NextStage => _nextStage;

public void SetIcon(string icon)
{
    _icon = icon;
    EventBus.Publish(new UserDataChanged());
}

public void SetNextStage(int stage)
{
    _nextStage = stage;
    EventBus.Publish(new UserDataChanged());
}
```

- `nextStage`는 "증감"이 아니라 "다음 목표 스테이지 번호를 통째로 대입"하는 값이므로 `Score`/`Rank`와 동일하게 `Set-` 계열 메서드 하나로 충분(`Add-`/`TrySpend-` 불필요)
- 기본값: `_icon = ""`(다른 문자열 필드와 동일), `_nextStage = 1` — `StageTable`의 key가 1부터 시작하므로, 신규 유저가 처음 InGame에 진입해도 유효한 스테이지 데이터를 즉시 조회할 수 있도록 필드 이니셜라이저에서 바로 유효값으로 초기화한다(다른 필드처럼 "빈 값"을 기본으로 두고 별도 초기화 시점을 기다릴 이유가 없음 — 0은 애초에 유효한 스테이지 key가 아니므로 기본값 후보에서 제외)

---

## 클래스 구조

```
UserData (기존 파일 수정, Core/User/)
├── _icon : string [SerializeField]         ← 신규
├── _nextStage : int [SerializeField]        ← 신규
├── Icon : string { get; }                   ← 신규
├── NextStage : int { get; }                 ← 신규
├── SetIcon(string)                          ← 신규
└── SetNextStage(int)                        ← 신규

DeckBuilder : static class                  (신규, InGame/)
└── Build(IReadOnlyList<int> friendKeys) : List<int>   ← 10장씩 복제 + Fisher–Yates 셔플

InGameSceneManager (기존 파일 수정, InGame/)
├── _userDeck : List<int>                    ← 신규
├── _computerDeck : List<int>                ← 신규
├── OnAwake()                                ← 기존 GameStateChanged 구독 + Solo 가드 + SetupDecks() 호출 추가
└── SetupDecks()                             ← 신규, private
```

---

## 파일 구성

```
Assets/Scripts/
├── Core/
│   └── User/
│       └── UserData.cs          ← 기존 파일 수정 (icon/nextStage 추가)
└── InGame/
    ├── DeckBuilder.cs            ← 신규
    └── InGameSceneManager.cs     ← 기존 파일 수정 (덱 생성 호출 추가)
```

---

## 상세 구현 명세

핵심 설계 결정 절에 전체 코드를 이미 제시했으므로 반복하지 않는다.

---

## 이번 범위에서 제외

- 생성된 덱을 실제로 소비(카드 뽑기)하는 로직 — [턴 진행 계획](plan-ingame-turnsystem.md)에서도 아직 연결하지 않음, 후속 별도 문서에서 다룬다
- `UserData.NextStage`를 언제/어떻게 증가시킬지(스테이지 클리어 시 `+1` 등) — 스테이지 클리어 판정 자체가 아직 없음
- `UserData.Icon`을 실제로 표시하는 UI(HUD 등) — 필드 추가까지만, 소비처는 후속 작업
- 덱 UI 배치(카드 프리팹을 화면에 실제로 늘어놓기) — 지금은 `Debug.Log`로만 확인

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| `GameSession.CurrentGameType == Battle` | `SetupDecks()` 호출 안 함 — `OnAwake()` 조기 반환 |
| `StageTable.Instance`가 `null`(asset 부재) | `GetFriends` 호출 자체가 `NullReferenceException`을 던짐 — 설정 누락은 방어하지 않고 즉시 드러냄(`CardTable`/`Friend`와 동일 철학이지만, 여긴 `Instance` 자체가 없는 경우라 `Get`류의 `LogError`+default 반환 이전 단계에서 예외로 이어짐) |
| `UserData.NextStage`가 `StageTable`에 없는 key | `StageTable.GetFriends`가 `LogError` 후 `null` 반환 → `DeckBuilder.Build(null)`에서 `NullReferenceException` — 잘못된 진행도 값은 즉시 실패로 드러남 |
| `UserData.Friends`가 3개 미만/빈 리스트(카드 선택 없이 진입) | `DeckBuilder.Build`가 그 개수만큼만 10장씩 생성 — 예외 없이 더 적은 덱이 만들어짐(카드 선택 강제는 MainMenu/카드선택 화면의 책임, 이 문서 범위 밖) |
| 씬 재진입(예: 재도전으로 InGame 씬 재로드) | `InGameSceneManager`가 새로 생성되며 `OnAwake()`가 다시 실행 → 덱도 다시 생성/셔플됨(매번 새 셔플 결과) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | `UserData.Friends = [1000, 1001, 1002]` 상태로 Solo InGame 진입 | `_userDeck.Count == 30`, 각 key가 정확히 10개씩 포함, 콘솔에 유저 덱 로그 1회 출력 |
| 2 | `UserData.NextStage`가 `StageTable`의 유효한 key, Solo InGame 진입 | `_computerDeck.Count == 30`(스테이지 friend 3개 기준), 콘솔에 컴퓨터 덱 로그 1회 출력 |
| 3 | `GameSession.CurrentGameType == Battle` 상태로 InGame 진입 | `SetupDecks()` 미실행, 덱 관련 로그 없음 |
| 4 | 같은 friend 구성으로 InGame을 두 번 진입(씬 재로드) | 두 번의 셔플 결과가 (매우 높은 확률로) 서로 다름 — 매번 새로 셔플 |
| 5 | `UserData.SetIcon("icon_01")` 호출 | `UserData.Icon == "icon_01"`, `UserDataChanged` 1회 발행 |
| 6 | `UserData.SetNextStage(3)` 호출 | `UserData.NextStage == 3`, `UserDataChanged` 1회 발행 |

---

## 구현 시 주의사항

- **`DeckBuilder`는 상태 없는 정적 클래스로 유지**: `SpriteManager`/`TableValueParser`와 동일하게 순수 함수형 유틸로 둔다 — 덱 상태 자체는 `InGameSceneManager`가 소유.
- **`UserData`의 새 필드도 기존 필드와 동일하게 `private` + `[SerializeField]` + 전용 setter 패턴을 반드시 지킨다**: 예외를 두지 않는다(`plan-userdata.md`의 캡슐화 원칙).
- **`SetIcon`/`SetNextStage` 모두 `EventBus.Publish(new UserDataChanged())` 호출을 빠뜨리지 않는다**: 현재 `UserData.cs`의 다른 모든 setter가 이미 이 패턴이므로 새 필드만 예외로 두면 일관성이 깨진다.
- **`GameType.Solo` 가드를 `InGameSceneManager.OnAwake()` 최상단 가까이에 둔다**: 이후 Battle 모드 InGame 로직이 추가될 때, 두 로직이 뒤섞이지 않도록 분기를 명확히 유지.
- **`DeckBuilder.Build`에 `null`/빈 리스트를 넘기는 상황을 별도로 방어하지 않는다**: 호출부(`StageTable`/`UserData`)가 이미 자기 계층에서 데이터 부재를 알리므로 중복 검사하지 않는다(엣지 케이스 표 참고).

---

## 구현 후 체크리스트

- [ ] `UserData.cs`: `_icon`/`_nextStage` 필드, `Icon`/`NextStage` 프로퍼티, `SetIcon`/`SetNextStage` 메서드 추가
- [ ] `DeckBuilder.cs` 작성 (`Assets/Scripts/InGame/`)
- [ ] `InGameSceneManager.OnAwake()`: `GameType.Solo` 가드 + `SetupDecks()` 호출 추가
- [ ] `InGameSceneManager.SetupDecks()`: 유저/컴퓨터 덱 생성 + `Debug.Log` 출력
- [ ] 테스트 시나리오 6개 검증
- [ ] (추후) 덱 소비 로직은 [턴 진행 계획](plan-ingame-turnsystem.md) 이후 별도 문서에서 연결
