# InGame 턴 진행 계획

> 상위 문서: [InGame 로직 개요](plan-ingame.md) (2단계, [덱 구성 계획](plan-ingame-decksetup.md) 이후)
> 관련 문서: [씬별 매니저 구현 계획](../02-scenemanager/plan-scenemanager.md) (`InGameSceneManager`가 씬 전용 로직을 담당한다는 원칙 재사용), [EventBus 구현 계획](../01-core-systems/eventbus/plan-eventbus.md) (구독/해제 패턴), [메인메뉴 게임 시작 계획](../02-scenemanager/mainmenuscene/plan-mainmenuscene-gamestart.md) (`GameSession.CurrentGameType`으로 Solo 분기하는 선례)
> 의존 관계: `JungleDice.Core.GameSession`(`GameType.Solo` 확인), `UnityEngine.UI.Button`, `TMPro.TextMeshProUGUI`
> 범위: 유저와 컴퓨터가 번갈아 진행하는 3단계(친구카드 플레이 → 공격 주사위 roll → 타겟 주사위 roll) 턴 상태 머신과, 그 진행에 따라 텍스트가 바뀌는 액션 버튼 하나. 각 단계의 실제 게임플레이 효과(카드 소비, 합체, 피격, 승패 판정)는 범위 밖 — 이번 단계는 상태 전환과 로그만 남기는 스켈레톤.

---

## 배경

요청받은 턴 구조를 그대로 옮기면 다음과 같다.

```
1. 친구카드 플레이   — 버튼 텍스트 "roll attacker"  — 완료 후 버튼 클릭 시 2로 진행
2. 공격 주사위 roll   — 버튼 텍스트 "roll target"    — 완료 후 버튼 클릭 시 3으로 진행
3. 타겟 주사위 roll   — 버튼 텍스트 "상대 턴"        — 2초 후 턴 변경
```

버튼에 표시되는 텍스트가 "지금 이 단계의 이름"이 아니라 "다음 단계의 이름"이라는 점에 주목해야 한다 — 1단계(친구카드 플레이) 중에는 버튼이 이미 "roll attacker"(2단계 이름)를 보여주고, 그 버튼을 누르면 2단계로 넘어간다. 이 규칙을 끝까지 따라가면: **버튼을 누르는 시점에 다음 단계가 시작되고, 그 단계의 동작(로그)이 즉시 실행되며, 버튼 텍스트는 그 다음에 올 단계를 미리 보여주는 라벨로 갱신된다.** 3단계("타겟 주사위 roll")에 진입하면 버튼은 "상대 턴"을 보여줄 뿐 더 이상 클릭을 받지 않고(다음이 사람의 결정이 아니라 자동 전이이므로), 2초 뒤 자동으로 턴이 바뀐다.

이 해석에 따라 상태 머신을 다음과 같이 정의한다 — 각 단계는 "진입 시 실행되는 동작"과 "그 단계에서 보여줄 버튼 라벨(= 클릭 시 실행될 다음 단계의 이름)"을 한 쌍으로 갖는다.

| 단계(`TurnPhase`) | 진입 시 동작(스텁) | 버튼 라벨 | 버튼 클릭 시 |
|---|---|---|---|
| `PlayFriend` | "친구카드 플레이" 로그 | `"roll attacker"` | → `RollAttacker` 진입 |
| `RollAttacker` | "공격 주사위: N" 로그(`Random.Range(1,7)`) | `"roll target"` | → `RollTarget` 진입 |
| `RollTarget` | "타겟 주사위: N" 로그(`Random.Range(1,7)`) | `"상대 턴"`, 버튼 비활성화 | (클릭 불가) |

`RollTarget` 진입 2초 후 자동으로 턴이 교대되고, 상대의 `PlayFriend`가 즉시 시작된다.

컴퓨터 턴에는 버튼 클릭이 없으므로, 요청사항("컴퓨터는 각 행동 단계에서 2초 대기")을 그대로 반영해 `PlayFriend`/`RollAttacker`에서도 2초를 기다린 뒤 자동으로 다음 단계로 진행시킨다. `RollTarget`의 2초 대기(턴 교대)는 유저/컴퓨터 공통이므로, 컴퓨터 턴은 결과적으로 매 단계 2초씩 총 3번의 2초 대기를 거친다.

---

## 설계 목표

