# GameManager 구현 계획

> 상위 문서: [공용 코어 시스템 설계 계획](../plan-core-systems.md) (시스템 목록 #1)
> Phase 1 — 두 번째 구현 대상  
> 의존 관계: EventBus (GameStateChanged 이벤트 발행)

---

## 설계 목표

- 게임 전체 생명주기의 단일 진입점
- 코어 시스템 초기화 순서 명시적 제어
- 씬 전환 이후에도 파괴되지 않는 루트 오브젝트
- 상태 전이가 예측 가능하고 추적 가능할 것
- 이중 인스턴스 생성 방지 (씬 재진입 안전)

---

## 핵심 설계 결정

### 게임 상태: `GameState` enum

```csharp
public enum GameState
{
    None,       // 초기화 전 (기본값)
    Logo,       // 앱 최초 실행 — 코어 시스템 초기화
    Login,      // 로그인 화면
    MainMenu,   // 메인 메뉴
    InGame,     // 게임 플레이 중
    Pause,      // 일시정지 (InGame에서만 진입 가능)
    GameOver,   // 게임 종료 결과 화면
}
```

### 허용된 상태 전이

```
Logo ──────→ Login
Login ─────→ MainMenu
MainMenu ──→ InGame
InGame ────→ Pause
InGame ────→ GameOver
Pause ─────→ InGame      (재개)
Pause ─────→ MainMenu    (포기)
GameOver ──→ MainMenu
GameOver ──→ InGame      (재도전)
```

- 허용되지 않은 전이는 경고 로그 출력 후 무시
- 동일 상태로의 재전이는 무시 (중복 호출 안전)

### 싱글턴 패턴: 씬 진입 시 중복 파괴

```csharp
// 씬 재진입 시 두 번째 인스턴스 자동 파괴
void Awake()
{
    if (Instance != null && Instance != this)
    {
        Destroy(gameObject);
        return;
    }
    Instance = this;
    DontDestroyOnLoad(gameObject);
    Initialize();
}
```

---

## 클래스 구조

```
GameManager : MonoBehaviour
├── Instance (static)                    ← 싱글턴 접근자
├── CurrentState : GameState             ← 현재 상태 (읽기 전용)
│
├── ChangeState(GameState next)          ← 상태 전이 요청
│
├── Initialize()                         ← Awake에서 1회 호출, 코어 시스템 초기화
│
├── OnApplicationPause(bool)             ← 앱 백그라운드 진입/복귀
├── OnApplicationFocus(bool)             ← 앱 포커스 변경
└── OnApplicationQuit()                  ← 앱 종료
```

---

## 파일 구성

```
Assets/
└── Scripts/
    └── Core/
        └── GameManager.cs
```

---

## 상세 구현 명세

### GameManager.cs

```csharp
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public GameState CurrentState { get; private set; } = GameState.None;

    // 허용된 상태 전이 테이블
    private static readonly Dictionary<GameState, HashSet<GameState>> _validTransitions = new()
    {
        { GameState.Logo,     new() { GameState.Login } },
        { GameState.Login,    new() { GameState.MainMenu } },
        { GameState.MainMenu, new() { GameState.InGame } },
        { GameState.InGame,   new() { GameState.Pause, GameState.GameOver } },
        { GameState.Pause,    new() { GameState.InGame, GameState.MainMenu } },
        { GameState.GameOver, new() { GameState.MainMenu, GameState.InGame } },
    };

    void Awake() { ... }

    private void Initialize()
    {
        // 코어 시스템 초기화 순서
        // 1. EventBus — 정적 클래스라 별도 초기화 불필요
        // 2. 이후 시스템은 각 시스템의 Awake에서 자체 초기화
        ChangeState(GameState.Logo);
    }

    public void ChangeState(GameState next)
    {
        if (CurrentState == next) return;

        if (!IsValidTransition(CurrentState, next))
        {
            Debug.LogWarning($"[GameManager] Invalid transition: {CurrentState} → {next}");
            return;
        }

        GameState previous = CurrentState;
        CurrentState = next;

        EventBus.Publish(new GameStateChanged(previous, next));
    }

    private bool IsValidTransition(GameState from, GameState to)
    {
        // None → 어디든 허용 (초기 Logo 진입)
        if (from == GameState.None) return true;
        return _validTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            // 포커스 잃음 — 오디오 뮤트, 진행 중 상태 저장
            EventBus.Publish(new AppPauseChanged(true));
        }
        else
        {
            // 복귀 — 오디오 복원
            EventBus.Publish(new AppPauseChanged(false));
        }
    }

    void OnApplicationFocus(bool hasFocus)
    {
        EventBus.Publish(new AppFocusChanged(hasFocus));
    }

    void OnApplicationQuit()
    {
        // 강제 종료 전 데이터 저장 트리거
        EventBus.Publish(new AppQuitRequested());
    }
}
```

#### ChangeState 내부 동작 흐름

```
ChangeState(next) 호출
    │
    ├─ CurrentState == next → 즉시 반환 (중복 무시)
    │
    ├─ IsValidTransition 검사
    │   ├─ 실패 → 경고 로그 후 반환
    │   └─ 성공 → 계속
    │
    ├─ CurrentState = next 갱신
    │
    └─ EventBus.Publish(GameStateChanged(previous, next))
        └─ 구독한 모든 시스템에 상태 변경 통보
           (SceneLoader, UIManager, AudioSystem 등)
```

### GameEvents.cs 추가 이벤트

아래 이벤트를 `GameEvents.cs`에 추가:

```csharp
// 앱 생명주기
public record AppPauseChanged(bool IsPaused);
public record AppFocusChanged(bool HasFocus);
public record AppQuitRequested();
```

> `GameStateChanged`는 EventBus 단계에서 이미 정의됨

---

## 코어 시스템 초기화 순서

GameManager는 코어 시스템의 초기화 순서를 조율하는 루트다.  
각 시스템은 자체 `Awake`에서 초기화하되, `Script Execution Order` 설정으로 순서를 보장한다.

| 순서 | 시스템 | 방식 |
|------|--------|------|
| -100 | GameManager | Script Execution Order |
| -90  | EventBus | 정적 클래스, 자동 초기화 |
| -80  | SaveSystem | Script Execution Order |
| -70  | AudioSystem | Script Execution Order |
| -60  | SceneLoader | Script Execution Order |
| -50  | UIManager | Script Execution Order |

> `Project Settings → Script Execution Order`에서 설정

---

## Unity 씬 구성

### Logo 씬 구조

```
[Scene: Logo]
└── GameManagers (GameObject, DontDestroyOnLoad)
    ├── GameManager.cs
    ├── SceneLoader.cs
    ├── AudioSystem.cs
    ├── UIManager.cs
    └── SaveSystem.cs
```

- 앱 최초 진입 씬은 항상 `Logo`
- Logo에서 코어 시스템 초기화 완료 후 `Login` 씬으로 자동 전환
- `GameManagers` 오브젝트는 `DontDestroyOnLoad`로 유지

### Logo → Login 자동 전환

```csharp
// GameManager.Initialize() 에서
IEnumerator LogoSequence()
{
    // 1. 코어 시스템이 Awake 완료될 때까지 1프레임 대기
    yield return null;

    // 2. SaveSystem에서 설정 로드
    // (SaveSystem 구현 후 연결)

    // 3. 초기화 완료 → Login 로드
    ChangeState(GameState.Logo);
    // SceneLoader가 GameStateChanged 구독 후 Login 로드 처리
}
```

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 씬 재진입으로 GameManager 두 번째 생성 | Awake에서 중복 감지 → Destroy(gameObject) |
| 허용되지 않은 상태 전이 요청 | 경고 로그 출력 후 무시, 상태 변경 없음 |
| 동일 상태로 재전이 | 조기 반환, 이벤트 미발행 |
| OnApplicationPause와 OnApplicationFocus 동시 호출 | 각각 독립 이벤트 발행, 수신측에서 필요한 것만 처리 |
| OnApplicationQuit에서 저장 실패 | SaveSystem이 백업 파일로 복원 처리 (SaveSystem 책임) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | 앱 시작 → Initialize | GameState.Logo, GameStateChanged(None, Logo) 발행 |
| 2 | Logo → Login 전이 | CurrentState == Login, 이벤트 발행 |
| 3 | Login → MainMenu 전이 | CurrentState == MainMenu, 이벤트 발행 |
| 4 | Logo → InGame 전이 (허용 안 됨) | 경고 로그, 상태 유지 |
| 5 | InGame → InGame 재전이 | 이벤트 미발행, 상태 유지 |
| 6 | InGame → Pause → InGame 재개 | 각 전이마다 이벤트 발행 |
| 7 | 씬 재진입으로 두 번째 GameManager 생성 | 두 번째 인스턴스 즉시 Destroy |
| 8 | OnApplicationPause(true) | AppPauseChanged(true) 발행 |
| 9 | OnApplicationQuit | AppQuitRequested 발행 |

---

## 구현 시 주의사항

- **Logo 씬 필수**: GameManager는 반드시 Logo 씬에만 배치. 다른 씬에 배치 시 중복 방지 로직이 동작하지만 초기화 순서가 꼬일 수 있음.
- **ChangeState 직접 호출 제한**: 게임 로직에서 직접 호출보다 EventBus를 통해 상태 전이를 요청하는 패턴도 고려. 현재는 직접 호출 허용.
- **OnApplicationPause 신뢰성**: Android에서 `OnApplicationPause`와 `OnApplicationFocus` 동작이 기기/OS 버전마다 상이할 수 있음. 두 콜백 모두 처리하여 커버.
- **DontDestroyOnLoad 오브젝트 정리**: 여러 시스템이 같은 `GameManagers` GameObject 하위에 묶여 있어야 씬 재진입 시 일괄 중복 방지 가능.

---

## 구현 후 체크리스트

- [ ] `GameState.cs` 열거형 파일 작성
- [ ] `GameManager.cs` 작성 (싱글턴, 상태 전이, 앱 생명주기)
- [ ] `GameEvents.cs`에 `AppPauseChanged`, `AppFocusChanged`, `AppQuitRequested` 추가
- [ ] Logo 씬 생성 및 `GameManagers` 오브젝트 구성
- [ ] Script Execution Order 설정 (-100)
- [ ] 테스트 시나리오 8개 검증
- [ ] SceneLoader 연동 테스트 (다음 단계 연결 확인)
