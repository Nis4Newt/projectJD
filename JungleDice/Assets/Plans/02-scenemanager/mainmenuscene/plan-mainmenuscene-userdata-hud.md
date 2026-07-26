# 메인메뉴 유저 정보 HUD 연결 계획

> 상위 문서: [씬별 매니저 구현 계획](../plan-scenemanager.md) (MainMenu 씬 전용 로직의 예시로 든 "배너 갱신" 자리 — `MainMenuSceneManager.OnAwake()`의 "씬 진입 시 초기화 로직(배너/공지 갱신 등)" 주석 — 에서, 유저 정보 표시 필요가 파생)
> 관련 문서: [UserData 구현 계획](../../04-userdata/plan-userdata.md) (이 문서가 설계한 `UserData`/`UserManager`를 최초로 실제 UI에 연결하고, YAGNI로 미뤄둔 `UserDataChanged` 이벤트를 실제로 도입), [EventBus 구현 계획](../../01-core-systems/eventbus/plan-eventbus.md) (구독/해제 패턴 재사용)
> 의존 관계: `JungleDice.Core.User.UserManager`/`UserData`, `JungleDice.Core.Event.EventBus`/`GameEvents`/`CompositeDisposable`
> 범위: MainMenu 씬 HUD에 닉네임/랭크/점수/재화(Shell)/티켓을 `UserManager.Current`로부터 읽어와 표시하고(랭크는 `"{rank}랭크"`, 점수는 `"{score}점"` 표기), 씬 진입 이후 값이 중간에 바뀌어도(다른 시스템이 `UserData`를 조작) HUD가 즉시 갱신되도록 한다. `UserManager.Load()`를 어디서 호출할지 결정, 재화 증감 애니메이션(카운트업 등), 필드별로 세분화된 이벤트, 재화/티켓의 자리수 구분(콤마) 포맷은 범위 밖.

---

## 배경

`UserData`/`UserManager`(`plan-userdata.md`)는 이미 구현돼 있지만(`Assets/Scripts/Core/User/`), 프로젝트 전체에서 `UserManager.Current`나 `UserManager.Load()`를 참조하는 코드가 아직 하나도 없다 — 데이터 클래스만 존재하고 실제로 쓰이는 곳이 없는 상태다. `MainMenuSceneManager.OnAwake()`는 "배너/공지 갱신 등" 주석만 남긴 빈 스켈레톤이고, MainMenu 씬 상단(`header`)에는 이름 없는 프로토타입 `Slider`/`Text (TMP)` 오브젝트들이 배치돼 있을 뿐 실제 유저 데이터와 연결돼 있지 않다.

이번 문서는 랭크·점수·닉네임·재화·티켓 5개 값을 MainMenu 진입 시 화면에 표시하는 첫 연결 지점을 만든다. 씬 진입 시점의 초기화뿐 아니라, MainMenu에 머무는 동안 다른 시스템(상점, 보상 수령 등 — 아직 미구현이지만 곧 `UserManager.Current`를 조작할 기능들)이 `UserData`를 바꾸는 경우도 HUD에 즉시 반영돼야 한다. `plan-userdata.md`는 이 필요를 예견해 "재화/점수 변경 시 UI 갱신용 EventBus 이벤트(예: `UserDataChanged`) 발행"을 이미 자연스러운 확장 지점으로 언급하며 YAGNI로 미뤄뒀는데, 지금이 그 시점이다.

---

## 설계 목표

- MainMenu 씬 진입 시 `UserManager.Current`의 5개 필드(Name/Rank/Score/Shell/Ticket)를 HUD 텍스트에 표시한다
- 씬에 머무는 동안 `UserData`가 변경되면(어떤 필드든) HUD가 별도 재진입 없이 즉시 갱신된다
- 표시 전용(read-only) — 이번 범위에는 HUD 자체가 유저 데이터를 바꾸는 액션(구매 등)은 없다
- `Friend.SetKey`(`plan-prefab.md`)처럼 필드를 직접 대입하는 가장 단순한 방식을 쓴다 — 뷰모델, 바인딩 프레임워크 등 새 추상화를 도입하지 않는다
- `UserManager.Current`는 설계상 항상 non-null이므로(`??=` 지연 생성) 호출부에서 널 체크를 하지 않는다

