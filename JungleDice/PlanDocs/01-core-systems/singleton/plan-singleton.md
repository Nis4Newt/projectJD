# Singleton<T> 구현 계획

> 상위 문서: [GameManager 구현 계획](../gamemanager/plan-gamemanager.md), [SceneLoader 구현 계획](../sceneloader/plan-sceneloader.md) (두 시스템의 중복 보일러플레이트를 통합하며 파생)
> Phase 1 — 리팩터링 (신규 시스템 아님)
> 의존 관계: 없음 (`GameManager`, `SceneLoader`가 이 클래스로 마이그레이션됨)
> 범위: MonoBehaviour 싱글턴 보일러플레이트 통합. 씬 스코프 싱글턴(파괴 시 재생성 등)은 이번 범위에서 제외

---

## 배경 / 문제 인식

`GameManager`, `SceneLoader` 모두 아래 패턴을 각자 중복 구현하고 있음:

```csharp
public static T Instance { get; private set; }

void Awake()
{
    if (Instance != null && Instance != this)
    {
        Destroy(gameObject);
        return;
    }
    Instance = this;
    DontDestroyOnLoad(gameObject);
    // 이후 개별 초기화 로직
}
```

`plan-core-systems.md` 기준 앞으로 `AudioSystem`, `UIManager`, `InputManager`, `PoolManager` 등도 동일 패턴을 필요로 하므로, 지금 공통 베이스로 뽑아두지 않으면 중복이 계속 늘어남.

---

## 설계 목표

- 싱글턴 등록/중복 파괴/`DontDestroyOnLoad` 로직을 한 곳에서 관리
- 각 시스템은 `Instance` 선언과 `Awake` 보일러플레이트 없이, 자신의 초기화 로직만 작성
- 기존 `GameManager.Instance`, `SceneLoader.Instance` 사용부(외부 API)는 변경 없이 유지
- 서브클래스가 `Awake`를 직접 오버라이드해서 `base.Awake()` 호출을 빠뜨리는 실수를 구조적으로 방지 (템플릿 메서드 패턴)
- 씬 재진입 시 중복 인스턴스는 지금처럼 즉시 `Destroy`

---

## 핵심 설계 결정

### CRTP 기반 제네릭 베이스 클래스

```csharp
public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
```

- `T`는 상속받는 구체 클래스 자기 자신 (`class GameManager : Singleton<GameManager>`)
- `abstract` — 베이스 자체는 컴포넌트로 붙일 이유가 없으므로 직접 인스턴스화 방지

### `Awake`는 봉인, 초기화는 `OnAwake` 훅으로 위임

```csharp
private void Awake()
{
    if (Instance != null && Instance != this)
    {
        Destroy(gameObject);
        return;
    }
    Instance = this as T;
    DontDestroyOnLoad(gameObject);
    OnAwake();
}

protected virtual void OnAwake() { }
```

- 서브클래스는 `Awake()`가 아니라 `OnAwake()`를 오버라이드
- `base.Awake()` 호출 누락, 중복 인스턴스인데도 초기화 로직이 도는 실수 자체가 불가능해짐 (중복이면 `Destroy` 후 `return`되어 `OnAwake` 호출 안 됨)
- `GameManager.Initialize()`, `SceneLoader`의 `EventBus.Subscribe` 호출부가 `OnAwake()` 안으로 이동

### `Instance` 해제 시점

```csharp
protected virtual void OnDestroy()
{
    if (Instance == this)
        Instance = null;
}
```

- 중복 인스턴스가 `Destroy`될 때는 `Instance`가 이미 살아남은 인스턴스를 가리키므로 이 체크로 실수로 지우지 않음
- 서브클래스가 `OnDestroy`가 필요하면 반드시 `base.OnDestroy()` 호출 (예: `GameManager`는 현재 `OnDestroy` 미사용이므로 해당 없음)

### `DontDestroyOnLoad`는 항상 적용 (옵션화하지 않음)

