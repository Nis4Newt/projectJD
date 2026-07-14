# 씬별 매니저(SceneManager) 구현 계획

> Phase 1 후속 — GameManager/SceneLoader/Singleton/EventBus 구현 이후 추가되는 인프라
> 의존 관계: EventBus, GameState, SceneLoader(간접, GameState↔씬 매핑 참고)
> 범위: 씬 진입/퇴장 시점에 그 씬만의 로직을 담당하는 씬-로컬 매니저 베이스. UI 시스템(UIManager)이나 씬 전환 연출은 제외

---

## 배경 / 문제 인식

현재 `GameManager`, `SceneLoader`는 둘 다 `Singleton<T>`를 상속하는 **영속(DontDestroyOnLoad) 싱글턴**이다. 앱 전체 생명주기를 다루기 때문에 씬이 바뀌어도 살아있어야 한다.

반면 "Login 씬 진입 시 자동 로그인 시도", "MainMenu 씬에서 배너 갱신", "InGame 씬에서 일시정지 오버레이 토글"처럼 **그 씬에서만 의미 있는 로직**은 지금 둘 곳이 없다. `GameManager`나 `SceneLoader`에 욱여넣으면 전역 시스템이 특정 씬의 사정을 알아야 하는 역방향 의존이 생긴다.

`plan-singleton.md`에서 이미 이 문제를 예견했다:

> 씬 스코프 싱글턴이 실제로 필요해지는 시점에 별도 타입(`SceneSingleton<T>` 등)으로 분리 검토 — 지금은 YAGNI.

지금이 그 시점이므로, `Singleton<T>`와 대칭되는 **씬 스코프 싱글턴 베이스**를 추가하고, 그 위에 씬별 매니저(`LoginSceneManager`, `MainMenuSceneManager`, `InGameSceneManager` 등)를 얹는다.

---

## 설계 목표

- 씬마다 "그 씬만의 진입/퇴장 로직"을 담당하는 자리를 하나로 통일
- `GameManager` / `SceneLoader`(영속 싱글턴)와 명확히 구분되는 생명주기: 씬이 바뀌면 자동으로 파괴됨
- 전역 시스템(GameManager, SceneLoader)을 직접 참조하지 않고 EventBus로만 연동 — 기존 원칙 유지
- 구독 해제 누락 방지: `CompositeDisposable` 패턴을 그대로 재사용
- 씬당 매니저는 1개만 존재하도록 중복 생성 방지
- 새 시스템을 발명하기보다 기존 `Singleton<T>` 구조를 그대로 미러링 — 학습 비용 최소화

---

## 핵심 설계 결정

### 영속 싱글턴 vs 씬 싱글턴

```
Singleton<T>       — DontDestroyOnLoad, 앱 전체 생명주기 (GameManager, SceneLoader)
SceneSingleton<T>  — 씬 로컬, 씬 전환(Single 모드 로드) 시 Unity가 자동으로 GameObject 파괴
```

Unity의 `SceneManager.LoadSceneAsync(name)`은 기본이 `LoadSceneMode.Single`이라, 새 씬을 로드하면 이전 씬의 모든 GameObject가 자동으로 파괴된다(`SceneLoader.cs` 참고). 즉 씬 싱글턴은 **별도의 "씬 퇴장" 이벤트 없이 `OnDestroy`만으로 정리가 끝난다** — `Singleton<T>`처럼 파괴 로직을 따로 챙길 필요가 없고, `DontDestroyOnLoad` 호출만 빼면 된다.

### `SceneSingleton<T>`: `Singleton<T>`와 동일한 템플릿 메서드 패턴

```csharp
public abstract class SceneSingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    public static T Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this as T)
        {
            Debug.LogWarning($"[SceneSingleton] 씬 내 중복 {typeof(T).Name} 감지, 파괴: {gameObject.name}");
            Destroy(gameObject);
            return;
        }

        Instance = this as T;
        OnAwake();
    }

    protected virtual void OnAwake() { }

    protected virtual void OnDestroy()
    {
        if (Instance == this as T)
            Instance = null;
    }
}
```

- `Singleton<T>`와 차이는 단 하나: `DontDestroyOnLoad(gameObject)` 호출이 없다는 것
- 서브클래스는 `Awake()`가 아니라 `OnAwake()`를 오버라이드 (기존 관례 그대로)
- `OnDestroy`를 오버라이드하는 서브클래스는 `base.OnDestroy()` 필수 호출 (기존 관례 그대로)

