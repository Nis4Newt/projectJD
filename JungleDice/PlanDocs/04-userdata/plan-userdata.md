# UserData 구현 계획

> 상위 문서: 없음 (신규 최상위 시스템, `03-table`과 동일한 위계 — 범용 프레임워크 설계)
> 관련 문서: [GameManager 구현 계획](../01-core-systems/gamemanager/plan-gamemanager.md) (소유 주체 후보로 검토 후 기각), [테이블 리더 시스템 구현 계획](../03-table/plan-table.md) (`TableLoader`의 static 클래스 설계를 소유 주체 패턴으로 재사용)
> 범위: 유저 상태(닉네임/재화/티켓/점수/랭크/선택 카드)를 담는 `UserData` 데이터 클래스와, 이를 전역에서 들고 있을 `UserManager`의 설계. 실제 `.cs` 구현은 이번 문서의 범위 밖(후속 구현 커밋에서 진행)

---

## 배경

게임에 재화(Shell)·티켓·점수·랭크·닉네임·선택 카드 같은 유저 상태가 필요한데, 아직 이 데이터를 담을 클래스도, 어디서 들고 있을지도 정해지지 않았다. 필드는 항상 캡슐화(`private`)하고 조작은 메서드로만 하도록 하는 것이 요구사항이며, 이번 문서는 그 클래스 설계와 전역 소유 주체를 결정한다.

프로젝트에는 아직 SaveSystem/서버 통신이 없다(`GameManager.cs:34-35`의 `// SaveSystem 구현 후 연결` 주석 참고). 따라서 지금 시점에 `UserData`를 "로드"할 영속 저장소는 없고, 기본값으로 생성하는 것만 가능하다. 이번 문서는 이 제약을 전제로, 이후 SaveSystem/서버 연동이 생겼을 때 `UserData` 자체는 손대지 않고 로드 로직만 교체할 수 있는 구조로 설계한다.

---

## 설계 목표

- 유저 데이터 필드는 항상 `private`으로 캡슐화하고, 읽기는 프로퍼티, 쓰기는 의미가 명확한 메서드로만 허용한다
- 재화(Shell/Ticket)처럼 "0 미만 금지", "잔액 부족 시 실패" 같은 실제 불변식이 있는 값은 그 규칙을 메서드 내부에 캡슐화해 호출부마다 중복 검증하지 않게 한다
- 데이터 클래스(`UserData`)와 그것을 전역에서 들고 있는 주체(`UserManager`)를 분리해, 향후 SaveSystem이 `UserManager`만 건드리면 되게 한다
- SaveSystem이 아직 없는 지금 시점에도 `UserManager.Current`가 항상 유효한 값을 반환해야 한다(널 체크를 호출부에 강요하지 않음)

---

## 핵심 설계 결정

### `UserData`: private 필드 + 조작 메서드를 가진 순수 C# 클래스