---

## 핵심 설계 결정

### 1. 신규 컴포넌트 `MainMenuHudView` — `MainMenuSceneManager`에 직접 넣지 않는다

`MainMenuSceneManager`는 `SceneSingleton`으로 씬 전체를 오케스트레이션하는 책임을 갖고, HUD 텍스트를 채우는 것은 프레젠테이션 로직이다. `MainMenuTabSlideController`를 별도 컴포넌트로 뺀 것과 같은 이유(SRP)로, HUD 바인딩도 `MainMenuHudView`라는 별도 `MonoBehaviour`로 분리한다.

### 2. 변경 발행 지점: `UserData`의 각 조작 메서드 끝에서 `EventBus.Publish(new UserDataChanged())`

"중간에 바뀔 수도 있다"는 요구를 만족하려면, 값이 실제로 바뀌는 유일한 지점 — `UserData`의 조작 메서드(`SetName`/`AddShell`/`TrySpendShell`/`AddTicket`/`TrySpendTicket`/`SetScore`/`SetRank`/`SetFriends`) — 에서 이벤트를 발행해야 한다. 호출부(상점 코드, 보상 코드 등)마다 "값을 바꾼 뒤 이벤트도 잊지 말고 발행"하도록 강제하면 언젠가 하나는 빠뜨리기 마련이므로, 발행 책임을 `UserData` 자신에게 두어 **변경 지점과 발행 지점을 하나로 묶는다**. `TrySpendShell`/`TrySpendTicket`처럼 실패할 수 있는 메서드는 실제로 값이 바뀐 경우(반환값 `true`)에만 발행한다.

```csharp
// UserData.cs
using JungleDice.Core.Event;

public void SetName(string name)
{
    _name = name;
    EventBus.Publish(new UserDataChanged());
}

public void AddShell(int amount)
{
    _shell = Mathf.Max(0, _shell + amount);
    EventBus.Publish(new UserDataChanged());
}

public bool TrySpendShell(int amount)
{
    if (amount <= 0 || _shell < amount) return false;
    _shell -= amount;
    EventBus.Publish(new UserDataChanged());
    return true;
}

// AddTicket/TrySpendTicket/SetScore/SetRank/SetFriends도 동일하게 각자의 대입 직후 발행
```

`UserManager`에 개별 필드마다 래퍼 메서드를 만들어 그곳에서 발행하는 방식도 검토했으나 기각했다 — 현재 호출 관례가 "`UserData`의 조작 메서드는 `UserManager.Current`를 통해서만 호출"(`plan-userdata.md`)이라 `UserManager`는 인스턴스를 들고 있을 뿐 개별 메서드를 감싸지 않는다. `UserManager`에 8개 필드만큼 래퍼를 새로 만드는 것은 이번 요구(변경 시 알림)에 비해 과한 보일러플레이트다.

### 3. `UserDataChanged`는 필드 구분 없는 단일 이벤트(파라미터 없음)

`GameEvents.cs`에 다음을 추가한다:

```csharp
// User 시스템
public record UserDataChanged();
```

어떤 필드가 바뀌었는지 payload로 구분하지 않는다 — `MainMenuHudView`는 어차피 5개 필드를 항상 통째로 다시 그리므로(아래 결정 4), "무엇이 바뀌었나"를 아는 것보다 "바뀌었다"는 사실만 알면 충분하다. 특정 필드만 구독해 세밀하게 갱신해야 하는 리스너가 생기면 그때 `ShellChanged`처럼 세분화된 이벤트를 추가로 도입한다(YAGNI).

### 4. `MainMenuHudView`: 초기 바인딩과 이벤트 갱신이 같은 메서드를 공유

`Awake()`의 최초 바인딩과 `UserDataChanged` 수신 시 갱신이 다른 코드 경로를 타면 언젠가 둘이 어긋난다. `BindUserData()` 하나로 합쳐서 두 곳 모두 이 메서드만 호출한다 — 5개 필드를 매번 통째로 다시 읽는 것은 비용이 무시할 만큼 저렴하므로, 어떤 필드가 바뀌었는지 구분해서 부분 갱신하는 최적화는 하지 않는다.