### 씬 매니저는 GameManager/SceneLoader를 직접 참조하지 않는다

```csharp
// InGameSceneManager 예시 — GameManager.Instance.CurrentState를 직접 읽지 않고
// GameStateChanged 이벤트만 구독해서 그 씬 안에서 필요한 반응(일시정지 오버레이 등)을 처리
_subs.Add(EventBus.Subscribe<GameStateChanged>(OnGameStateChanged));
```

- `plan-sceneloader.md`에서 세운 "직접 참조 대신 이벤트 구독" 원칙을 씬 매니저에도 동일 적용
- 같은 씬 내에서 상태만 바뀌는 경우(`InGame → Pause`처럼 `SceneLoader`의 `_stateSceneMap`에 없는 전이)는 씬 전환이 일어나지 않으므로, 그 반응은 전적으로 해당 씬 매니저의 책임

### 구독 관리: `CompositeDisposable` 재사용

```csharp
private readonly CompositeDisposable _subs = new();

protected override void OnAwake()
{
    _subs.Add(EventBus.Subscribe<GameStateChanged>(OnGameStateChanged));
}

protected override void OnDestroy()
{
    _subs.Dispose();
    base.OnDestroy();
}
```

- 새 메커니즘 도입 없이 `plan-eventbus.md`에서 정의한 패턴을 그대로 따름

### `BootSceneManager`: 씬 진입에 반응만 하는 다른 매니저와 달리 전이를 "요청"하는 특수 케이스

`LoginSceneManager` / `MainMenuSceneManager` / `InGameSceneManager`는 전부 이미 벌어진 상태 변화(`GameStateChanged`)에 반응만 한다. 반면 `Bootstrap` 씬은 그 반대다 — 앱이 켜지자마자 `GameManager`가 `None → Boot`로 진입하지만, 그 다음 `Boot → Login` 전이를 실제로 트리거하는 주체가 아직 없다(`plan-gamemanager.md`의 `BootSequence`는 `Boot` 진입까지만 담당).

`BootSceneManager`가 이 역할을 맡는다: 스플래시/로고 연출, 최소 노출 시간 대기 등 **Bootstrap 씬 전용 연출**을 담당하고, 끝나면 `GameManager.Instance.ChangeState(...)`를 직접 호출하는 대신 **`BootSceneReady` 이벤트만 발행**한다. `GameManager`가 그 이벤트를 구독해 `ChangeState(GameState.Login)`을 호출한다.

```csharp
// BootSceneManager — 연출이 끝나면 이벤트로만 알림
EventBus.Publish(new BootSceneReady());

// GameManager — 이벤트를 구독해 전이를 직접 수행 (ChangeState 호출 권한은 GameManager가 계속 보유)
EventBus.Subscribe<BootSceneReady>(_ => ChangeState(GameState.Login));
```

- `ChangeState` 호출 권한은 여전히 `GameManager` 한 곳에만 있다 — `BootSceneManager`가 직접 `GameManager.Instance`를 참조하며 상태를 바꾸는 걸 막기 위함
- `GameManager`도 `BootSceneManager.Instance`를 참조하지 않는다 — "씬 매니저는 전역 시스템을 직접 참조하지 않는다" 원칙이 반대 방향에도 동일하게 적용됨
- `GameState.Boot`는 `_validTransitions`상 재진입 경로가 없으므로 `BootSceneReady`는 앱 생명주기 동안 정확히 한 번만 유효하게 발행됨 (중복 발행돼도 `GameManager.ChangeState`의 `CurrentState == next` 가드로 안전)

---

## 클래스 구조

```
SceneSingleton<T> : MonoBehaviour where T : MonoBehaviour   (abstract, Core 공용)
├── Instance : T (static)
├── OnAwake()     ← protected virtual, 서브클래스 초기화 훅
└── OnDestroy()   ← protected virtual, Instance 정리

BootSceneManager     : SceneSingleton<BootSceneManager>       ← Bootstrap 씬 전용 (스플래시 연출 → BootSceneReady 발행)
LoginSceneManager    : SceneSingleton<LoginSceneManager>      ← Login 씬 전용
MainMenuSceneManager : SceneSingleton<MainMenuSceneManager>   ← MainMenu 씬 전용
InGameSceneManager   : SceneSingleton<InGameSceneManager>     ← InGame 씬 전용
```