```
지금 두 대상(GameManager, SceneLoader) 모두 씬 간 유지가 필수.
plan-core-systems.md의 다른 예정 시스템(AudioSystem, UIManager, InputManager, PoolManager)도 전부 영속 싱글턴.
씬 스코프 싱글턴이 실제로 필요해지는 시점에 별도 타입(SceneSingleton<T> 등)으로 분리 검토 — 지금은 YAGNI.
```

---

## 클래스 구조

```
Singleton<T> : MonoBehaviour where T : MonoBehaviour   (abstract)
├── Instance : T (static, 읽기 전용 접근자)
├── Awake()                 ← private, 봉인. 싱글턴 등록/중복 파괴 처리 후 OnAwake() 호출
├── OnAwake()                ← protected virtual, 서브클래스 초기화 훅 (기본 구현 없음)
└── OnDestroy()               ← protected virtual, Instance 정리
```

---

## 파일 구성

```
Assets/
└── Scripts/
    └── Core/
        └── Singleton.cs
```

`Core` 바로 아래 배치 — `GameManager.cs`와 같은 위치, 특정 하위 시스템에 속하지 않는 공용 베이스이므로.

---

## 상세 구현 명세

### Singleton.cs

```csharp
using UnityEngine;

namespace JungleDice.Core
{
    public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        public static T Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this as T)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this as T;
            DontDestroyOnLoad(gameObject);
            OnAwake();
        }

        protected virtual void OnAwake() { }

        protected virtual void OnDestroy()
        {
            if (Instance == this as T)
                Instance = null;
        }
    }
}
```

### GameManager.cs 변경

```csharp
public class GameManager : Singleton<GameManager>
{
    public GameState CurrentState { get; private set; } = GameState.None;

    private static readonly Dictionary<GameState, HashSet<GameState>> _validTransitions = new() { ... };

    protected override void OnAwake()
    {
        EventBus.Subscribe<GameStateChanged>(gsc => { Debug.LogError($"GameStateChanged: {gsc.Previous} → {gsc.Next}"); });
        StartCoroutine(LogoSequence());
    }

    // LogoSequence(), ChangeState(), IsValidTransition(),
    // OnApplicationPause/Focus/Quit 는 변경 없음
}
```

- `public static GameManager Instance { get; private set; }` 선언 제거 (베이스 상속)
- 기존 `Awake()` / `Initialize()` 제거, 내용을 `OnAwake()`로 이동

### SceneLoader.cs 변경

```csharp
public class SceneLoader : Singleton<SceneLoader>
{
    public bool IsLoading { get; private set; }

    private static readonly Dictionary<GameState, string> _stateSceneMap = new() { ... };

    protected override void OnAwake()
    {
        EventBus.Subscribe<GameStateChanged>(OnGameStateChanged);
    }

    // OnGameStateChanged(), LoadScene(), LoadSceneRoutine() 는 변경 없음
}
```

- `public static SceneLoader Instance { get; private set; }` 선언 제거 (베이스 상속)
- 기존 `Awake()` 제거, 내용을 `OnAwake()`로 이동

### 외부 사용부 영향