- 유저 턴: 버튼 클릭으로만 단계가 진행된다(자동 진행 없음, `RollTarget` 진입 후 2초 대기만 예외)
- 컴퓨터 턴: 클릭 없이 매 단계 2초 대기 후 자동 진행된다 — "현재 구현 단계"에서는 실제 AI 판단 없이 대기만 한다는 것을 코드에도 명시(추후 교체 지점 남김)
- 버튼 텍스트는 상태 머신의 현재 단계에서 파생되는 값이다 — 버튼 라벨을 여기저기서 개별적으로 `.text = "..."`로 흩어 쓰지 않고 단계 전이 지점 한 곳에서만 갱신
- 컴퓨터 턴인데 버튼을 눌러도 아무 일도 일어나지 않는다(오조작 방지)
- 승패 판정이 없으므로 턴은 무한히 반복된다 — 이번 범위에서 종료 조건을 만들지 않는다(YAGNI, `GameState.GameOver` 전이는 아직 트리거할 근거가 없음)

---

## 핵심 설계 결정

### 상태: `TurnOwner`/`TurnPhase` enum + `InGameSceneManager` 필드로 보관

별도 클래스로 분리하지 않고 `InGameSceneManager`(씬 전용 오케스트레이터, `plan-scenemanager.md`가 이미 "그 씬만의 로직을 담당"하도록 정의)에 그대로 얹는다. 지금 시점에 턴 로직을 별도 컴포넌트로 쪼갤 근거(재사용, 다른 씬에서도 필요 등)가 없어 새 추상화를 만들지 않는다(YAGNI) — [덱 구성 계획](plan-ingame-decksetup.md)에서 이미 `_userDeck`/`_computerDeck` 필드도 `InGameSceneManager`에 두기로 했으므로 일관성도 있다.

```csharp
public enum TurnOwner { User, Computer }
public enum TurnPhase { PlayFriend, RollAttacker, RollTarget }
```

### 단계 전이: `EnterPhase(TurnPhase)` 한 곳에서 동작 실행 + 버튼 갱신

```csharp
[SerializeField] private Button _actionButton;
[SerializeField] private TextMeshProUGUI _actionButtonText;

private TurnOwner _currentOwner;
private TurnPhase _currentPhase;

private void StartMatch()
{
    _currentOwner = TurnOwner.User;
    EnterPhase(TurnPhase.PlayFriend);
}

private void EnterPhase(TurnPhase phase)
{
    _currentPhase = phase;

    switch (phase)
    {
        case TurnPhase.PlayFriend:
            Debug.Log($"[InGame] {_currentOwner} 턴 - 친구카드 플레이");
            _actionButtonText.text = "roll attacker";
            _actionButton.interactable = _currentOwner == TurnOwner.User;
            break;

        case TurnPhase.RollAttacker:
            Debug.Log($"[InGame] {_currentOwner} 턴 - 공격 주사위: {Random.Range(1, 7)}");
            _actionButtonText.text = "roll target";
            _actionButton.interactable = _currentOwner == TurnOwner.User;
            break;

        case TurnPhase.RollTarget:
            Debug.Log($"[InGame] {_currentOwner} 턴 - 타겟 주사위: {Random.Range(1, 7)}");
            _actionButtonText.text = "상대 턴";
            _actionButton.interactable = false;
            StartCoroutine(SwitchTurnAfterDelay());
            break;
    }

    if (_currentOwner == TurnOwner.Computer && phase != TurnPhase.RollTarget)
        StartCoroutine(ComputerAdvanceAfterDelay(phase));
}
```

- 유저 턴이든 컴퓨터 턴이든 `EnterPhase`가 실제 동작(로그)과 버튼 텍스트 갱신을 전담 — 두 경로가 각자 다른 곳에서 버튼을 건드리지 않도록 진입점을 하나로 통일
- `RollTarget`에 진입하는 순간 이미 버튼을 비활성화한다 — 다음 전이가 클릭이 아니라 자동(2초 대기)이므로 클릭을 받을 이유가 없음
- 컴퓨터 턴이면서 아직 `RollTarget`이 아닌 단계(`PlayFriend`/`RollAttacker`)에 진입했을 때만 자동 진행 코루틴을 추가로 건다 — `RollTarget`은 유저/컴퓨터 공통으로 이미 `SwitchTurnAfterDelay`가 자동 진행을 담당하므로 중복으로 걸지 않음