각 씬 매니저는 서로를 모르고, 서로의 존재도 필요 없다. 오직 `EventBus`와 자기 씬 안의 오브젝트만 안다. `BootSceneManager`만 예외적으로 `GameManager`에게 "다음으로 넘어가도 된다"는 신호(`BootSceneReady`)를 보내지만, 이 역시 이벤트를 통해서만 이루어진다.

---

## 파일 구성

```
Assets/
└── Scripts/
    ├── Core/
    │   ├── Singleton.cs
    │   ├── SceneSingleton.cs        ← 신규 (영속 싱글턴과 대칭)
    │   ├── GameManager.cs
    │   ├── GameState.cs
    │   ├── Event/
    │   └── Scene/
    │       └── SceneLoader.cs
    ├── Boot/
    │   └── BootSceneManager.cs       ← 신규 (Bootstrap 씬 전용, 예시 스켈레톤)
    ├── Login/
    │   └── LoginSceneManager.cs      ← 신규 (예시 스켈레톤)
    ├── MainMenu/
    │   └── MainMenuSceneManager.cs   ← 신규 (예시 스켈레톤)
    └── InGame/
        └── InGameSceneManager.cs     ← 신규 (예시 스켈레톤)
```

`SceneSingleton.cs`는 `Singleton.cs`와 마찬가지로 특정 하위 시스템에 속하지 않는 공용 베이스이므로 `Core/` 바로 아래 배치. 씬별 매니저는 각 씬 전용 로직이므로 `Core/` 밖, 씬 이름과 같은 폴더에 배치.

---

## 상세 구현 명세

### SceneSingleton.cs

```csharp
using UnityEngine;

namespace JungleDice.Core
{
    public abstract class SceneSingleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        public static T Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this as T)
            {
                Debug.LogWarning($"[SceneSingleton] 씬 내 중복 {typeof(T).Name} 감지, 파괴: {gameObject.name}");
                Destroy(gameObject);
                return;
            }

            Instance = this as T;
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

### BootSceneManager.cs (예시)

```csharp
using System.Collections;
using UnityEngine;
using JungleDice.Core;
using JungleDice.Core.Event;

namespace JungleDice.Boot
{
    public class BootSceneManager : SceneSingleton<BootSceneManager>
    {
        [SerializeField] private float _minSplashDuration = 1.5f;

        protected override void OnAwake()
        {
            StartCoroutine(BootRoutine());
        }

        private IEnumerator BootRoutine()
        {
            // 스플래시/로고 연출, 버전 표시 등 Bootstrap 씬 전용 연출
            yield return new WaitForSeconds(_minSplashDuration);

            // 준비 완료를 이벤트로만 알림 — GameManager를 직접 참조하지 않음
            EventBus.Publish(new BootSceneReady());
        }
    }
}
```

### GameManager.cs 변경 — `BootSceneReady` 구독 추가

```csharp
protected override void OnAwake()
{
    EventBus.Subscribe<GameStateChanged>(gsc => Debug.LogError($"GameStateChanged: {gsc.Previous} → {gsc.Next}"));
    EventBus.Subscribe<BootSceneReady>(_ => ChangeState(GameState.Login));
    StartCoroutine(BootSequence());
}
```

- `BootSequence()`는 지금처럼 `None → Boot` 진입까지만 담당, `Boot → Login` 전이는 `BootSceneReady` 수신 시 처리
- `GameEvents.cs`에 `public record BootSceneReady();` 추가 필요

### LoginSceneManager.cs (예시)

```csharp
using JungleDice.Core;
using JungleDice.Core.Event;

namespace JungleDice.Login
{
    public class LoginSceneManager : SceneSingleton<LoginSceneManager>
    {
        private readonly CompositeDisposable _subs = new();

        protected override void OnAwake()
        {
            _subs.Add(EventBus.Subscribe<AppFocusChanged>(OnAppFocusChanged));

            // 씬 진입 시 초기화 로직 (자동 로그인 시도 등)
            // TryAutoLogin();
        }

        private void OnAppFocusChanged(AppFocusChanged e)
        {
            // 포커스 복귀 시 로그인 화면 갱신 등
        }

        protected override void OnDestroy()
        {
            _subs.Dispose();
            base.OnDestroy();
        }
    }
}
```

### InGameSceneManager.cs (예시 — 씬 전환 없는 상태 반응)

```csharp
using JungleDice.Core;
using JungleDice.Core.Event;

