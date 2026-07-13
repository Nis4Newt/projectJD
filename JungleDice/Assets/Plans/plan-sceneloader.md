# SceneLoader 구현 계획

> Phase 1 — 세 번째 구현 대상
> 의존 관계: GameManager (`GameStateChanged` 구독), EventBus
> 범위: 씬 전환 핵심 로직만. 로딩 화면/전환 연출은 제외 (추후 별도 계획으로 확장)

---

## 설계 목표

- 씬 전환 핵심 로직만 우선 구현 (비동기 로드 + 상태 연동)
- GameManager를 직접 참조하지 않고 EventBus로만 연동 (결합도 최소화)
- 중복 로드 요청에 안전할 것 (로딩 중 재호출 방지)
- 로딩 화면/전환 연출/입력 차단은 이번 범위에서 제외 — 나중에 `LoadingScreen`을 얹어도 `SceneLoader` 핵심 API가 바뀌지 않도록 구조만 열어둠

---

## 핵심 설계 결정

### 씬 참조 방식: Build Settings 씬 이름 문자열

```
Addressables 미도입 상태이므로 SceneManager.LoadSceneAsync(string) 사용
씬 이름은 Build Settings에 등록되어 있어야 함
Addressables 도입 여부는 plan-core-systems.md 메모대로 리소스 규모 확정 후 재검토
```

### GameManager 연동: 직접 참조 대신 이벤트 구독

```csharp
// SceneLoader가 GameManager를 참조하지 않고 GameStateChanged만 구독
EventBus.Subscribe<GameStateChanged>(OnGameStateChanged);
```

- `GameState → 씬 이름` 매핑 테이블을 SceneLoader 내부에 보유
- 매핑이 없는 상태(`Pause`, `GameOver` 등 오버레이성 상태)는 씬 로드를 트리거하지 않음
- 매핑된 씬이 현재 활성 씬과 같으면 무시 (중복 로드 방지)

```csharp
private static readonly Dictionary<GameState, string> _stateSceneMap = new()
{
    { GameState.Logo,     "Logo" },
    { GameState.Login,    "Login" },
    { GameState.MainMenu, "MainMenu" },
    { GameState.InGame,   "InGame" },
};
```

### 중복 요청 방지

```csharp
public bool IsLoading { get; private set; }
```

- `IsLoading == true`일 때 새 `LoadScene` 호출은 경고 로그 후 무시

### 로딩 화면/전환 연출은 이번 범위에서 제외

```
페이드, 진행률 UI, 입력 차단막은 별도 시스템(LoadingScreen)으로 나중에 붙일 예정
지금은 SceneManager.LoadSceneAsync가 끝날 때까지 대기만 하고, 완료 시점을 이벤트로 알림
```

---

## 클래스 구조

```
SceneLoader : MonoBehaviour
├── Instance (static)                       ← 싱글턴 접근자
├── IsLoading : bool                        ← 읽기 전용, 로딩 중 여부
│
├── LoadScene(string sceneName)             ← 외부 공개 API, 내부적으로 코루틴 실행
│
├── Awake()                                 ← 싱글턴 초기화, GameStateChanged 구독
├── OnGameStateChanged(GameStateChanged e)  ← 상태 → 씬 매핑 후 자동 로드
└── LoadSceneRoutine(string sceneName)      ← 코루틴, 실제 로드 로직 (private)
```

---

## 파일 구성

```
Assets/
└── Scripts/
    └── Core/
        └── Scene/
            └── SceneLoader.cs
```

---

## 상세 구현 명세

### SceneLoader.cs

```csharp
public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }
    public bool IsLoading { get; private set; }

    private static readonly Dictionary<GameState, string> _stateSceneMap = new()
    {
        { GameState.Logo,     "Logo" },
        { GameState.Login,    "Login" },
        { GameState.MainMenu, "MainMenu" },
        { GameState.InGame,   "InGame" },
    };

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        EventBus.Subscribe<GameStateChanged>(OnGameStateChanged);
    }

    private void OnGameStateChanged(GameStateChanged e)
    {
        if (!_stateSceneMap.TryGetValue(e.Next, out var sceneName))
            return; // 씬 전환이 필요 없는 상태 (Pause, GameOver 등)

        if (SceneManager.GetActiveScene().name == sceneName)
            return; // 이미 해당 씬

        LoadScene(sceneName);
    }

    public void LoadScene(string sceneName)
    {
        if (IsLoading)
        {
            Debug.LogWarning($"[SceneLoader] 로딩 중 중복 요청 무시: {sceneName}");
            return;
        }

        StartCoroutine(LoadSceneRoutine(sceneName));
    }

    private IEnumerator LoadSceneRoutine(string sceneName)
    {
        IsLoading = true;
        EventBus.Publish(new SceneLoadRequested(sceneName));

        var op = SceneManager.LoadSceneAsync(sceneName);

        try
        {
            yield return op;
        }
        finally
        {
            IsLoading = false;
        }

        EventBus.Publish(new SceneLoadCompleted(sceneName));
    }
}
```