### 유저 클릭 → 다음 단계로 전진

```csharp
private void OnActionButtonClicked()
{
    if (_currentOwner != TurnOwner.User) return; // 컴퓨터 턴에는 버튼이 이미 비활성화되어 있지만 이중 방어

    switch (_currentPhase)
    {
        case TurnPhase.PlayFriend:
            EnterPhase(TurnPhase.RollAttacker);
            break;
        case TurnPhase.RollAttacker:
            EnterPhase(TurnPhase.RollTarget);
            break;
        // RollTarget 단계에서는 버튼이 비활성화되어 있어 호출되지 않음
    }
}
```

### 컴퓨터 자동 진행: 매 단계 2초 대기 후 동일한 전이 함수 재사용

```csharp
private IEnumerator ComputerAdvanceAfterDelay(TurnPhase enteredPhase)
{
    yield return new WaitForSeconds(2f);

    // 대기 중 다른 경로로 단계가 바뀌지 않았는지 확인(방어적 체크, 현재 구조상 항상 참)
    if (_currentPhase != enteredPhase) yield break;

    EnterPhase(enteredPhase == TurnPhase.PlayFriend ? TurnPhase.RollAttacker : TurnPhase.RollTarget);
}
```

- 컴퓨터의 "판단"은 지금 존재하지 않는다 — 2초 대기 후 그냥 다음 단계로 넘어가는 것이 요청사항의 "현재 구현 단계"가 의미하는 전부. 실제 AI 판단(어떤 카드를 낼지 등)이 생기면 이 코루틴 내부만 교체하면 되도록 진입점을 분리해둔다

### 턴 교대: `RollTarget` 진입 2초 후 공통 처리

```csharp
private IEnumerator SwitchTurnAfterDelay()
{
    yield return new WaitForSeconds(2f);

    _currentOwner = _currentOwner == TurnOwner.User ? TurnOwner.Computer : TurnOwner.User;
    EnterPhase(TurnPhase.PlayFriend);
}
```

- 유저/컴퓨터 구분 없이 동일 코루틴을 재사용 — `RollTarget`의 2초 대기는 "누구 턴이었는지"와 무관하게 동일한 규칙(요청사항 원문의 "2초후 턴 변경"이 특정 플레이어 전용이 아님)

### 시작 시점: `SetupDecks()`(덱 구성 계획) 직후 `StartMatch()` 호출

```csharp
protected override void OnAwake()
{
    _subs.Add(EventBus.Subscribe<GameStateChanged>(OnGameStateChanged));

    if (GameSession.CurrentGameType != GameType.Solo) return;

    SetupDecks();      // plan-ingame-decksetup.md
    StartMatch();      // 이번 문서

    _actionButton.onClick.AddListener(OnActionButtonClicked);
}
```

---

## 클래스 구조

```
TurnOwner : enum                                  (신규, InGame/)
├── User
└── Computer

TurnPhase : enum                                  (신규, InGame/)
├── PlayFriend
├── RollAttacker
└── RollTarget

InGameSceneManager (기존 파일 수정, InGame/)
├── _actionButton : Button [SerializeField]        ← 신규
├── _actionButtonText : TextMeshProUGUI [SerializeField]  ← 신규
├── _currentOwner : TurnOwner                      ← 신규
├── _currentPhase : TurnPhase                      ← 신규
├── StartMatch()                                   ← 신규, private
├── EnterPhase(TurnPhase)                          ← 신규, private, 동작 실행 + 버튼 갱신 단일 진입점
├── OnActionButtonClicked()                         ← 신규, private
├── ComputerAdvanceAfterDelay(TurnPhase) : IEnumerator  ← 신규, private
└── SwitchTurnAfterDelay() : IEnumerator            ← 신규, private
```

---

## 파일 구성

```
Assets/Scripts/
└── InGame/
    ├── TurnOwner.cs              ← 신규 (또는 InGameSceneManager.cs 안에 함께 선언 — 아래 주의사항 참고)
    ├── TurnPhase.cs               ← 신규
    └── InGameSceneManager.cs      ← 기존 파일 수정 (턴 상태 머신 + 버튼 연결)
```

- `GameEvents.cs`가 여러 `record`를 한 파일에 묶어두는 관례를 참고해, `TurnOwner`/`TurnPhase` 두 enum은 별도 파일 대신 `InGameSceneManager.cs` 상단에 함께 선언해도 무방(파일 수 최소화). 프로젝트에 "여러 관련 타입을 한 파일에 묶는다" vs "타입마다 파일 분리" 중 확립된 절대 규칙은 없으므로, 구현 시점에 선호에 따라 선택한다.