```csharp
using JungleDice.Core.Event;
using JungleDice.Core.User;
using TMPro;
using UnityEngine;

namespace JungleDice.MainMenu
{
    public class MainMenuHudView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _nicknameText;
        [SerializeField] private TextMeshProUGUI _rankText;
        [SerializeField] private TextMeshProUGUI _scoreText;
        [SerializeField] private TextMeshProUGUI _shellText;
        [SerializeField] private TextMeshProUGUI _ticketText;

        private readonly CompositeDisposable _subs = new();

        private void Awake()
        {
            BindUserData();
            _subs.Add(EventBus.Subscribe<UserDataChanged>(_ => BindUserData()));
        }

        private void BindUserData()
        {
            var data = UserManager.Current;

            _nicknameText.text = data.Name;
            _rankText.text = data.Rank + "랭크";
            _scoreText.text = data.Score + "점";
            _shellText.text = data.Shell.ToString();
            _ticketText.text = data.Ticket.ToString();
        }

        private void OnDestroy()
        {
            _subs.Dispose();
        }
    }
}
```

`Awake`/`OnDestroy`로 구독·해제 쌍을 맞춘다 — `MainMenuHudView`는 MainMenu 씬 안에서 별도로 비활성화될 일이 없는 상시 표시 UI라, `OnEnable`/`OnDisable`이 아니라 `LoginTapToContinueUI`와 동일하게 `Awake`/`OnDestroy` 페어를 쓴다.

### 5. `UserManager.Load()` 호출 시점은 이 문서가 해결하지 않는다

`plan-userdata.md`가 이미 "`UserManager.Load()`를 실제로 어디서(언제) 호출할지"를 후속 검토 항목으로 남겨뒀다. 이 문서는 그 시점과 무관하게, "지금 `UserManager.Current`가 들고 있는 값을 그대로 표시하고, 바뀌면 다시 표시"하는 데만 집중한다. `Load()`가 아직 어디서도 호출되지 않은 채 MainMenu에 진입하면 `Current`는 기본값(`Name=""`, 나머지 `0`)을 반환하고, HUD는 그 기본값을 그대로 보여준다 — 이는 버그가 아니라 SaveSystem/서버 연동 전까지 예상된 동작이다.

---

## 클래스 구조

```
UserData (기존 파일 수정, Core/User/)
├── (필드/프로퍼티 변경 없음)
└── SetName/AddShell/TrySpendShell/AddTicket/TrySpendTicket/SetScore/SetRank/SetFriends
    └── 각 메서드가 실제로 값을 바꾼 시점에 EventBus.Publish(new UserDataChanged()) 추가

GameEvents (기존 파일 수정, Core/Event/)
└── UserDataChanged() : record  ← 신규, 파라미터 없음

MainMenuHudView : MonoBehaviour                  (신규, MainMenu/)
├── _nicknameText : TextMeshProUGUI  [SerializeField]
├── _rankText     : TextMeshProUGUI  [SerializeField]
├── _scoreText    : TextMeshProUGUI  [SerializeField]
├── _shellText    : TextMeshProUGUI  [SerializeField]
├── _ticketText   : TextMeshProUGUI  [SerializeField]
├── _subs : CompositeDisposable
├── Awake()          ← private, BindUserData() 호출 + UserDataChanged 구독
├── BindUserData()   ← private, UserManager.Current를 읽어 5개 텍스트에 대입 (초기/갱신 공용)
└── OnDestroy()      ← private, _subs.Dispose()
```

---

## 파일 구성

```
Assets/Scripts/
├── Core/
│   ├── User/
│   │   └── UserData.cs      ← 기존 파일 수정 (각 조작 메서드에 이벤트 발행 추가)
│   └── Event/
│       └── GameEvents.cs    ← 기존 파일 수정 (UserDataChanged 추가)
└── MainMenu/
    └── MainMenuHudView.cs   ← 신규
```

---

## Unity 씬/오브젝트 구성

```
[Scene: MainMenu]
└── Canvas
    └── header (기존 오브젝트, 현재 이름 없는 프로토타입 Slider/Text 7개 자식)
        └── Hud (신규 GameObject, MainMenuHudView.cs 부착)
            ├── NicknameText (TextMeshProUGUI)
            ├── RankText (TextMeshProUGUI)
            ├── ScoreText (TextMeshProUGUI)
            ├── ShellText (TextMeshProUGUI)
            └── TicketText (TextMeshProUGUI)
```