#### LoadSceneRoutine 내부 동작 흐름

```
LoadScene(sceneName) 호출
    │
    ├─ IsLoading == true → 경고 로그 후 무시, 종료
    │
    ├─ IsLoading = true, SceneLoadRequested 발행
    │
    ├─ SceneManager.LoadSceneAsync 실행 및 완료 대기
    │   └─ 실패해도 finally에서 IsLoading = false 보장
    │
    └─ IsLoading = false, SceneLoadCompleted 발행
```

### GameEvents.cs 추가 이벤트

```csharp
// 씬 시스템
public record SceneLoadRequested(string SceneName);
public record SceneLoadCompleted(string SceneName);
```

---

## Unity 씬/오브젝트 구성

```
[Scene: Logo]
└── GameManagers (GameObject, DontDestroyOnLoad)
    ├── GameManager.cs
    └── SceneLoader.cs
```

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 로딩 중 재요청 (`LoadScene` 중복 호출) | 경고 로그 후 무시, 기존 로딩 계속 진행 |
| 존재하지 않는 씬 이름 요청 | `SceneManager.LoadSceneAsync`가 에러 로그 발생, `finally`로 `IsLoading` 복구되어 이후 로드는 계속 가능 |
| `GameStateChanged`가 매핑되지 않은 상태로 전이 (`Pause`, `GameOver`) | 씬 로드 트리거 안 함 |
| `GameStateChanged(None, Logo)` 발행 | `Logo → "Logo"` 매핑은 있지만 이미 `Logo` 씬이 활성 씬이므로 로드 스킵 |
| 요청한 씬이 이미 활성 씬 | 로드 스킵 |
| 씬 재진입으로 SceneLoader 두 번째 생성 | Awake에서 중복 감지 → Destroy(gameObject) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | `GameStateChanged(None, Logo)` 발행 | `Logo`이 이미 활성 씬이므로 로드 스킵 (매핑은 존재하되 트리거 안 됨) |
| 2 | `GameStateChanged(Logo, Login)` 발행 | SceneLoader가 "Login" 씬 자동 로드 |
| 3 | 로딩 중 `LoadScene` 재호출 | 경고 로그, 기존 로딩에 영향 없음 |
| 4 | 존재하지 않는 씬 이름으로 `LoadScene` 호출 | 에러 로그 발생 후에도 `IsLoading == false`로 복구 |
| 5 | 씬 로드 완료 | `SceneLoadCompleted` 이벤트 발행, `IsLoading == false` |
| 6 | `GameStateChanged(InGame, Pause)` 발행 | 씬 로드 트리거 안 함 (매핑 없음) |
| 7 | 이미 활성 중인 씬으로 로드 요청 | 로드 스킵, 이벤트 미발행 |
| 8 | 씬 재진입으로 두 번째 SceneLoader 생성 | 두 번째 인스턴스 즉시 Destroy |

---

## 구현 시 주의사항

- **GameManager 직접 참조 금지**: `SceneLoader`는 `GameManager.Instance`를 참조하지 않고 반드시 `EventBus.Subscribe<GameStateChanged>`로만 연동.
- **`IsLoading` 복구**: 씬 로드 실패 시에도 `finally`로 `IsLoading = false`를 보장해 이후 로드가 영구히 막히지 않게 함.
- **로딩 화면은 의도적으로 제외**: 지금은 화면 전환 중 입력 차단이나 페이드가 없음. `LoadingScreen`을 나중에 추가할 때 `SceneLoader.LoadScene` API 시그니처를 유지한 채 내부 코루틴에 Fade 단계만 끼워 넣을 수 있도록 구조를 열어둠.
- **Addressables 도입 시**: `SceneManager.LoadSceneAsync(string)` 호출부만 Addressables API로 교체. 씬 이름 문자열 대신 키/레퍼런스 체계로 바뀔 수 있음.

---

## 구현 후 체크리스트

- [ ] `SceneLoader.cs` 작성 (싱글턴, 상태-씬 매핑, 로드 코루틴)
- [ ] `GameEvents.cs`에 `SceneLoadRequested`, `SceneLoadCompleted` 추가
- [ ] Logo 씬의 `GameManagers` 오브젝트에 `SceneLoader` 추가
- [ ] Login / MainMenu / InGame 씬을 Build Settings에 등록
- [ ] 테스트 시나리오 7개 검증
- [ ] GameManager 연동 테스트 (`GameStateChanged` 발행 → 자동 씬 전환 확인)
- [ ] (추후) LoadingScreen 도입 시 별도 계획 문서 작성