---

## Unity 씬/오브젝트 구성

```
[Scene: InGame]
└── IngameSceneManager (기존 GameObject, 씬에 이미 배치됨)
    └── InGameSceneManager.cs
        ├── _actionButton      ← 액션 버튼 GameObject의 Button 컴포넌트 참조 (신규 배치 필요)
        └── _actionButtonText  ← 액션 버튼 자식의 TextMeshProUGUI 참조
```

현재 `InGame.unity`에는 "roll attacker"/"roll target"/"상대 턴" 텍스트를 표시할 전용 액션 버튼 오브젝트가 없다(`turn`이라는 이름의 텍스트 오브젝트는 있으나 용도 미확인, 다른 표시용일 가능성). 실제 구현 시 `Button` + 자식 `TextMeshProUGUI`를 가진 오브젝트를 씬에 배치하고 `IngameSceneManager`의 두 필드에 연결해야 한다 — `MainMenuSceneManager`의 `Mode Solo`/`Mode Battle` 버튼 연결과 동일한 절차(인스펙터의 `onClick`은 비워두고 코드에서 `AddListener`).

---

## 이번 범위에서 제외

- 실제 카드 소비/합체 판정/피격/데미지 계산 — `PlayFriend`/`RollAttacker`/`RollTarget` 모두 로그만 남기는 스텁
- 승패 판정과 `GameState.GameOver` 전이 — 종료 조건이 없어 턴이 무한 반복됨
- 컴퓨터의 실제 판단 로직(어느 카드를 낼지, 주사위 결과에 따른 전략 등) — 지금은 고정 2초 대기 후 무조건 다음 단계로 진행
- [덱 구성 계획](plan-ingame-decksetup.md)에서 만든 `_userDeck`/`_computerDeck`을 실제로 이 턴 진행에서 소비하는 로직 — 두 문서는 이번 단계에서 서로 연결되지 않음(후속 문서 과제)
- 액션 버튼의 비주얼/사운드 연출 — 텍스트 전환과 활성/비활성만 다룸

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 컴퓨터 턴 중 `_actionButton` 클릭 시도 | `interactable = false`로 비활성화되어 클릭 자체가 발생하지 않음(`OnActionButtonClicked`의 `_currentOwner != User` 체크는 이중 안전장치) |
| `RollTarget` 단계에서 버튼 클릭 시도 | `interactable = false`라 클릭 불가 — 다음 전이는 오직 2초 후 자동 |
| `GameSession.CurrentGameType == Battle`로 InGame 진입 | `StartMatch()`/버튼 리스너 등록 모두 미실행 — 액션 버튼이 씬에 있어도 아무 반응 없음(Battle 모드 InGame UI는 별도 설계 필요, 범위 밖) |
| 유저가 `RollAttacker` 단계에서 버튼을 빠르게 연타 | 첫 클릭으로 즉시 `RollTarget`에 진입하며 버튼이 `interactable = false`로 바뀌므로 두 번째 클릭은 발생하지 않음 |
| 씬을 나갔다가 다시 InGame 진입(재도전) | `InGameSceneManager`가 새로 생성되며 `OnAwake()`부터 재실행 — 턴 상태는 유저 선공으로 초기화됨. 이전 인스턴스가 실행 중이던 코루틴은 씬 전환으로 `MonoBehaviour`가 파괴되며 Unity가 함께 정지시킴 |
| `_actionButton`/`_actionButtonText` 인스펙터 연결 누락 | `NullReferenceException` — `Friend.cs`/`MainMenuSceneManager`와 동일하게 방어 코드 없이 인스펙터 연결을 전제 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | Solo InGame 진입 직후 | `_currentOwner == User`, `_currentPhase == PlayFriend`, 버튼 텍스트 `"roll attacker"`, 버튼 활성화, 콘솔에 "친구카드 플레이" 로그 |
| 2 | 시나리오 1 상태에서 버튼 클릭 | `_currentPhase == RollAttacker`, 버튼 텍스트 `"roll target"`, 콘솔에 "공격 주사위: N"(1~6) 로그 |
| 3 | 시나리오 2 상태에서 버튼 클릭 | `_currentPhase == RollTarget`, 버튼 텍스트 `"상대 턴"`, 버튼 비활성화, 콘솔에 "타겟 주사위: N"(1~6) 로그 |
| 4 | 시나리오 3 이후 2초 경과 | `_currentOwner == Computer`, `_currentPhase == PlayFriend`, 버튼 비활성화(컴퓨터 턴이므로), 콘솔에 "Computer 턴 - 친구카드 플레이" 로그 |
| 5 | 시나리오 4 이후 2초 경과(컴퓨터 자동 진행) | `_currentPhase == RollAttacker`, 콘솔에 "Computer 턴 - 공격 주사위" 로그 |
| 6 | 시나리오 5 이후 2초 경과 | `_currentPhase == RollTarget`, 콘솔에 "Computer 턴 - 타겟 주사위" 로그 |
| 7 | 시나리오 6 이후 2초 경과 | `_currentOwner == User`로 복귀, `_currentPhase == PlayFriend`, 버튼 다시 활성화 — 유저/컴퓨터 한 라운드 완료 |
| 8 | `GameSession.CurrentGameType == Battle`로 InGame 진입 | 버튼에 리스너가 연결되지 않음, 턴 로그가 전혀 출력되지 않음 |

