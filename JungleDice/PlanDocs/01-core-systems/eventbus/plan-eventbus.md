# EventBus 구현 계획

> 상위 문서: [공용 코어 시스템 설계 계획](../plan-core-systems.md) (시스템 목록 #6)
> Phase 1 — 첫 번째 구현 대상  
> 의존 관계: 없음 (모든 시스템이 이것에 의존)

---

## 설계 목표

- 시스템 간 직접 참조 제거 → 결합도 최소화
- 구독 해제 누락으로 인한 메모리 릭 방지
- Unity 메인 스레드 기준 동작 (멀티스레드 불필요)
- 제네릭 기반 타입 안전성 보장
- 사용법이 단순하고 직관적일 것

---

## 핵심 설계 결정

### 이벤트 타입: `record` 구조체 사용

```csharp
// 이벤트는 순수 데이터 컨테이너
public record PlayerGoldChanged(int Before, int After);
public record SceneLoadRequested(string SceneName);
public record GameStateChanged(GameState Previous, GameState Next);
```

- `record`는 불변(immutable) + 값 기반 동등성 → 이벤트 객체로 적합
- 클래스 상속 계층 없음, 각 이벤트가 독립적인 타입

### 구독 해제: `IDisposable` 토큰 방식

```csharp
// 구독 시 토큰 반환
IDisposable token = EventBus.Subscribe<PlayerGoldChanged>(OnGoldChanged);

// 해제 — OnDestroy 또는 using 블록에서
token.Dispose();
```

- `Unsubscribe` 메서드 없음 → 토큰을 잃어버리면 해제 불가 구조로 강제
- `CompositeDisposable`로 여러 구독을 묶어서 한번에 해제 지원

### 발행 중 구독/해제 안전성

- 발행 루프 중 컬렉션 변경 방지: 발행 시 리스너 목록 스냅샷 복사 후 순회

---

## 클래스 구조

```
EventBus (static)
├── Subscribe<T>(Action<T>) → IDisposable
├── Publish<T>(T)
└── Clear()                              ← 씬 전환 시 전체 정리용 (주의 필요)

Subscription<T> : IDisposable            ← 토큰 구현체 (내부 클래스)

CompositeDisposable : IDisposable        ← 다중 구독 묶음 해제 헬퍼
```

---

## 파일 구성

```
Assets/
└── Scripts/
    └── Core/
        └── Event/
            ├── EventBus.cs
            ├── CompositeDisposable.cs
            └── GameEvents.cs            ← 프로젝트 공통 이벤트 정의 모음
```

---

## 상세 구현 명세

### EventBus.cs

```csharp
public static class EventBus
{
    // 타입별 리스너 딕셔너리
    // Key: 이벤트 타입, Value: 델리게이트 목록
    private static readonly Dictionary<Type, List<Delegate>> _listeners = new();

    /// <summary>이벤트 구독. 반환된 토큰을 Dispose하면 구독 해제.</summary>
    public static IDisposable Subscribe<T>(Action<T> listener)

    /// <summary>이벤트 발행. 해당 타입 구독자 전체에게 전달.</summary>
    public static void Publish<T>(T evt)

    /// <summary>
    /// 모든 구독 해제. 씬 전환 등 완전 초기화 시 사용.
    /// 주의: DontDestroyOnLoad 시스템의 구독도 제거되므로 신중히 호출.
    /// </summary>
    public static void Clear()
    
    /// <summary>특정 타입의 구독만 해제.</summary>
    public static void Clear<T>()
}
```

#### Publish 내부 동작 흐름

```
Publish<T>(evt) 호출
    │
    ├─ _listeners에 T 타입 키가 없으면 → 즉시 반환
    │
    ├─ 현재 리스너 목록을 배열로 스냅샷 복사
    │   (발행 중 구독/해제가 일어나도 안전)
    │
    └─ 스냅샷 순회하며 각 Action<T> 호출
        └─ 예외 발생 시: 해당 리스너만 건너뛰고 나머지 계속 호출
           (한 리스너의 예외가 전체 발행을 막지 않음)
```

### Subscription\<T\> (내부 클래스)

```csharp
private sealed class Subscription<T> : IDisposable
{
    private Action<T> _listener;
    private bool _disposed;

    // Dispose 호출 시 EventBus._listeners에서 자신을 제거
    // 중복 Dispose 안전 처리
}
```

### CompositeDisposable.cs

```csharp
/// <summary>
/// 여러 IDisposable을 하나로 묶어 일괄 해제하는 헬퍼.
/// MonoBehaviour의 멤버로 선언하고 OnDestroy에서 Dispose 호출.
/// </summary>
public sealed class CompositeDisposable : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Add(IDisposable disposable)
    public void Dispose()   // 전체 해제
}
```

#### 사용 패턴

```csharp
public class GoldUI : MonoBehaviour
{
    private readonly CompositeDisposable _subs = new();

    void OnEnable()
    {
        _subs.Add(EventBus.Subscribe<PlayerGoldChanged>(OnGoldChanged));
        _subs.Add(EventBus.Subscribe<GameStateChanged>(OnStateChanged));
    }

    void OnDestroy() => _subs.Dispose();   // 한 줄로 전체 해제
}
```

### GameEvents.cs — 공통 이벤트 정의

```csharp
// 씬 시스템
public record SceneLoadRequested(string SceneName, FadeType Transition);
public record SceneLoadCompleted(string SceneName);

// 게임 상태
public record GameStateChanged(GameState Previous, GameState Next);

// 오디오
public record BgmChangeRequested(AudioID Id, float FadeTime);
public record SfxPlayRequested(AudioID Id);

// 설정
public record VolumeChanged(AudioChannel Channel, float Value);
public record LanguageChanged(string LanguageCode);

// 입력
public record SwipeEvent(Vector2 Direction, float Speed);
public record BackButtonPressed();
```

> 카드게임 전용 이벤트는 별도 `CardGameEvents.cs`에 정의 (Core와 분리)

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 발행 중에 구독 추가 | 스냅샷 순회라 현재 발행에는 포함 안 됨, 다음 발행부터 적용 |
| 발행 중에 구독 해제 | 스냅샷 순회라 현재 발행은 호출됨, 다음 발행부터 제외 |
| 리스너에서 예외 발생 | try-catch로 감싸 나머지 리스너는 계속 호출, 예외는 로그 출력 |
| 토큰 중복 Dispose | 두 번째 Dispose는 무시 (bool 플래그) |
| 구독자 없는 타입 Publish | 딕셔너리 키 없으면 즉시 반환, 예외 없음 |
| Clear() 후 남은 토큰 Dispose | 이미 제거된 리스너 → 무해하게 무시 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | Subscribe → Publish | 리스너 호출 1회 |
| 2 | Subscribe → Dispose → Publish | 리스너 호출 없음 |
| 3 | 같은 타입 구독자 3개 → Publish | 3개 모두 호출 |
| 4 | Publish 중 Dispose 호출 | 현재 발행은 호출, 이후 발행은 제외 |
| 5 | 리스너 내부에서 예외 발생 | 나머지 리스너 정상 호출 |
| 6 | CompositeDisposable.Dispose | 묶인 구독 전체 해제 |
| 7 | 구독자 없는 타입 Publish | 예외 없이 정상 반환 |
| 8 | Dispose 중복 호출 | 두 번째 무시, 예외 없음 |

---

## 구현 시 주의사항

- **`Clear()` 남용 금지**: `DontDestroyOnLoad` 오브젝트의 구독까지 제거됨. 씬 전환 시 호출하면 GameManager 등 영구 시스템의 구독도 끊김. 일반적으로 호출할 일 없음.
- **람다 구독 시 해제 불가 패턴 주의**: 토큰을 변수에 저장하지 않으면 해제 불가. 항상 `_subs.Add(EventBus.Subscribe<T>(...))` 패턴 사용.
- **이벤트 무한 루프 주의**: 리스너 내부에서 같은 타입 Publish 호출 시 재귀 가능. 스냅샷 방식이라 무한루프는 아니지만 매 프레임 반복 발행 시 성능 문제.
- **Unity 메인 스레드 전용**: Thread-safe 구현 아님. 코루틴이나 async/await에서 호출 시 반드시 메인 스레드 보장 필요.

---

## 구현 후 체크리스트

- [ ] `EventBus.cs` 작성
- [ ] `CompositeDisposable.cs` 작성
- [ ] `GameEvents.cs` 기본 이벤트 정의
- [ ] 유닛 테스트 8개 작성 및 통과
- [ ] GameManager 연동 테스트 (다음 단계 연결 확인)