MonoBehaviour도 ScriptableObject도 아니다 — 테이블처럼 에셋으로 저장되는 고정 데이터가 아니라, 런타임 내내 값이 바뀌는 상태이기 때문이다. `TableDataBase<TKey>`는 리플렉션 파싱을 위해 필드를 `public`으로 열어두지만(`TableBase<,,>.PopulateFromText`가 필드명으로 직접 대입), `UserData`는 그 대상이 아니므로 이 예외를 따를 이유가 없다. 필드는 전부 `private`으로 두고, `[SerializeField]`만 붙여 향후 `JsonUtility` 기반 SaveSystem이 별도 DTO 변환 없이 바로 직렬화할 수 있게 대비한다(직렬화 로직 자체는 이번 범위에 없음).

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace JungleDice.Core.User
{
    [Serializable]
    public class UserData
    {
        [SerializeField] private string _name = "";
        [SerializeField] private int _shell;
        [SerializeField] private int _ticket;
        [SerializeField] private int _score;
        [SerializeField] private int _rank;
        [SerializeField] private List<int> _friends = new();

        public string Name => _name;
        public int Shell => _shell;
        public int Ticket => _ticket;
        public int Score => _score;
        public int Rank => _rank;
        public IReadOnlyList<int> Friends => _friends;

        public void SetName(string name) => _name = name;

        public void AddShell(int amount) => _shell = Mathf.Max(0, _shell + amount);

        public bool TrySpendShell(int amount)
        {
            if (amount <= 0 || _shell < amount) return false;
            _shell -= amount;
            return true;
        }

        public void AddTicket(int amount) => _ticket = Mathf.Max(0, _ticket + amount);

        public bool TrySpendTicket(int amount)
        {
            if (amount <= 0 || _ticket < amount) return false;
            _ticket -= amount;
            return true;
        }

        public void SetScore(int score) => _score = score;

        public void SetRank(int rank) => _rank = rank;

        public void SetFriends(IEnumerable<int> cardIds)
        {
            _friends.Clear();
            _friends.AddRange(cardIds);
        }
    }
}
```

필드별로 메서드를 나눈 이유:

| 필드 | 메서드 | 왜 단순 세터가 아닌가 |
|------|--------|----------------------|
| `Name` | `SetName` | 규칙은 없지만 "필드는 항상 private, 외부는 메서드로만" 원칙을 일관되게 적용 |
| `Shell`/`Ticket` | `Add-`, `TrySpend-` | 0 미만 방지 + 잔액 부족 실패 반환이라는 실제 불변식이 있어 세터 하나로 표현 불가 |
| `Score`/`Rank` | `Set-` | 서버/게임 로직이 계산한 값을 그대로 대입하는 값(증감이 아님)이라 대입형 메서드로 충분 |
| `Friends` | `SetFriends` | 카드 선택 화면에서 선택 결과를 통째로 제출하는 흐름을 가정 — 개별 추가/제거는 필요해지면 추후 확장(YAGNI) |

### 소유 주체: `UserManager` — static 클래스, `TableLoader`와 동일 패턴

**후보 검토:**

| 후보 | 기각/채택 사유 |
|------|----------------|
| `GameManager`가 직접 보유 | 기각 — `GameManager`의 책임은 상태 머신 오케스트레이션. 유저 데이터 보관을 얹으면 서로 다른 책임이 한 클래스에 섞여 SRP 위반 |
| `Singleton<T>` MonoBehaviour (GameManager 방식) | 기각 — `UserData`는 `Update`/`OnApplicationPause` 등 Unity 콜백이 전혀 필요 없다. GameObject 배치, `DontDestroyOnLoad` 설정 같은 부가 비용만 늘어남 |
| **static 클래스 (`TableLoader`/`EventBus` 방식)** | **채택** — static 필드는 씬 전환에도 자동 유지되어 별도 설정이 불필요. `TableLoader`가 "값을 전역으로 유지하고 정적 메서드로 채운다"는 동일한 문제를 이미 이 방식으로 풀었으므로 동일 패턴을 재사용하는 것이 일관적 |

```csharp
namespace JungleDice.Core.User
{
    public static class UserManager
    {
        private static UserData _current;

        public static UserData Current => _current ??= CreateDefault();

        public static void Load()
        {
            // SaveSystem/서버 연동 전까지는 기본값으로 초기화.
            // 이후 로컬 세이브 또는 서버 응답으로 채우도록 이 메서드 내부만 교체하면 됨.
            _current = CreateDefault();
        }

