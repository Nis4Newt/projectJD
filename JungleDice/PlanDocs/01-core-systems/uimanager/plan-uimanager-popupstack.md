# UIManager 팝업 스택·백버튼 구현 계획

> 상위 문서: [UIManager 구현 계획](plan-uimanager.md) (2단계, `Load<T>` 캐시 이후)
> 관련 문서: [공용 코어 시스템 설계 계획](../plan-core-systems.md)(시스템 목록 #4 `UIManager`가 요구한 나머지 책임 — 팝업 스택 관리, Android 백버튼, 로딩/토스트/확인 팝업 공통 제공 — 을 이 문서가 마저 구현)
> 의존 관계: [UIManager 구현 계획](plan-uimanager.md)의 `UIManager.Load<T>`(재사용), `UnityEngine.Input`(백버튼 폴링), `GameManagers`(`DontDestroyOnLoad` 루트, 드라이버 부착 대상), `SceneLoadRequested`(`SceneLoader`가 씬 전환 직전 발행 — `UIManagerDriver`가 구독해 `ClearStack()` 호출)
> 범위: `UIPanel` 베이스 클래스, 팝업 스택(`Show<T>`/`HideTop`/`HideAll`), Android 백버튼 자동 처리까지 구현한다. **레이어(HUD/Panel/Popup/Toast/SystemModal) 기반의 공유 Canvas 정렬 시스템은 도입하지 않는다** — 각 패널이 자기 `Canvas`를 직접 포함하거나(프리팹) 씬에 이미 배치돼있으므로(`Register<T>`), `UIManager`가 그리기 순서를 관리할 이유가 없다(핵심 설계 결정 6 참고). `ShowToast`/`ShowConfirm` API와 그 대상인 `ToastPanel`/`ConfirmPanel`(코드·프리팹 모두)은 설계만 문서에 남기고 이번 구현에서는 제외한다 — 아직 이 API를 실제로 호출하는 소비자가 없어, 생기는 시점에 함께 만든다. `LoadingScreen`도 범위 밖.

---

## 배경 / 문제 인식

[UIManager 구현 계획](plan-uimanager.md)(1단계)은 `plan-core-systems.md`가 `UIManager`(#4)에게 맡긴 다섯 책임 중 "UI 풀링(자주 쓰이는 팝업 재사용)"만 `Load<T>`로 구현했다. 나머지가 남아있다:

- 팝업 스택 관리 (열기/닫기/뒤로가기)
- Android 백버튼 → 최상단 팝업 닫기 자동 처리
- 로딩/토스트/확인 팝업 공통 제공

이 문서가 이 세 가지를 `UIManager`에 마저 얹는다. `OptionPanel`([plan-option-panel.md](../../07-option/plan-option-panel.md))은 이 시스템에 편입시키지 않는다 — 볼륨 확정 저장·`Time.timeScale` 전환처럼 자기만의 여닫힘 로직이 이미 있고, 여러 팝업이 겹쳐 쌓이는 상황도 아니라(각 씬에 하나뿐, 전용 버튼으로만 여닫음) 스택 관리 대상으로 삼을 이유가 없다. 이 문서가 새로 만드는 `UIPanel` 베이스는 앞으로 생길 "여러 개가 겹쳐 쌓일 수 있는" 팝업 종류에만 적용된다.

---

## 설계 목표

- 화면에 여러 팝업이 동시에 열려도 "가장 최근에 연 것부터 닫는" 순서가 자동으로 보장돼야 한다.
- Android 백버튼을 누르면 최상단 팝업 하나만 닫히고, 스택이 비어있으면 아무 일도 일어나지 않아야 한다(앱을 직접 종료시키지 않음).
- 토스트/확인 팝업처럼 자주 쓰이는 패턴은 한 줄 API(`ShowToast`/`ShowConfirm`)로 띄울 수 있어야 한다.
- 1단계의 `UIManager.Load<T>` 캐시 메커니즘을 그대로 재사용한다 — 팝업도 결국 "타입별로 하나씩 재사용하는 패널"이라는 점은 동일하다. 프리팹이 이미 생성돼있으면 다시 만들지 않고 그 인스턴스를 그대로 보여준다.

---

## 핵심 설계 결정

### 5. `UIPanel` 추상 베이스 클래스 — Open/Close 공통 인터페이스

**후보 검토:**

| 후보 | 기각/채택 사유 |
|------|----------------|
| `interface IUIPanel` | 기각 — 공통 동작(기본 `Open`/`Close` 구현)을 담을 곳이 없어 구현체마다 `gameObject.SetActive` 같은 뻔한 코드가 중복됨 |
| **`abstract class UIPanel : MonoBehaviour`** | **채택** — 기본 `Open`/`Close` 구현을 제공하고, 필요한 패널만 오버라이드 |

```csharp
public abstract class UIPanel : MonoBehaviour
{
    public virtual void Open() => gameObject.SetActive(true);
    public virtual void Close() => gameObject.SetActive(false);
}
```

### 6. 공유 레이어 Canvas는 만들지 않는다 — 패널이 자기 `Canvas`를 직접 포함하거나 씬 Canvas를 그대로 쓴다

`UIManager`는 정적 클래스라 씬과 무관하게 살아있는 Canvas 계층을 스스로 관리하지 않는다. `Resources/UI/`의 프리팹에서 `Load<T>`로 처음 인스턴스화되는 패널(예: `OptionPanel`, `ConfirmPanel`)은 자기 `Canvas`(+`CanvasScaler`+`GraphicRaycaster`)를 직접 포함시킨다 — 어느 부모 밑에 `Instantiate`되든 스스로 렌더링돼야 하기 때문이다. `UIManager`는 그 프리팹을 인스턴스화해서 원하는 부모 밑에 두기만 하면 되고, 레이어/정렬 순서를 알 필요가 없다.

```csharp
// Resources.Load로 처음 인스턴스화되는 프리팹 루트에 직접 포함
Canvas (RenderMode: ScreenSpaceOverlay)
├── CanvasScaler (Scale With Screen Size, 참조 해상도는 다른 씬 Canvas와 동일하게 맞춘다)
└── GraphicRaycaster
```

**단, 씬에 이미 배치돼있는 패널(`FriendPanel`/`StorePanel`, 핵심 설계 결정 11)은 이 규칙 밖이다** — `Register<T>`는 `Instantiate`도 `SetParent`도 하지 않고 참조만 캐시에 저장하므로, 씬에 원래 있던 위치(대개 이미 그 씬의 Canvas 하위)에 그대로 남는다. 즉 자기 `Canvas`가 필요한 건 "런타임에 어디로 튈지 모르는" 프리팹 인스턴스뿐이고, "씬에 고정 배치된 뒤 캐시만 등록되는" 패널은 원래 있던 Canvas 계층을 그대로 쓰면 된다.

`UIManagerDriver`는 백버튼 폴링만 담당하는 얇은 드라이버다.

```csharp
public class UIManagerDriver : MonoBehaviour
{
    private void Update() => UIManager.HandleBackButton();
}
```

**트레이드오프**: 여러 패널이 동시에 열렸을 때(예: `FriendPanel` 위에 `StorePanel`) 어느 게 위에 그려질지는 더 이상 `UIManager`가 보장하지 않는다 — 각 프리팹의 `Canvas.sortOrder`를 직접 설정해서 관리해야 한다(기본값 0으로 두면 Unity가 대략 생성/활성화 순서로 정렬하지만 확정적이지 않음). 지금은 실제로 동시에 여러 패널이 열리는 시나리오가 없어([popupstack 이번 범위에서 제외] 참고) 당장 문제가 되지 않는다 — 필요해지면 그때 각 프리팹에 `sortOrder` 값을 명시적으로 배정한다.

### 7. 팝업 스택은 `Load<T>` 위에 얇게 얹는다 — `Show<T>`/`HideTop`/`HideAll`

`Show<T>`는 부모 `Transform`을 인자로 받는다는 것만 빼면 `Load<T>`를 그대로 감싸는 얇은 래퍼다.

```csharp
private static readonly Stack<UIPanel> _popupStack = new();

public static T Show<T>(Transform parent, Action<T> onCreated = null) where T : UIPanel
{
    var panel = Load<T>(parent, onCreated); // 이미 생성돼있으면 캐시된 인스턴스를 그대로 재사용
    panel.Open();
    _popupStack.Push(panel);
    EventBus.Publish(new PopupStackChanged(HasOpenPanel));
    return panel;
}

public static void HideTop()
{
    if (_popupStack.Count == 0) return;
    _popupStack.Pop().Close();
    EventBus.Publish(new PopupStackChanged(HasOpenPanel));
}

public static void HideAll()
{
    if (_popupStack.Count == 0) return;

    while (_popupStack.Count > 0)
        _popupStack.Pop().Close();
    EventBus.Publish(new PopupStackChanged(HasOpenPanel));
}
```

`HideAll`은 [메인메뉴 고정형 레이아웃 전환](../../02-scenemanager/mainmenuscene/plan-mainmenuscene-fixedlayout.md)에서 처음 쓰인다 — 패널마다 자기 닫기 버튼을 두지 않고, 호출부(`MainMenuSceneManager`)가 공용 닫기 버튼 하나로 열려있는 패널을 전부 닫는 용도. `HideTop`(백버튼, 한 단계씩 되돌아가기)과 `HideAll`(공용 닫기 버튼, 전부 닫기)은 서로 다른 UX를 위해 공존한다.

`Load<T>`가 이미 "타입별로 하나만 생성, 있으면 재사용" 캐시를 갖고 있으므로(1단계), `Show<T>`를 같은 타입으로 여러 번 호출해도 `Instantiate`가 다시 일어나지 않는다 — 인스턴스가 이미 있으면 그 패널을 그대로 다시 `Open()`할 뿐이다.

### 8. Android 백버튼: `UIManagerDriver.Update()`에서 매 프레임 폴링, 스택 최상단만 닫음

```csharp
public static void HandleBackButton()
{
    if (_popupStack.Count > 0 && Input.GetKeyDown(KeyCode.Escape))
        HideTop();
}
```

`KeyCode.Escape`가 Android 백버튼과 매핑되는 것은 Unity의 기존 관례다. `plan-core-systems.md`의 `InputManager`(#8)가 아직 미구현이라, 새 Input System 액션을 추가하는 대신 레거시 `Input` API로 최소 구현한다 — `InputManager` 도입 시 이 폴링을 그쪽으로 옮기는 것을 재검토한다. 스택이 비어있으면(열린 팝업이 없으면) 아무것도 하지 않는다 — 앱 종료는 OS/Unity 기본 동작에 맡기고 직접 처리하지 않는다.

### 9. `ShowToast`/`ShowConfirm`: 토스트는 스택에 안 쌓이고, 확인 팝업은 쌓인다

```csharp
public static void ShowToast(string message, Transform parent)
{
    var toast = Load<ToastPanel>(parent);
    toast.Show(message);
}

public static ConfirmPanel ShowConfirm(string message, Transform parent, Action onYes, Action onNo)
{
    var confirm = Show<ConfirmPanel>(parent);
    confirm.Setup(message, onYes, onNo);
    return confirm;
}
```

`ShowToast`는 `Show<T>`가 아니라 `Load<T>`를 직접 쓴다 — 토스트는 몇 초 뒤 스스로 사라지는 알림이라 "뒤로가기로 닫는 스택 대상"이 아니다. 스택에 넣으면 토스트가 자동으로 사라진 뒤에도 스택에는 항목이 남아, 다음 백버튼이 이미 사라진 토스트를 대상으로 또 `Close()`를 호출하는 불일치가 생긴다. `ShowConfirm`은 반대로 사용자가 반드시 응답해야 하는 모달이라 스택에 참여시킨다. `ToastPanel`/`ConfirmPanel`도 다른 패널과 마찬가지로 자기 `Canvas`를 포함하고(핵심 설계 결정 6), `ConfirmPanel`은 항상 최상단에 보이도록 그 `Canvas.sortOrder`를 프리팹에서 높게(예: 999) 설정해둔다 — `UIManager`가 레이어로 관리해주지 않으므로 프리팹 쪽 책임이다.

### 10. 스택 변경은 `EventBus`로 알린다 — `PopupStackChanged`

호출부가 스택 상태에 따라 자기 UI(예: 옵션 버튼 ↔ 닫기 버튼 전환)를 갱신해야 하는 경우가 생겼다. `Show`/`HideTop`으로 직접 호출한 경로는 호출부가 결과를 이미 알지만, 백버튼(`HandleBackButton`→`HideTop`)으로 패널이 닫히는 경로는 호출부가 알 방법이 없다 — 그래서 스택이 바뀔 때마다 `EventBus`로 알린다(`plan-eventbus.md` 패턴 그대로, `GameEvents.cs`에 추가).

```csharp
// GameEvents.cs
public record PopupStackChanged(bool HasOpenPanel);
```

```csharp
public static bool HasOpenPanel => _popupStack.Count > 0;
```

소비자는 `_subs.Add(EventBus.Subscribe<PopupStackChanged>(e => ...))`로 구독하고, `OnAwake()`에서 `UIManager.HasOpenPanel`로 초기 상태를 한 번 반영한다(이벤트는 "변경"에만 발행되므로 최초 상태는 직접 조회해야 함).

### 11. 씬에 이미 배치된 패널은 `Register<T>`로 캐시에 미리 등록한다

`Load<T>`는 캐시에 없으면 `Resources.Load<T>($"UI/{typeof(T).Name}")`로 프리팹을 찾아 새로 만든다. 그런데 [메인메뉴 고정형 레이아웃 전환](../../02-scenemanager/mainmenuscene/plan-mainmenuscene-fixedlayout.md)의 `FriendPanel`/`StorePanel`은 `Resources/UI/`의 프리팹이 아니라 **MainMenu 씬에 처음부터 배치돼있는 인스턴스**다 — 이 상태에서 그냥 `Show<T>`를 호출하면 캐시가 비어있으니 `Resources.Load`가 실패하거나(프리팹이 없으므로), 설령 같은 이름의 프리팹이 있어도 씬에 이미 있는 인스턴스를 무시하고 매번 새로 `Instantiate`해버린다.

```csharp
public static void Register<T>(T instance) where T : MonoBehaviour => _instances[typeof(T)] = instance;
```

**등록은 패널 자신의 `Awake()`가 스스로 한다 — 씬을 소비하는 쪽(`MainMenuSceneManager` 등)은 어떤 패널이 있는지 몰라도 된다.** 소비자가 각 패널을 인스펙터로 받아 대신 등록하는 방식도 가능하지만, 그러면 패널이 늘어날 때마다 소비자 쪽 필드/등록 코드가 함께 늘어난다 — 패널이 자기 자신을 등록하면 씬에 그 오브젝트를 두는 것만으로 등록이 끝나고, 소비자는 `Show<T>` 호출 시 타입만 알면 된다.

```csharp
private void Awake()
{
    gameObject.SetActive(false);
    UIManager.Register(this); // 자기 자신을 캐시에 등록
    // ...
}
```

`Load<T>`/`Show<T>`는 그 뒤로 캐시 히트로 이 인스턴스를 그대로 재사용하고, `Resources.Load`는 아예 호출되지 않는다. 즉 `Resources/UI/` 프리팹과 "씬 배치 + 자가 `Register`" 두 가지 모두 같은 `_instances` 캐시를 채우는 방법일 뿐이고, 어느 쪽을 쓸지는 소비 씬의 사정에 달렸다 — `OptionPanel`처럼 여러 씬에서 재사용되면 프리팹 쪽이, `FriendPanel`/`StorePanel`처럼 한 씬 전용이면 씬 배치 쪽이 더 간단하다. `Register`는 `SetParent`를 하지 않으므로, 씬 배치 쪽을 택한 패널은 자기 `Canvas`를 따로 포함할 필요가 없다(핵심 설계 결정 6의 "단," 참고) — 원래 있던 씬 Canvas 밑에 그대로 있으면 된다.

### 12. 씬 전환 시 `_popupStack`만 비운다 — `_instances` 캐시는 건드리지 않는다

`_popupStack`/`_instances`는 둘 다 `UIManager`의 static 필드라 씬을 넘나들며 살아남는다. 그런데 `FriendPanel`처럼 씬에 배치된 패널을 스택에 쌓아둔 채로 다른 씬(예: InGame)으로 전환하면, 그 패널 GameObject는 씬 언로드로 파괴되지만 `_popupStack`에는 파괴된 참조가 그대로 남는다 — 이후 `HideTop()`/`HideAll()`이 그 참조에 `Close()`를 호출하면 `MissingReferenceException`이 나고, `HasOpenPanel`도 실제로 열린 패널이 없는데 계속 `true`를 반환해 [slideremoval 문서](../../02-scenemanager/mainmenuscene/plan-mainmenuscene-fixedlayout-slideremoval.md)의 옵션/닫기 버튼 토글이 잘못된 상태로 남는다.

```csharp
// 씬 전환 시 이전 씬 소속 패널이 파괴되므로, Close() 호출 없이 스택만 비운다
public static void ClearStack() => _popupStack.Clear();
```

`SceneLoader`가 실제 씬 전환 직전에 발행하는 `SceneLoadRequested`를 `UIManagerDriver`가 구독해 호출한다 — `GameManagers`(DontDestroyOnLoad)에 붙어있어 앱 생명주기 동안 계속 살아있으므로, `SceneLoader.LoadSceneRoutine`이 `SceneManager.LoadSceneAsync`를 호출하기 전에 항상 먼저 정리된다.

```csharp
private void Awake() => EventBus.Subscribe<SceneLoadRequested>(_ => UIManager.ClearStack());
```

`_instances` 캐시는 건드리지 않는다 — `FriendPanel`/`StorePanel`처럼 씬에 배치된 패널은 씬이 다시 로드될 때 `Awake()`가 다시 실행되며 스스로 `Register(this)`로 캐시를 새 인스턴스로 덮어쓰고(결정 11), `OptionPanel`처럼 `Resources/UI/` 프리팹인 패널은 `Load<T>`가 호출되는 시점에 캐시 값이 파괴됐는지(`cached != null`)를 확인해 파괴된 항목만 제거하고 새로 만든다(`_instances.Remove` 후 재생성). 씬 전환 시점에 캐시를 일괄 비우면 아직 파괴되지 않은(다른 씬에서도 쓰이는) 인스턴스까지 불필요하게 버리게 되므로, 캐시 정리는 항상 "실제로 쓰려는 시점"(`Load<T>` 호출 시)에 맡긴다.

`ClearStack()`은 `Close()`를 호출하지 않고 스택만 비운다 — 스택에 남은 패널은 곧 씬 언로드로 파괴될 대상이라 `Close()`(`SetActive(false)`)가 의미가 없고, `PopupStackChanged`도 발행하지 않는다(어차피 그 이벤트를 구독하던 이전 씬의 `MainMenuSceneManager`도 함께 파괴되는 중이라 의미 없는 알림).

---

## 클래스 구조

```
UIPanel : MonoBehaviour, abstract                     (신규, Core/UI/)
├── Open()                                              ← virtual
└── Close()                                             ← virtual

UIManager                                             (기존, Core/UI/, static class — 1단계에 이어 확장)
├── _instances : Dictionary<Type, MonoBehaviour>       ← 기존(1단계)
├── _popupStack : Stack<UIPanel>                        ← 신규
├── HasOpenPanel : bool                                 ← 신규, _popupStack.Count > 0
├── Register<T>(T instance) where T : MonoBehaviour     ← 신규, 씬에 이미 배치된 인스턴스를 캐시에 등록
├── Load<T>(...)                                        ← 기존(1단계), 파괴된 캐시 항목은 제거 후 재생성하도록 보강
├── Show<T>(Transform parent, Action<T> onCreated = null) : T where T : UIPanel   ← 신규, Load<T> 래퍼, 스택 변경 후 PopupStackChanged 발행
├── HideTop()                                           ← 신규, 스택 변경 후 PopupStackChanged 발행
├── HideAll()                                           ← 신규, 스택을 비울 때까지 Pop+Close 반복 후 PopupStackChanged 발행
├── HandleBackButton()                                  ← 신규, UIManagerDriver.Update에서 매 프레임
├── ClearStack()                                        ← 신규, 씬 전환 시 UIManagerDriver가 호출, _popupStack만 Clear
├── ShowToast(string message, Transform parent)         ← 신규, Load 기반(스택 미참여)
└── ShowConfirm(string message, Transform parent, Action onYes, Action onNo) : ConfirmPanel   ← 신규, Show 기반(스택 참여)

UIManagerDriver : MonoBehaviour                        (신규, Core/UI/, GameManagers에 부착 — 인스펙터 연결 없음)
├── Awake() → EventBus.Subscribe<SceneLoadRequested>(_ => UIManager.ClearStack())
└── Update() → UIManager.HandleBackButton()

ToastPanel : UIPanel                                    (신규, Core/UI/, 코드 스텁만 — 프리팹은 범위 밖)
└── Show(string message)                                ← 일정 시간 후 스스로 Close

ConfirmPanel : UIPanel                                  (신규, Core/UI/, 코드 스텁만 — 프리팹은 범위 밖)
└── Setup(string message, Action onYes, Action onNo)
```

---

## 파일 구성

```
Assets/
├── Scripts/
│   └── Core/
│       └── UI/
│           ├── UIManager.cs         ← 기존, 확장(Show/HideTop/HideAll/HandleBackButton/ShowToast/ShowConfirm 추가)
│           ├── UIPanel.cs           ← 신규
│           ├── UIManagerDriver.cs   ← 신규
│           ├── ToastPanel.cs        ← 신규(코드 스텁 — 프리팹 없이는 동작 안 함)
│           └── ConfirmPanel.cs      ← 신규(코드 스텁 — 프리팹 없이는 동작 안 함)
└── Resources/
    └── UI/
        └── (ToastPanel.prefab, ConfirmPanel.prefab — 실제 제작은 범위 밖, 만들 때 자기 Canvas 포함 필수)
```

---

## 상세 구현 명세

### UIManager.cs (기존 파일, 확장)

```csharp
using System;
using System.Collections.Generic;
using JungleDice.Core.Event;
using UnityEngine;

namespace JungleDice.Core.UI
{
    public static class UIManager
    {
        private static readonly Dictionary<Type, MonoBehaviour> _instances = new();
        private static readonly Stack<UIPanel> _popupStack = new();

        public static bool HasOpenPanel => _popupStack.Count > 0;

        public static T Load<T>(Transform parent, Action<T> onCreated = null) where T : MonoBehaviour
        {
            if (_instances.TryGetValue(typeof(T), out var cached))
            {
                if (cached != null) return (T)cached;
                _instances.Remove(typeof(T)); // 씬 전환 등으로 파괴된 참조 — 새로 채운다
            }

            var prefab = Resources.Load<T>($"UI/{typeof(T).Name}");
            var instance = UnityEngine.Object.Instantiate(prefab, parent);
            _instances[typeof(T)] = instance;
            onCreated?.Invoke(instance);
            return instance;
        }

        public static T Show<T>(Transform parent, Action<T> onCreated = null) where T : UIPanel
        {
            var panel = Load<T>(parent, onCreated);
            panel.Open();
            _popupStack.Push(panel);
            EventBus.Publish(new PopupStackChanged(HasOpenPanel));
            return panel;
        }

        public static void HideTop()
        {
            if (_popupStack.Count == 0) return;
            _popupStack.Pop().Close();
            EventBus.Publish(new PopupStackChanged(HasOpenPanel));
        }

        public static void HideAll()
        {
            if (_popupStack.Count == 0) return;

            while (_popupStack.Count > 0)
                _popupStack.Pop().Close();
            EventBus.Publish(new PopupStackChanged(HasOpenPanel));
        }

        public static void HandleBackButton()
        {
            if (_popupStack.Count > 0 && Input.GetKeyDown(KeyCode.Escape))
                HideTop();
        }

        public static void ClearStack() => _popupStack.Clear();

        public static void ShowToast(string message, Transform parent)
        {
            var toast = Load<ToastPanel>(parent);
            toast.Show(message);
        }

        public static ConfirmPanel ShowConfirm(string message, Transform parent, Action onYes, Action onNo)
        {
            var confirm = Show<ConfirmPanel>(parent);
            confirm.Setup(message, onYes, onNo);
            return confirm;
        }
    }
}
```

### UIPanel.cs (신규)

```csharp
using UnityEngine;

namespace JungleDice.Core.UI
{
    public abstract class UIPanel : MonoBehaviour
    {
        public virtual void Open() => gameObject.SetActive(true);
        public virtual void Close() => gameObject.SetActive(false);
    }
}
```

### UIManagerDriver.cs (신규)

```csharp
using JungleDice.Core.Event;
using UnityEngine;

namespace JungleDice.Core.UI
{
    public class UIManagerDriver : MonoBehaviour
    {
        private void Awake() => EventBus.Subscribe<SceneLoadRequested>(_ => UIManager.ClearStack());
        private void Update() => UIManager.HandleBackButton();
    }
}
```

### ToastPanel.cs / ConfirmPanel.cs (신규, 코드 스텁)

```csharp
using TMPro;
using UnityEngine;

namespace JungleDice.Core.UI
{
    public class ToastPanel : UIPanel
    {
        [SerializeField] private TextMeshProUGUI _messageText;
        [SerializeField] private float _duration = 2f;

        public void Show(string message)
        {
            _messageText.text = message;
            Open();
            CancelInvoke(nameof(Close));
            Invoke(nameof(Close), _duration);
        }
    }
}
```

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JungleDice.Core.UI
{
    public class ConfirmPanel : UIPanel
    {
        [SerializeField] private TextMeshProUGUI _messageText;
        [SerializeField] private Button _yesButton;
        [SerializeField] private Button _noButton;

        private Action _onYes;
        private Action _onNo;

        private void Awake()
        {
            _yesButton.onClick.AddListener(() => { Close(); _onYes?.Invoke(); });
            _noButton.onClick.AddListener(() => { Close(); _onNo?.Invoke(); });
        }

        public void Setup(string message, Action onYes, Action onNo)
        {
            _messageText.text = message;
            _onYes = onYes;
            _onNo = onNo;
        }
    }
}
```

`ToastPanel`/`ConfirmPanel`은 `[SerializeField]` 참조(`_messageText`, `_yesButton`, `_noButton`)를 실제로 연결해줄 프리팹이 없으면 인스펙터 연결이 비어있는 채로 `Resources.Load`조차 실패한다(프리팹 자체가 `Resources/UI/`에 없으므로) — 이 두 클래스는 코드만 준비해두고, 프리팹은 실제 소비자가 생기는 시점에 함께 제작한다(그때 자기 `Canvas`를 포함시키는 것도 잊지 말 것 — 핵심 설계 결정 6).

---

## 이번 범위에서 제외

- **`ToastPanel`/`ConfirmPanel`의 실제 프리팹(비주얼) 제작** — 디자인 리소스와 Unity 에디터 작업이 필요하고, 현재 이 API를 실제로 호출하는 곳이 없어 급하지 않다. 소비자가 생기는 시점에 함께 만든다.
- **`LoadingScreen`(로딩 화면)** — 로드맵의 `UIManager` 책임 목록에 있었지만, `SceneLoader`(`plan-sceneloader.md`)가 이미 씬 전환 로딩을 다루고 있어 겹치는 부분을 어떻게 나눌지 조율이 필요하다. 이 문서에서 함께 설계하지 않는다.
- **여러 패널이 동시에 열렸을 때의 그리기 순서 보장** — 공유 레이어 시스템을 없앤 대가로, 이제 각 프리팹의 `Canvas.sortOrder`를 직접 관리해야 한다(핵심 설계 결정 6의 트레이드오프). 지금은 동시에 여러 패널이 열리는 실제 시나리오가 없어 다루지 않는다.
- **New Input System으로의 백버튼 처리 이관** — `InputManager`(#8) 도입 시 재검토.
- **`Show<T>()` 중복 호출 시 스택에 같은 인스턴스가 여러 번 쌓이는 것에 대한 방어** — 아직 실제 소비자가 없어 이게 실제로 문제가 되는지 확인되지 않았다. 아래 "엣지 케이스"에 남겨두고, 실제로 발생하면 그때 가드를 추가한다.

---

## 엣지 케이스 처리

| 상황 | 처리 방식 |
|------|-----------|
| 팝업이 하나도 없는 상태에서 백버튼 입력 | 아무 일도 일어나지 않음(앱이 종료되지 않음, OS/Unity 기본 동작에 맡김) |
| 같은 타입에 대해 `Show<T>()`를 연속 두 번 호출 | `Load<T>`가 같은 인스턴스를 반환하지만, `_popupStack`에는 같은 인스턴스가 두 번 쌓임 — `HideTop()`을 한 번 눌러도 완전히 닫히지 않고 스택에 남은 복제 항목 때문에 한 번 더 눌러야 함(위 "이번 범위에서 제외" 참고, 아직 방어 없음) |
| `UIManagerDriver`가 씬(`GameManagers`)에 아예 부착돼있지 않음 | 백버튼 폴링 자체가 동작하지 않음(`HandleBackButton()`이 호출되지 않으므로) — `Show<T>`/`HideTop`/`HideAll`은 `UIManagerDriver`와 무관하게 정상 동작하므로, 이 실수는 "백버튼이 안 먹힌다"는 형태로만 드러난다 |
| `Resources/UI/<T>.prefab`이 없는 상태에서 `Show<T>`/`Load<T>` 호출 | `Resources.Load<T>`가 `null` 반환 → `Instantiate(null, parent)`에서 예외 — 방어 없음, 프리팹 제작이 선행돼야 함 |
| `ToastPanel`이 화면에 떠있는 도중 같은 메시지로 `ShowToast`를 다시 호출 | 캐시된 동일 인스턴스를 재사용, `CancelInvoke` → `Invoke` 재예약으로 사라지는 타이머만 리셋되고 텍스트는 최신 메시지로 갱신됨 |
| 여러 패널이 동시에 열려 `Canvas.sortOrder`가 같은 값(기본 0)으로 겹침 | 어느 게 위에 그려질지 Unity 기본 정렬에 맡겨짐(확정적이지 않음) — 위 "이번 범위에서 제외" 참고, 필요해지면 프리팹별 `sortOrder`를 명시적으로 배정 |
| 패널이 열린 채(`_popupStack`에 쌓인 채) 다른 씬으로 전환됨 | `SceneLoadRequested` 구독이 씬 파괴 전에 `ClearStack()`을 호출해 스택을 비움 — 파괴된 참조에 `Close()`를 호출하는 일이 없고, 새 씬의 `HasOpenPanel`도 정확히 `false`로 시작 |
| `Load<T>`가 캐시된 인스턴스를 반환하려는 시점에 그 인스턴스가 이미 파괴돼있음(다른 씬 소속이었던 경우 등) | `cached != null` 검사로 감지해 `_instances`에서 제거 후 `Resources.Load`로 새로 생성 — `Register`로 등록된(프리팹 없는) 타입이면 `Resources.Load`도 실패해 예외(해당 타입은 씬이 다시 로드돼 스스로 재등록할 때까지 `Show<T>` 호출 자체를 하지 않아야 함) |

---

## 테스트 시나리오

| # | 시나리오 | 기대 결과 |
|---|----------|-----------|
| 1 | `Show<ConfirmPanel>(parent)` 호출 | 화면에 보임(자기 `Canvas`로 렌더링), 스택에 1개 |
| 2 | 시나리오 1 이후 백버튼(Escape) 입력 | `ConfirmPanel`이 `Close()`되어 사라짐, 스택 0개 |
| 3 | 스택이 빈 상태에서 백버튼 입력 | 아무 일도 일어나지 않음(예외 없음) |
| 4 | `ShowToast("저장됨", parent)` 호출 | 표시되고, 지정 시간 뒤 자동으로 사라짐 — 표시되는 동안 백버튼을 눌러도(스택에 없으므로) 토스트에 영향 없음 |
| 5 | `ShowConfirm("나가시겠습니까?", parent, onYes, onNo)` 호출 후 "예" 버튼 클릭 | `onYes` 콜백이 호출되고 패널이 닫힘, 스택에서도 제거됨(다음 백버튼이 다른 팝업을 대상으로 함) |
| 6 | 같은 타입에 `Show<T>(parent)`를 두 번 연달아 호출 | 두 번째 호출은 `Instantiate`가 일어나지 않고 캐시된 인스턴스를 그대로 재사용해서 `Open()`만 다시 호출 |
| 7 | `Show<FriendPanel>(parent)`로 스택에 1개 쌓인 상태에서 다른 씬으로 전환(`SceneLoadRequested` 발행) | 전환 직후 `UIManager.HasOpenPanel == false`, 씬 전환 도중/이후 예외 없음 |

---

## 구현 시 주의사항

- **`ShowToast`는 `Show<T>`가 아니라 `Load<T>`를 쓴다.** 토스트를 스택에 넣으면 백버튼 처리와 어긋난다(위 "핵심 설계 결정 9" 참고) — 새 팝업 종류를 추가할 때 "이게 스스로 사라지는 알림인지, 사용자가 닫아야 하는 모달인지"를 먼저 판단해서 `Load`/`Show` 중 맞는 쪽을 고를 것.
- **`ToastPanel`/`ConfirmPanel`의 프리팹이 없는 동안은 `ShowToast`/`ShowConfirm`을 호출하지 않는다** — 호출하면 즉시 예외가 난다.
- **`Show<T>`/`Load<T>`로 여는 모든 패널 프리팹은 자기 `Canvas`(+`CanvasScaler`+`GraphicRaycaster`)를 반드시 포함해야 한다** — `UIManager`가 더 이상 공유 Canvas를 준비해주지 않는다. 빠뜨리면 `Instantiate`는 성공하지만 화면에 아무것도 렌더링되지 않는다(부모 계층에 다른 `Canvas`가 없다면).
- **여러 패널이 동시에 열릴 가능성이 있는 프리팹은 `Canvas.sortOrder`를 직접 배정한다** — 특히 확인 모달처럼 항상 최상단이어야 하는 패널.
- **`ClearStack()`은 씬 전환 훅(`SceneLoadRequested`) 전용이다** — 일반적인 "전부 닫기" 용도로는 `HideAll()`을 쓴다(`Close()` 호출 + `PopupStackChanged` 발행 차이).

---

## 구현 후 체크리스트

- [x] `UIManager.cs`에 `Show`/`HideTop`/`HideAll`/`HandleBackButton`/`HasOpenPanel` 추가
- [x] `UIPanel.cs` 작성 (`Open`/`Close`만)
- [x] `UIManagerDriver.cs` 작성 — 백버튼 폴링만 담당
- [x] `UIManager.cs`에 `Register<T>` 추가 — 씬에 미리 배치된 패널(`FriendPanel`/`StorePanel`)을 캐시에 등록
- [x] `UIManager.cs`에 `ClearStack()` 추가, `Load<T>`가 파괴된 캐시 항목을 제거 후 재생성하도록 보강
- [x] `UIManagerDriver.cs`에 `SceneLoadRequested` 구독 추가 (`Awake()`에서 `UIManager.ClearStack()` 호출)
- [ ] **(이번 구현에서 제외)** `ToastPanel.cs`/`ConfirmPanel.cs`, `UIManager.ShowToast`/`ShowConfirm` — 문서 설계만 남기고 코드는 작성하지 않음
- [ ] `UIManagerDriver`를 `GameManagers`에 부착 (에디터 작업 — 컴포넌트 추가만 하면 되고 인스펙터 연결은 불필요)
- [ ] 테스트 시나리오 1~3, 6, 7 검증(`Show<T>`/`HideTop`/백버튼/캐시 재사용/씬 전환 정리 — 임시 `UIPanel` 구현체로 검증 가능) — Unity 에디터 Play 모드 필요
- [ ] (추후) `ToastPanel`/`ConfirmPanel` 코드 + 프리팹 제작(자기 `Canvas` 포함), `ShowToast`/`ShowConfirm` 구현 — 실제 소비자가 생기는 시점
- [ ] (추후) `LoadingScreen` 설계, `SceneLoader`와 책임 분담 조율
- [ ] (추후) `InputManager` 도입 시 백버튼 폴링을 그쪽으로 이관 검토
- [ ] (추후) 여러 패널 동시 오픈이 실제로 필요해지면 `Canvas.sortOrder` 배정 규칙 문서화