namespace JungleDice.InGame
{
    public class InGameSceneManager : SceneSingleton<InGameSceneManager>
    {
        private readonly CompositeDisposable _subs = new();

        protected override void OnAwake()
        {
            _subs.Add(EventBus.Subscribe<GameStateChanged>(OnGameStateChanged));
        }

        private void OnGameStateChanged(GameStateChanged e)
        {
            // InGame ↔ Pause는 SceneLoader의 _stateSceneMap에 없어 씬 전환이 일어나지 않음
            // → 오버레이 표시/숨김은 이 씬 매니저가 전담
            if (e.Next == GameState.Pause)
            {
                // ShowPauseOverlay();
            }
            else if (e.Previous == GameState.Pause && e.Next == GameState.InGame)
            {
                // HidePauseOverlay();
            }
        }

        protected override void OnDestroy()
        {
            _subs.Dispose();
            base.OnDestroy();
        }
    }
}
```

---

## Unity 씬/오브젝트 구성

```
[Scene: Bootstrap]
├── GameManagers (GameObject, DontDestroyOnLoad)
│   ├── GameManager.cs
│   └── SceneLoader.cs
└── BootManagers (GameObject, 씬 로컬 — DontDestroyOnLoad 아님)
    └── BootSceneManager.cs

[Scene: Login]
└── LoginManagers (GameObject, 씬 로컬 — DontDestroyOnLoad 아님)
    └── LoginSceneManager.cs

[Scene: MainMenu]
└── MainMenuManagers (GameObject)
    └── MainMenuSceneManager.cs

[Scene: InGame]
└── InGameManagers (GameObject)
    └── InGameSceneManager.cs