---

## 구현 시 주의사항

- **`EnterPhase`가 버튼 텍스트/활성화를 갱신하는 유일한 지점**: `OnActionButtonClicked`나 코루틴에서 직접 `_actionButtonText.text`를 건드리지 않는다 — 전부 `EnterPhase`를 거쳐야 유저/컴퓨터 경로가 갈라지지 않는다.
- **`RollTarget` 진입 시 버튼을 반드시 비활성화**: 다음 전이가 2초 자동이므로, 비활성화를 빠뜨리면 대기 중 클릭이 `OnActionButtonClicked`를 다시 타 상태가 꼬일 수 있다.
- **컴퓨터 자동 진행 코루틴(`ComputerAdvanceAfterDelay`)은 `RollTarget`에는 걸지 않는다**: `RollTarget`의 자동 전이는 `SwitchTurnAfterDelay` 하나로 유저/컴퓨터 공통 처리 — 두 코루틴이 동시에 걸리면 이중 전이가 발생한다.
- **`GameType.Solo` 가드는 [덱 구성 계획](plan-ingame-decksetup.md)과 동일한 위치(`OnAwake()` 최상단)에서 함께 처리**: 두 문서가 각자 다른 곳에서 가드를 반복하지 않도록 최종 구현 시 하나로 합친다.
- **주사위 값(`Random.Range(1, 7)`)은 로그 출력에만 쓰고 아직 아무 곳에도 반영하지 않는다**: 데미지 계산 등 실제 사용처가 생기면 그때 이 값을 들고 다닐 필드/이벤트를 추가한다(YAGNI, 지금은 요청사항에 없음).
- **`InGameSceneManager`가 씬 전환 없이 계속 살아있는 동안 코루틴이 누적되지 않도록 주의**: 매 `EnterPhase` 호출마다 새 코루틴을 시작하므로, 향후 일시정지(`GameState.Pause`) 등으로 상태 전이를 멈추는 기능이 추가되면 실행 중인 코루틴을 명시적으로 `StopCoroutine`해야 한다 — 지금은 Pause 중에도 턴이 계속 진행되는 것이 알려진 제약(범위 밖, 후속 검토 필요).

---

## 구현 후 체크리스트

- [ ] `TurnOwner`/`TurnPhase` enum 작성 (`Assets/Scripts/InGame/`, 또는 `InGameSceneManager.cs` 내부)
- [ ] `InGameSceneManager.cs`: `_actionButton`/`_actionButtonText` 필드, `StartMatch`/`EnterPhase`/`OnActionButtonClicked`/`ComputerAdvanceAfterDelay`/`SwitchTurnAfterDelay` 추가
- [ ] `InGame.unity`에 액션 버튼 오브젝트 배치(`Button` + 자식 `TextMeshProUGUI`), `IngameSceneManager`에 인스펙터 연결 (Unity 에디터 작업 필요)
- [ ] 테스트 시나리오 8개 검증 (특히 #4~#7: 컴퓨터 턴 자동 진행 타이밍)
- [ ] (추후) 덱 소비/합체/피격/승패 판정을 다루는 후속 계획 문서 작성