```
GameManager.Instance.ChangeState(...) 등 기존 호출부는 시그니처 변경 없음.
Instance는 Singleton<T>에서 상속되지만 타입은 그대로 GameManager / SceneLoader이므로 호출 코드 수정 불필요.
```

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 씬 재진입으로 두 번째 인스턴스 생성 | `Awake`에서 중복 감지 → `Destroy(gameObject)`, `OnAwake()` 호출 안 됨 |
| 살아남은 인스턴스가 `Destroy`됨 (앱 종료 등) | `OnDestroy`에서 `Instance == this` 확인 후 `null`로 정리 |
| 중복 인스턴스가 `Destroy`됨 | `OnDestroy`에서 `Instance != this`이므로 살아있는 `Instance` 보존 |
| 서브클래스가 `OnDestroy`를 오버라이드하면서 `base.OnDestroy()` 누락 | `Instance`가 파괴된 오브젝트를 계속 가리킬 수 있음 — 구현 시 주의사항에 명시, 코드 리뷰로 방지 (컴파일 타임 강제는 어려움) |
| `T`가 `this as T`로 캐스팅 실패 (이론상 `class Foo : Singleton<Bar>`처럼 잘못 상속) | CRTP 관례상 발생하지 않아야 하나, 발생 시 `Instance`가 `null`로 남아 이후 접근에서 `NullReferenceException` — 계약 위반이므로 방어 코드 없이 실패하게 둠 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | `GameManager`, `SceneLoader` 각각 최초 `Awake` | `Instance`에 자기 자신 할당, `OnAwake()` 호출됨, `DontDestroyOnLoad` 적용 |
| 2 | 씬 재진입으로 `GameManager` 두 번째 인스턴스 생성 | 즉시 `Destroy`, 기존 `Instance` 유지, 두 번째 인스턴스의 `OnAwake()` 미호출 (초기화 로직 중복 실행 안 됨) |
| 3 | `GameManager.Instance.ChangeState(...)` 호출 | 기존과 동일하게 동작 (마이그레이션 전후 회귀 없음) |
| 4 | `SceneLoader`가 `GameStateChanged` 구독 후 자동 씬 전환 | 마이그레이션 전 `plan-sceneloader.md` 테스트 시나리오 7개 그대로 통과 |
| 5 | 앱 종료 시 `Instance` 파괴 | `OnDestroy`에서 `Instance`가 `null`로 정리됨 (재조회 시 예외 대신 `null` 반환 확인) |

---

## 구현 시 주의사항

- **`Awake`는 서브클래스에서 오버라이드하지 않는다**: 초기화 로직은 반드시 `OnAwake()`에 작성. `Awake()`를 새로 선언하면 베이스의 싱글턴 등록 로직을 가리게 되어 버그로 이어짐.
- **`OnDestroy`를 오버라이드하는 서브클래스는 `base.OnDestroy()` 필수 호출**: 현재 `GameManager`/`SceneLoader`는 `OnDestroy`를 쓰지 않아 해당 없음. 향후 시스템이 `OnDestroy`를 추가할 때 체크리스트 항목으로 재확인.
- **정적 상태 초기화 순서**: `Instance`는 Unity의 `Awake` 호출 시점에만 설정됨. `Start()`나 다른 컴포넌트의 `Awake()`에서 다른 싱글턴의 `Instance`를 참조할 경우 Script Execution Order에 의존하게 되므로, 가능하면 `EventBus` 구독/발행으로 간접 연동 (`SceneLoader`가 이미 이 원칙을 따름 — `plan-sceneloader.md` 참고).
- **`plan-core-systems.md`의 후속 시스템(AudioSystem, UIManager, InputManager, PoolManager)도 이 베이스를 사용**: 각 시스템 구현 계획 작성 시 `Singleton<T>` 상속을 기본값으로 명시.

---

## 구현 후 체크리스트

- [ ] `Singleton.cs` 작성 (`Assets/Scripts/Core/Singleton.cs`)
- [ ] `GameManager`가 `Singleton<GameManager>` 상속하도록 변경, `Instance` 선언 제거, `Awake` → `OnAwake` 이동
- [ ] `SceneLoader`가 `Singleton<SceneLoader>` 상속하도록 변경, `Instance` 선언 제거, `Awake` → `OnAwake` 이동
- [ ] `plan-sceneloader.md` 테스트 시나리오 7개 재검증 (회귀 없음 확인)
- [ ] `GameManager` 상태 전이 테스트 재검증 (회귀 없음 확인)
- [ ] 씬 재진입 시 두 클래스 모두 중복 인스턴스 `Destroy` 동작 확인
- [ ] (향후) `AudioSystem`, `UIManager`, `InputManager`, `PoolManager` 구현 계획에 `Singleton<T>` 상속 명시