`header` 하위의 기존 프로토타입 오브젝트(이름 없는 `Slider`/`Text (TMP)` 등)는 실제 구현 시 위 5개 텍스트로 정리하거나 병행 배치한다 — 정확한 비주얼 배치(아이콘, 여백 등)는 디자인 몫이라 이 문서는 어떤 데이터를 어떤 컴포넌트가 갖고 있어야 하는지만 규정한다.

---

## 이번 범위에서 제외

- **`UserManager.Load()` 호출 시점 결정** — `plan-userdata.md`의 미해결 후속 항목, 이 문서가 대신 결정하지 않음
- **필드별로 세분화된 이벤트(`ShellChanged` 등)** — 지금은 `UserDataChanged` 하나로 뭉뚱그려 처리. 특정 필드만 구독해야 하는 리스너가 생기면 그때 세분화
- **재화/티켓의 자리수 구분(콤마) 포맷** — 디자인 미확정, 지금은 원시값 그대로
- **재화 변경 시 카운트업 등 애니메이션** — 표시 전용 범위라 해당 없음
- **`UserData`를 실제로 바꾸는 상점/보상 등 기능 자체** — 아직 미구현. 이 문서는 "바뀌면 HUD가 반응한다"는 배선만 준비한다

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| MainMenu 체류 중 다른 코드가 `UserManager.Current.AddShell(100)` 등을 호출 | `AddShell`이 `UserDataChanged`를 발행 → `MainMenuHudView`의 구독 콜백이 `BindUserData()` 재실행 → ShellText 등 즉시 갱신 |
| `TrySpendShell`/`TrySpendTicket`이 잔액 부족으로 실패(`false` 반환) | 값이 바뀌지 않았으므로 이벤트도 발행되지 않음 — HUD 변화 없음 |
| `UserManager.Load()`가 아직 어디서도 호출되지 않은 채 MainMenu 진입 | `Current`가 기본값 반환 → 닉네임 빈 문자열, 나머지 텍스트 "0" 표시. 예외 없음(SaveSystem 연동 전 예상된 동작) |
| `UserManager.Load()`가 MainMenu 체류 중 호출되어 `Current`가 완전히 새 `UserData` 인스턴스로 교체됨 | `EventBus.Subscribe<UserDataChanged>`는 타입 기반 구독이라 특정 인스턴스에 묶여 있지 않음 — `Load()` 내부에서도 필요 시 발행하면 동일하게 갱신됨(다만 `Load()`가 이벤트를 발행하도록 만드는 것은 이 문서 범위 밖, `plan-userdata.md`의 후속 항목) |
| `Name`이 빈 문자열 | 그대로 빈 텍스트로 표시 — "Guest" 등 fallback 문구는 디자인 결정이 없어 추가하지 않음 |
| 인스펙터에서 Text 필드 연결 누락 | `NullReferenceException` — `Friend.cs`와 동일하게 인스펙터 연결을 전제하며 방어 코드를 추가하지 않는 기존 관례를 따름 |
| MainMenu 씬 재진입(로그아웃 후 재로그인 등, Single 모드 재로드) | `Awake()`가 다시 실행되어 그 시점의 `UserManager.Current` 값으로 재바인딩 + 새 구독 등록. 이전 씬의 구독은 `OnDestroy` → `_subs.Dispose()`로 이미 해제됨 |
| `SetFriends` 호출(HUD가 표시하지 않는 필드) | `UserDataChanged`가 발행되지만 `BindUserData()`가 Friends를 읽지 않으므로 HUD엔 변화 없음 — 불필요한 재바인딩이 한 번 더 일어날 뿐 부작용 없음 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | `UserManager.Current.SetName("Woong")`, `SetRank(3)`, `SetScore(1200)`, `AddShell(500)`, `AddTicket(2)` 호출 후 MainMenu 씬 진입 | 닉네임 "Woong", 랭크 "3랭크", 점수 "1200점", 재화 "500", 티켓 "2" 표시 |
| 2 | `UserManager.Load()`를 호출하지 않고 바로 MainMenu 씬 진입 | 닉네임 빈 문자열, 랭크 "0랭크", 점수 "0점", 재화/티켓 "0" 표시, 예외 없음 |
| 3 | MainMenu 진입 후(초기 표시 확인 뒤) `UserManager.Current.AddShell(100)` 호출 | 씬 재진입 없이 ShellText가 즉시 갱신된 값으로 바뀜 |
| 4 | MainMenu 진입 후 `UserManager.Current.TrySpendShell(999999)`(잔액 부족) 호출 | `false` 반환, `UserDataChanged` 미발행, ShellText 변화 없음 |
| 5 | MainMenu 진입 후 `UserManager.Current.AddTicket(1)`과 `SetScore(50)`을 연속 호출 | 두 번의 `UserDataChanged` 발행 각각에 대해 `BindUserData()`가 재실행되며, 최종적으로 TicketText/ScoreText 모두 최신값으로 일치 |
| 6 | 인스펙터에 5개 Text 참조가 모두 정상 연결된 상태로 Play | `Awake()` 실행 중 예외 없이 5개 텍스트가 채워지고 구독이 등록됨 |