```

`Bootstrap` 씬 안에 영속 오브젝트(`GameManagers`)와 씬 로컬 오브젝트(`BootManagers`)가 함께 있다는 점에 주의: `BootSceneManager`는 `Login`으로 전환되는 순간 Unity가 자동으로 파괴하며, `DontDestroyOnLoad`를 호출하지 않는다. `Login` / `MainMenu` / `InGame`의 `{Scene}Managers`도 마찬가지로 씬이 바뀌면 자동 파괴된다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 같은 씬을 다시 로드해서 씬 매니저 두 번째 생성 | `Awake`에서 중복 감지 → 경고 로그 후 `Destroy(gameObject)`, `OnAwake()` 미호출 |
| 씬 전환(Single 모드 로드)으로 이전 씬 매니저 파괴 | Unity가 자동으로 `OnDestroy` 호출 → `_subs.Dispose()`로 구독 정리, `Instance = null` |
| `GameStateChanged`가 씬 전환 없는 상태로 전이 (`InGame ↔ Pause`) | 씬은 그대로 유지되므로 해당 씬 매니저(`InGameSceneManager`)가 이벤트로 직접 반응 |
| 씬 매니저가 `GameManager.Instance`를 직접 참조 | 설계 원칙 위반 — 반드시 `EventBus` 구독으로 대체해야 함 (코드 리뷰로 방지) |
| `OnDestroy`를 오버라이드하면서 `base.OnDestroy()` 누락 | `Instance`가 파괴된 오브젝트를 계속 가리켜 다음 씬 로직이 꼬일 수 있음 |
| `BootSceneReady`가 여러 번 발행됨 (코루틴 중복 실행 등) | `GameManager.ChangeState`의 `CurrentState == next` 가드로 두 번째 호출은 조기 반환, 부작용 없음 |
| `BootSceneManager`가 파괴된 뒤에도 `GameManager`의 `BootSceneReady` 구독이 남아있음 | 문제 없음 — `GameState.Boot`는 `_validTransitions`상 재진입 경로가 없어 앱 생명주기 동안 다시 발행될 일이 없음 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | Bootstrap 씬 시작 → `BootSceneManager.OnAwake()` | `BootRoutine` 코루틴 시작, `_minSplashDuration` 대기 |
| 2 | 스플래시 대기 종료 | `BootSceneReady` 발행 → `GameManager.ChangeState(Login)` 자동 호출 → `SceneLoader`가 Login 씬 로드 |
| 3 | Login 씬 로드 | `LoginSceneManager.Instance` 할당, `OnAwake()` 호출, `BootSceneManager`는 Bootstrap 씬과 함께 자동 파괴 |
| 4 | Login → MainMenu 전이 (씬 전환 발생) | `LoginSceneManager` 자동 파괴, 구독 해제, `MainMenuSceneManager` 새로 생성 |
| 5 | InGame 중 `GameStateChanged(InGame, Pause)` 발행 | 씬 전환 없이 `InGameSceneManager`가 오버레이 표시 |
| 6 | `GameStateChanged(Pause, InGame)` 발행 (재개) | `InGameSceneManager`가 오버레이 숨김 |
| 7 | 같은 씬을 재로드 (예: InGame 재도전) | 두 번째 `InGameSceneManager` 즉시 파괴, 기존 `Instance` 유지 안 됨(씬 자체가 새로 로드되므로 기존 것도 파괴된 뒤 새로 생성됨) |
| 8 | 씬 전환 후 이전 씬 매니저의 구독이 여전히 호출되는지 확인 | 호출되지 않음 (메모리 릭 없음 검증) |

---

## 구현 시 주의사항

- **`SceneSingleton<T>`는 `DontDestroyOnLoad`를 호출하지 않는다**: `Singleton<T>`와 이름이 비슷해 혼동하기 쉬움. 영속 시스템(GameManager, SceneLoader, 향후 AudioSystem 등)은 `Singleton<T>`, 씬 전용 매니저는 `SceneSingleton<T>`로 명확히 구분.
- **씬 매니저는 전역 시스템을 직접 참조하지 않는다**: `GameManager.Instance`, `SceneLoader.Instance` 참조 금지, `EventBus` 구독만 사용 (`plan-sceneloader.md` 원칙과 동일).
- **`OnDestroy` 오버라이드 시 `base.OnDestroy()` 필수**: `Singleton<T>`와 동일한 규칙.
- **씬 매니저 오브젝트는 씬당 1개**: 씬 루트에 `{SceneName}Managers` 오브젝트 하나만 두고 그 안에 씬 매니저 컴포넌트를 붙인다.
- **Additive 로드 도입 시 재검토 필요**: 현재 `SceneLoader`는 `LoadSceneMode.Single`만 사용하므로 이전 씬 파괴가 보장됨. 이후 로딩 화면 등을 위해 Additive 로드를 도입하면 씬 매니저의 자동 파괴 전제가 깨지므로 이 문서를 다시 검토해야 함.
- **`BootSceneManager`는 예외적으로 전이를 유발하지만 `ChangeState`를 직접 호출하지 않는다**: `EventBus.Publish(new BootSceneReady())`까지만 하고, 실제 `ChangeState(Login)` 호출은 `GameManager`가 구독을 통해 수행. 이 경계를 허물면 "씬 매니저는 전역 시스템을 직접 참조하지 않는다" 원칙이 Boot 케이스부터 깨짐.
- **`GameEvents.cs`에 `BootSceneReady` 추가 필요**: `plan-eventbus.md`의 이벤트 정의 관례(순수 데이터 레코드)를 따라 `public record BootSceneReady();`로 추가.

---

## 구현 후 체크리스트

- [ ] `SceneSingleton.cs` 작성 (`Assets/Scripts/Core/SceneSingleton.cs`)
- [ ] `BootSceneManager.cs` 작성 (스플래시 연출 → `BootSceneReady` 발행)
- [ ] `LoginSceneManager.cs`, `MainMenuSceneManager.cs`, `InGameSceneManager.cs` 스켈레톤 작성
- [ ] `GameEvents.cs`에 `BootSceneReady` 추가
- [ ] `GameManager.OnAwake()`에 `EventBus.Subscribe<BootSceneReady>(_ => ChangeState(GameState.Login))` 추가
- [ ] Bootstrap / Login / MainMenu / InGame 씬에 각각 씬 매니저 오브젝트 배치 (`BootSceneManager`는 영속 `GameManagers`와 별도 오브젝트로 분리 배치)
- [ ] `plan-sceneloader.md`의 `_stateSceneMap`에 `GameState.Boot → "BootStrap"` 반영 확인
- [ ] 테스트 시나리오 8개 검증
- [ ] `plan-singleton.md`의 `SceneSingleton<T>` 후속 항목 체크 반영
- [ ] (향후) UIManager 도입 시 씬 매니저와의 연동 방식 별도 계획 문서 작성
