# UIManager 구현 계획

> 상위 문서: 없음 — [공용 옵션 패널 구현 계획](../../07-option/plan-option-panel.md)에서 필요가 처음 제기됐으나(아래 "배경" 참고), `OptionPanel`에 종속되지 않는 범용 시스템이라 특정 기능 문서의 자식으로 두지 않고 `01-core-systems/`의 독립 시스템으로 배치한다.
> 관련 문서: [공용 코어 시스템 설계 계획](../plan-core-systems.md)(시스템 목록 #4 `UIManager`가 명시한 책임 — 팝업 스택 관리, Android 백버튼, 레이어 정렬, "UI 풀링(자주 쓰이는 팝업 재사용)" — 중 "UI 풀링" 부분을 이 문서가 구현한다. 나머지는 [UIManager 팝업 스택·레이어·백버튼 구현 계획](plan-uimanager-popupstack.md)(2단계)로 이어짐), [공용 옵션 패널 구현 계획](../../07-option/plan-option-panel.md)(이 시스템의 첫 실사용 소비자 — `OptionPanel.Configure` 연결까지 완료됨)
> 의존 관계: `UnityEngine.Resources`, `UnityEngine.Object.Instantiate` — 그 외 프로젝트 내부 시스템에는 의존하지 않는다(패널 타입에 대해 완전히 제네릭).
> 범위: 타입 인자 하나로 프리팹을 찾아 생성·캐시하고, 씬 전환 시 캐시를 자동으로 무효화하는 `UIManager` 정적 클래스까지.

---

## 배경 / 문제 인식

`OptionPanel`(메인메뉴·인게임이 공유하는 옵션 패널, [plan-option-panel.md](../../07-option/plan-option-panel.md))을 "각 씬이 시작될 때 인스턴스화"하는 방법을 논의하며 몇 단계를 거쳤다:

1. 씬 매니저가 프리팹 참조(`[SerializeField]`)와 부모 Transform을 직접 들고 `Instantiate` — 씬 매니저마다 이 세 필드(프리팹, 부모, 모드)가 반복되고, 패널 종류가 늘어날 때마다 씬 매니저가 계속 비대해짐.
2. 씬마다 `OptionPanelLoader` 컴포넌트를 별도로 배치 — 책임은 분리되지만 여전히 패널 종류별로 로더 컴포넌트를 하나씩 씬에 배치해야 함.
3. `OptionPanel` 전용 정적 클래스(`OptionPanelManager.Get(mode, parent)`) — `SpriteManager`처럼 `Resources.Load` 기반 정적 클래스로 가되, 패널 타입이 늘어나면 그때마다 전용 매니저 클래스를 새로 만들어야 하는 문제가 남음.

이 문서는 3번을 제네릭화한 결과다 — `Load<T>()` 형태로 호출하면 어떤 `MonoBehaviour` 기반 패널이든(꼭 `OptionPanel`이 아니어도) 같은 매니저 하나로 로드·생성·캐시할 수 있게 한다.

`plan-core-systems.md`의 로드맵은 `UIManager`(#4)에게 팝업 스택 관리·Android 백버튼·레이어 정렬·UI 풀링까지 포괄하는 훨씬 큰 책임을 맡겨뒀다. 이 문서는 그 이름을 그대로 가져와, 이 단계에서는 "UI 풀링(패널 인스턴스 로드/재사용)"만 구현한다. 나머지 세 책임(팝업 스택, 백버튼, 레이어 정렬)과 로딩/토스트/확인 팝업 공통 제공은 [UIManager 팝업 스택·레이어·백버튼 구현 계획](plan-uimanager-popupstack.md)(2단계)에서 같은 `UIManager` 클래스를 확장하는 방식으로 이어간다.

---

## 설계 목표

- 사용처(씬 매니저 등)는 프리팹 참조나 `Resources` 경로를 몰라도 `UIManager.Load<T>()` 한 줄로 원하는 패널을 가져올 수 있어야 한다.
- 패널 타입이 늘어나도 `UIManager` 자체는 수정하지 않는다 — 완전히 제네릭.
- 씬 전환으로 이전 인스턴스가 파괴되면, 별도의 씬 언로드 이벤트 구독 없이도 다음 요청 때 자동으로 새로 생성해야 한다.
- 최초 생성 시점에만 실행돼야 하는 타입별 설정(예: `OptionPanel.Configure(mode)`)을 `UIManager`가 그 존재조차 몰라도 호출부가 주입할 수 있어야 한다.

---

## 핵심 설계 결정

### 1. 제네릭 `Load<T>` + `Dictionary<Type, MonoBehaviour>` 캐시

**후보 검토:**

| 후보 | 기각/채택 사유 |
|------|----------------|
| 패널 타입마다 전용 매니저 클래스(`OptionPanelManager`, `ConfirmPanelManager`, ...) | 기각 — 패널이 늘어날 때마다 거의 동일한 코드(로드+생성+캐시)를 가진 클래스를 계속 새로 만들어야 함 |
| **`Type`을 키로 쓰는 `Dictionary<Type, MonoBehaviour>` 캐시 + 제네릭 `Load<T>`** | **채택** — 매니저 코드는 한 번만 작성하고, 새 패널 타입은 `Load<NewPanel>(...)` 호출만 추가하면 됨. 타입별 캐시라 서로 다른 패널이 섞이지 않음 |

```csharp
public static class UIManager
{
    private static readonly Dictionary<Type, MonoBehaviour> _instances = new();

    public static T Load<T>(Transform parent, Action<T> onCreated = null) where T : MonoBehaviour
    {
        if (_instances.TryGetValue(typeof(T), out var cached) && cached != null)
            return (T)cached;

        var prefab = Resources.Load<T>($"UI/{typeof(T).Name}");
        var instance = UnityEngine.Object.Instantiate(prefab, parent);
        _instances[typeof(T)] = instance;
        onCreated?.Invoke(instance);
        return instance;
    }
}
```

### 2. 프리팹 조회는 인스펙터 필드가 아니라 `Resources.Load` 경로 컨벤션

**후보 검토:**

| 후보 | 기각/채택 사유 |
|------|----------------|
| 사용처마다 `[SerializeField] private T _prefab;` 필드로 프리팹 연결 | 기각 — 애초에 이 문서가 풀려던 문제(씬 매니저마다 프리팹 참조 필드가 반복되는 것)로 되돌아감 |
| **`Resources.Load<T>($"UI/{typeof(T).Name}")`로 타입명 기반 조회** | **채택** — `SpriteManager`(`Resources/Sprite/{category}/{name}`), `AudioSystem`(`Resources/Audio/{folder}/{id}`)이 이미 쓰고 있는 프로젝트 관례와 동일한 패턴 |

`Resources/UI/` 폴더 아래 프리팹 파일명을 타입명과 정확히 일치시켜야 한다(`OptionPanel` 타입이면 `OptionPanel.prefab`). 대소문자까지 일치해야 한다 — Windows에서는 무시되지만 Android 빌드 등 대소문자를 구분하는 파일시스템에서는 실패할 수 있다.

### 3. 캐시 무효화는 Unity의 "파괴된 오브젝트는 `== null`" 처리에 그대로 의존

`if (_instances.TryGetValue(typeof(T), out var cached) && cached != null)`에서 `cached != null` 비교가 핵심이다. `UnityEngine.Object`는 `==`/`!=` 연산자를 오버로드해서, C# 참조 자체는 남아있어도 그 오브젝트가 파괴됐으면 `== null`이 `true`가 되도록 만들어준다. 그래서:

1. `MainMenu` 씬에서 `Load<OptionPanel>(...)` 호출 → 캐시에 없으니 새로 생성해 저장.
2. `MainMenu` 씬이 언로드되면 그 인스턴스도 씬과 함께 파괴됨 → 캐시엔 여전히 참조가 남아있지만 "파괴된 오브젝트"라 다음 비교에서 `cached != null`이 `false`.
3. `InGame` 씬에서 `Load<OptionPanel>(...)` 호출 → 캐시가 무효로 판정되어 새로 생성.

`SceneLoadCompleted` 같은 이벤트를 구독해 캐시를 수동으로 비우는 코드가 필요 없다 — 이 프로젝트에 아직 없는 새로운 패턴을 들여오는 대신, Unity 엔진 자체의 동작을 활용해 상태 관리를 단순하게 유지한다.

### 4. `onCreated`는 새로 생성될 때만 실행 — 캐시 히트에서는 실행되지 않음

```csharp
public static T Load<T>(Transform parent, Action<T> onCreated = null) where T : MonoBehaviour
{
    if (_instances.TryGetValue(typeof(T), out var cached) && cached != null)
        return (T)cached; // onCreated를 거치지 않고 곧바로 반환

    var prefab = Resources.Load<T>($"UI/{typeof(T).Name}");
    var instance = UnityEngine.Object.Instantiate(prefab, parent);
    _instances[typeof(T)] = instance; // onCreated 실행 전에 캐시부터 저장
    onCreated?.Invoke(instance);
    return instance;
}
```

`OptionPanel.Configure(OptionPanelMode)`처럼 "생애 첫 1회만 실행돼야 하는 설정"을 `UIManager`가 그 존재 자체를 몰라도 호출부가 람다로 주입할 수 있다 — `OptionManager.BindVolumeSliders`를 `Awake`에서 1회만 호출하는 것과 같은 이유(중복 실행 방지)를, 캐시 계층에서 한 번 더 보장하는 셈이다. 캐시 저장을 `onCreated` 호출보다 먼저 두는 이유는, `onCreated`가 예외를 던지더라도 인스턴스 자체는 이미 씬에 존재하므로(반쯤 초기화된 상태라도) 캐시가 그 존재를 알고 있어야 다음 `Load<T>` 호출이 또 다른 인스턴스를 중복 생성하지 않기 때문이다.

---

## 클래스 구조

```
UIManager                                             (신규, Core/UI/, static class)
├── _instances : Dictionary<Type, MonoBehaviour>       ← private static, 타입별 캐시
└── Load<T>(Transform parent, Action<T> onCreated = null) : T where T : MonoBehaviour
        ← 캐시 확인(파괴됐으면 무효) → 없으면 Resources.Load<T>($"UI/{typeof(T).Name}") + Instantiate
          + 캐시 저장 + onCreated 실행(있으면)
```

---

## 파일 구성

```
Assets/
├── Scripts/
│   └── Core/
│       └── UI/                      ← 기존(빈 폴더로 이미 존재, UIManager용으로 예약돼 있었음), 첫 스크립트
│           └── UIManager.cs         ← 신규
└── Resources/
    └── UI/                          ← 신규 폴더
        └── (패널 프리팹들, 예: OptionPanel.prefab — 실제 이동은 이 문서 범위 밖)
```

`Core/UI/`는 로드맵이 `UIManager`용으로 이미 예약해 둔 폴더였다 — 이름을 그대로 물려받아 이 문서가 그 폴더의 첫 스크립트가 된다.

---

## 상세 구현 명세

### UIManager.cs

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace JungleDice.Core.UI
{
    public static class UIManager
    {
        private static readonly Dictionary<Type, MonoBehaviour> _instances = new();

        public static T Load<T>(Transform parent, Action<T> onCreated = null) where T : MonoBehaviour
        {
            if (_instances.TryGetValue(typeof(T), out var cached) && cached != null)
                return (T)cached;

            var prefab = Resources.Load<T>($"UI/{typeof(T).Name}");
            var instance = UnityEngine.Object.Instantiate(prefab, parent);
            _instances[typeof(T)] = instance;
            onCreated?.Invoke(instance);
            return instance;
        }
    }
}
```

### 실사용 예시 ([plan-option-panel.md](../../07-option/plan-option-panel.md)/[plan-option-scenes.md](../../07-option/plan-option-scenes.md)에서 실제로 연결됨)

```csharp
_optionPanel = UIManager.Load<OptionPanel>(_canvasTransform, p => p.Configure(OptionPanelMode.MainMenu));
_optionButton.onClick.AddListener(_optionPanel.Show);
```

---

## 이번 범위에서 제외

- **로드맵이 `UIManager`에 요구한 나머지 책임(팝업 스택 관리, Android 백버튼, 레이어 정렬, 로딩/토스트/확인 팝업)** — [UIManager 팝업 스택·레이어·백버튼 구현 계획](plan-uimanager-popupstack.md)(2단계)에서 다룬다.
- **제네릭 `Load<T>` 이외의 API(`Unload<T>`, `IsLoaded<T>` 등)** — 지금까지 필요하다고 확인된 것은 "가져오기"뿐이다. 명시적으로 캐시를 비우거나 파괴해야 하는 시나리오가 생기면 그때 추가.
- **`Resources` 대신 Addressables 사용** — `plan-core-systems.md`가 "Addressables 도입 여부는 리소스 규모 확정 후 결정"이라고 이미 보류해뒀다. 지금은 `AudioSystem`/`SpriteManager`와 동일하게 `Resources` 기반으로 통일.
- **비동기 로드(`Resources.LoadAsync`)** — 패널 하나 로드하는 비용은 무시할 만한 수준이라 동기 `Resources.Load`로 충분하다는 전제(`SpriteManager`/`AudioSystem`과 동일한 판단).

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 같은 프레임(또는 같은 씬 생애 중) `Load<T>()`를 여러 번 호출 | 두 번째 호출부터는 캐시 히트 — 새로 생성하지 않고 기존 인스턴스를 그대로 반환, `onCreated`도 재실행되지 않음 |
| `Resources/UI/{타입명}` 경로에 프리팹이 없음(파일명 불일치·삭제 등) | `Resources.Load<T>`가 `null` 반환 → `UnityEngine.Object.Instantiate(null, parent)`에서 예외 발생 — 방어 코드 없음, 인스펙터 연결 누락과 동일하게 즉시 드러나는 쪽을 택함 |
| `onCreated` 콜백이 예외를 던짐 | 인스턴스는 이미 캐시에 저장된 뒤이므로, 이후 `Load<T>()` 호출은 "설정이 안 끝난" 그 인스턴스를 그대로 반환한다(재시도하지 않음) — `onCreated`가 실패했다는 신호(예외)가 호출부까지 그대로 전파되므로 문제 자체는 조용히 묻히지 않는다 |
| 서로 다른 두 패널 타입을 각각 `Load<A>()`/`Load<B>()` | `Dictionary`가 `Type`을 키로 구분하므로 서로 독립적으로 캐시됨 — 섞이지 않음 |
| 에디터에서 플레이 모드를 껐다 켬(Domain Reload) | 정적 필드(`_instances`)가 Unity에 의해 자동으로 초기화됨 — 별도 리셋 코드 불필요 |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | `Load<OptionPanel>(canvasA, p => p.Configure(MainMenu))` 최초 호출 | `Resources`에서 로드 + `canvasA` 하위에 `Instantiate`, `onCreated`(Configure)가 실행됨 |
| 2 | 시나리오 1 직후 같은 타입으로 `Load<OptionPanel>(canvasA, p => flagSetTrue)` 재호출 | 캐시된 동일 인스턴스를 반환, 두 번째 `onCreated`(`flagSetTrue`)는 실행되지 않음 |
| 3 | 시나리오 1의 인스턴스가 속한 씬을 언로드한 뒤 `Load<OptionPanel>(canvasB, p => p.Configure(InGame))` 호출 | 이전 인스턴스는 파괴되어 캐시가 무효로 판정됨 → `canvasB` 하위에 새 인스턴스 생성, `onCreated`가 다시 실행됨 |
| 4 | `Load<OptionPanel>(...)`와 `Load<SomeOtherPanel>(...)`를 각각 호출 | 서로 다른 캐시 슬롯에 저장되어 독립적으로 동작 |
| 5 | `Resources/UI/`에 해당 타입의 프리팹이 없는 상태에서 `Load<T>()` 호출 | 예외 발생(크래시) — 문제가 조용히 묻히지 않고 즉시 드러남 |

---

## 구현 시 주의사항

- **프리팹 파일명을 타입명과 정확히 일치시킨다(대소문자 포함).** 경로 컨벤션(`UI/{typeof(T).Name}`)이 어긋나면 별도 에러 로그 없이 `Instantiate` 단계에서 곧바로 예외가 난다.
- **`onCreated`는 "새로 생성될 때만" 실행된다는 계약을 호출부가 알고 있어야 한다.** 이미 존재하는 인스턴스에 설정을 다시 적용하고 싶다면 `Load<T>()`가 아니라 반환된 인스턴스에 직접 호출해야 한다.
- **`UIManager`는 캐시된 인스턴스를 절대 명시적으로 `Destroy`하지 않는다** — 오직 씬 언로드에 의한 자연 파괴에만 의존한다. 같은 씬 안에서(씬 전환 없이) 패널을 "새로 초기화"하고 싶은 시나리오에는 이 캐시 방식이 맞지 않는다.
- **`Assets/Resources/UI/` 폴더가 실제로 만들어지기 전까지는 `Load<T>()`가 항상 예외를 던진다** — 이 문서만으로는 아직 `OptionPanel` 프리팹이 그 자리에 없으므로, 추후 연결 문서에서 이동 작업이 선행돼야 한다.
- **이 클래스가 로드맵의 `UIManager` 이름을 그대로 가져왔다는 점을 유의한다** — 팝업 스택/백버튼/레이어 정렬 같은 나머지 책임은 새 클래스를 만드는 대신 이 `UIManager`를 확장하는 방식으로 [2단계 문서](plan-uimanager-popupstack.md)가 이어간다.

---

## 구현 후 체크리스트

- [x] `UIManager.cs` 작성 (`Assets/Scripts/Core/UI/`)
- [x] `Assets/Resources/UI/` 폴더 생성
- [x] `OptionPanel` 연결 완료 — [plan-option-panel.md](../../07-option/plan-option-panel.md)/[plan-option-scenes.md](../../07-option/plan-option-scenes.md)
- [ ] 테스트 시나리오 5개 검증 — Unity 에디터 Play 모드 필요
- [ ] [UIManager 팝업 스택·레이어·백버튼 구현 계획](plan-uimanager-popupstack.md)(2단계)으로 이동