---

## 구현 시 주의사항

- **`Friend.cs` 패턴을 그대로 따른다**: 필드 직접 대입만 하고 포맷터/바인더 클래스를 새로 만들지 않는다.
- **이벤트 발행은 반드시 `UserData`의 조작 메서드 내부, 실제로 값이 바뀐 시점에만 한다** — `TrySpendShell`/`TrySpendTicket`처럼 실패 가능한 메서드는 `false` 반환 경로에서 발행하지 않도록 주의(가드 절 이후, 대입 직후에 위치).
- **`UserData.cs`에 `using JungleDice.Core.Event;`를 추가한다** — `Core.User`가 `Core.Event`를 참조하는 것은 둘 다 `Core` 하위의 정적 유틸리티라 순환 의존이 없다.
- **`MainMenuHudView`는 `Awake`/`OnDestroy` 페어로 구독·해제한다** — `LoginTapToContinueUI`와 동일한 관례(상시 표시 UI는 `OnEnable`/`OnDisable`을 쓰지 않음).
- **`BindUserData()`는 초기 바인딩과 이벤트 갱신 양쪽에서 반드시 동일한 메서드를 호출한다** — 두 경로가 갈라지면 언젠가 어긋난다.
- **`UserManager.Load()`의 호출 시점 결정은 이 문서의 범위가 아니다** — `plan-userdata.md`의 미해결 항목을 그대로 둔다.
- **랭크/점수는 `"{value}랭크"`/`"{value}점"` 문자열 접합으로 고정한다** — 재화/티켓처럼 원시값만 표시하지 않도록 주의(요구사항에 명시된 유일한 포맷 규칙).
- **재화/티켓의 자리수 구분(콤마 등)은 디자인이 확정되면 별도로 추가한다** — 지금은 `ToString()` 원시값 그대로.

---

## 구현 후 체크리스트

- [x] `GameEvents.cs`에 `public record UserDataChanged();` 추가
- [x] `UserData.cs`: `SetName`/`AddShell`/`TrySpendShell`/`AddTicket`/`TrySpendTicket`/`SetScore`/`SetRank`/`SetFriends` 각각에 `EventBus.Publish(new UserDataChanged())` 추가(Try 계열은 성공 경로에만)
- [x] `MainMenuHudView.cs` 작성 (`Assets/Scripts/MainMenu/`)
- [ ] `header` 하위에 `Hud` 오브젝트와 Nickname/Rank/Score/Shell/Ticket 5개 `TextMeshProUGUI` 배치 (Unity 에디터 작업 필요)
- [ ] `Hud` 오브젝트에 `MainMenuHudView.cs` 부착 후 5개 Text 인스펙터 연결 (Unity 에디터 작업 필요)
- [ ] 테스트 시나리오 6개 검증(특히 #3~#5: 씬 재진입 없이 실시간 갱신되는지)
- [ ] (추후) `UserManager.Load()` 호출 시점이 결정되면 이 문서의 동작 재확인
- [ ] (추후) 재화/티켓 자리수 구분(콤마 등)이 디자인 확정되면 반영