        private static UserData CreateDefault() => new UserData();
    }
}
```

`Current`는 `TableBase<TSelf,TData,TKey>.Instance`와 동일하게 최초 접근 시 지연 생성되는 안전망이다 — `Load()`가 아직 호출되지 않은 시점에 어딘가에서 먼저 접근해도 `null`이 아닌 기본값을 돌려준다. `Load()`를 언제 호출할지(부팅 시점, 씬 진입 시점 등)는 이 문서의 범위 밖이며, 이후 실제 연동 시점에 맞춰 결정한다.

---

## 클래스 구조

```
UserData                                          (신규, Core/User/, 순수 C# 클래스)
├── Name, Shell, Ticket, Score, Rank : {get}       ← 읽기 전용 프로퍼티
├── Friends : IReadOnlyList<int> {get}
├── SetName(string)
├── AddShell(int) / TrySpendShell(int) : bool
├── AddTicket(int) / TrySpendTicket(int) : bool
├── SetScore(int)
├── SetRank(int)
└── SetFriends(IEnumerable<int>)

UserManager                                   (신규, Core/User/, 런타임 static)
├── Current : UserData                            ← 전역 접근점, 지연 기본값 생성
├── Load()                                         ← 기본값으로 (재)초기화
└── CreateDefault() : UserData                     (private)
```

---

## 파일 구성

```
Assets/Scripts/
└── Core/
    └── User/
        ├── UserData.cs          ← 신규, 유저 데이터 클래스
        └── UserManager.cs   ← 신규, 전역 보유 + 로드 진입점
```

---

## 이번 범위에서 제외

- 실제 파일/서버 영속화(SaveSystem, 로컬 저장, 네트워크 동기화) — `PlayerPrefs`/`JsonUtility`/`UnityWebRequest` 어느 것도 프로젝트에 아직 없음. `Load()`는 기본값 생성만 수행
- 재화/점수 변경 시 UI 갱신용 EventBus 이벤트(예: `UserDataChanged`) 발행 — `EventBus`는 이미 있으므로 자연스러운 확장 지점이지만, UI 바인딩 요구가 생기기 전까지는 추가하지 않음(YAGNI)
- `Friends`(선택 카드)의 개별 추가/제거 API — 지금은 `SetFriends`로 전체 교체만 지원
- `UserManager.Load()`를 실제로 어디서(언제) 호출할지 — 부팅 시점/씬 진입 시점 등 연동 지점 결정은 후속 작업

---

## 엣지 케이스

| 상황 | 처리 방식 |
|------|-----------|
| `Load()` 호출 전에 다른 코드가 `UserManager.Current`에 먼저 접근 | `Current` getter의 `??=`가 즉시 `CreateDefault()`를 생성해 반환 — `null` 참조 없음. 이후 `Load()`가 호출되면 `_current`를 다시 덮어씀 |
| `TrySpendShell`/`TrySpendTicket`에 잔액보다 큰 값 또는 음수 전달 | `false` 반환, 필드 변경 없음 — 호출부가 실패를 인지하고 처리(예: 구매 취소 UI) |
| `AddShell`/`AddTicket`에 음수 전달(감소 용도로 오용) | `Mathf.Max(0, ...)`로 0 미만은 방지되지만, 의도된 차감은 `TrySpend-` 계열을 쓰는 것이 맞음 — `Add-` 계열은 획득 전용으로 문서화 |
| `SetFriends`에 빈 컬렉션 전달 | `_friends.Clear()`만 수행되고 빈 리스트 유지 — 정상 동작(선택 해제) |
| 씬 전환(Login → MainMenu → InGame) | static 필드이므로 별도 처리 없이 값 유지 (도메인 리로드가 없는 한 앱 세션 내내 보존 — Unity 에디터에서 스크립트 재컴파일 시 초기화되는 것은 `TableBase.Instance`와 동일한 특성) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 앱 시작 후 `UserManager.Load()`도 호출하지 않고 `Current` 최초 접근 | 기본값(`Name=""`, `Shell=0`, `Ticket=0`, `Score=0`, `Rank=0`, `Friends=[]`)을 가진 `UserData` 반환, 예외 없음 |
| 2 | `Current.AddShell(100)` 후 `Current.TrySpendShell(30)` | `true` 반환, `Shell == 70` |
| 3 | `Current.TrySpendShell(1000)` (잔액 부족) | `false` 반환, `Shell` 변경 없음 |
| 4 | `Current.SetFriends(new[]{1,2,3})` 후 다시 `Current.SetFriends(new[]{5})` | `Friends`가 `[5]`로 완전히 교체됨(누적 아님) |

---

## 구현 시 주의사항

- **`UserManager`는 `Core/User/`(런타임)에 둔다**: 테이블 시스템이 `Core/Table/`(프레임워크) vs `Data/Table/`(구체 데이터)로 나뉜 것과 달리, `UserData`는 타입이 하나뿐이라 별도 분리 없이 `Core/User/`에 함께 둔다.
- **`Add-`/`TrySpend-` 계열은 용도를 분리해 문서/주석으로 명확히 한다**: `Add-`는 획득(양수 가정), `TrySpend-`는 소비(실패 가능) — 하나의 메서드로 증감 모두 처리하지 않는다.
- **SaveSystem 연동 시 `Load()`만 교체**: `CreateDefault()` 호출 부분을 로컬 파일/서버 응답 파싱으로 바꾸면 되고, `UserData`의 필드 캡슐화(private + 메서드)는 그대로 유지되므로 `UserData` 자체는 손댈 필요가 없어야 한다.
- **`UserData`의 조작 메서드는 `UserManager.Current`를 통해서만 호출**: `UserData` 인스턴스를 여기저기 복사해 들고 다니지 않고, 항상 `UserManager.Current`로 최신 인스턴스에 접근하도록 호출부 컨벤션을 통일한다.

---

## 구현 후 체크리스트

- [x] `UserData.cs` 작성 (`Assets/Scripts/Core/User/`)
- [x] `UserManager.cs` 작성 (`Assets/Scripts/Core/User/`)
- [ ] 테스트 시나리오 4개 검증
- [ ] `UserManager.Load()`를 실제로 어디서 호출할지 후속 검토
